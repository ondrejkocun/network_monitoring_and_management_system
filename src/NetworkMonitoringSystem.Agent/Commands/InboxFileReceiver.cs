using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Grpc.Core;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Agents;

namespace NetworkMonitoringSystem.Agent.Commands;

/// <summary>Receives a file the administrator sent to this device.</summary>
public interface IFileReceiver
{
    /// <summary>Downloads the file of the command and saves it. Never throws; a failure is reported as the result.</summary>
    Task<CommandResult> ReceiveAsync(AgentCommand command, AgentCredentials credentials, CancellationToken cancellationToken);
}

/// <summary>
/// Saves received files into one inbox folder and nowhere else. The name is checked again here, although the server
/// checks it too; nothing is overwritten, the size and SHA-256 hash are verified, and the file is never run.
/// </summary>
public sealed partial class InboxFileReceiver : IFileReceiver
{
    private readonly AgentApi.AgentApiClient _client;
    private readonly AgentOptions _options;
    private readonly ILogger<InboxFileReceiver> _logger;

    public InboxFileReceiver(AgentApi.AgentApiClient client, IOptions<AgentOptions> options, ILogger<InboxFileReceiver> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>The folder received files are saved into.</summary>
    public string InboxPath => Path.GetFullPath(string.IsNullOrWhiteSpace(_options.InboxPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkMonitoringSystem", "Inbox")
        : _options.InboxPath);

    public async Task<CommandResult> ReceiveAsync(AgentCommand command, AgentCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!IsSafeFileName(command.Parameter))
        {
            return CommandResult.Failure("The file name is not acceptable; the file was not downloaded.");
        }

        var inbox = PrepareInbox();
        var partialPath = Path.Combine(inbox, $".{command.Id}.part");

        try
        {
            using var download = _client.DownloadFile(
                new DownloadFileRequest { Credentials = credentials, CommandId = command.Id },
                cancellationToken: cancellationToken);

            FileHeader? header = null;
            long received = 0;
            string? failure = null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // The partial file is closed before it is kept or deleted.
            await using (var partial = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while (failure is null && await download.ResponseStream.MoveNext(cancellationToken))
                {
                    var part = download.ResponseStream.Current;

                    if (part.PartCase == DownloadFileReply.PartOneofCase.Header)
                    {
                        header = part.Header;

                        // The name must be the one the administrator was shown with the command.
                        if (header.FileName != command.Parameter || header.Size < 0)
                        {
                            failure = "The server described a different file than the command; it was not kept.";
                        }

                        continue;
                    }

                    received += part.Data.Length;

                    if (header is null || received > header.Size)
                    {
                        failure = "The server sent more data than the file has; it was not kept.";

                        continue;
                    }

                    hash.AppendData(part.Data.Span);
                    await partial.WriteAsync(part.Data.Memory, cancellationToken);
                }
            }

            if (failure is not null)
            {
                return Fail(partialPath, failure);
            }

            if (header is null || received != header.Size)
            {
                return Fail(partialPath, "The file arrived incomplete; it was not kept.");
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset());

            if (!string.Equals(sha256, header.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(partialPath, "The file arrived damaged (its SHA-256 hash does not match); it was not kept.");
            }

            var savedPath = MoveToFreeName(partialPath, inbox, header.FileName);
            _logger.LogInformation("File {FileName} ({Size} bytes) received into {Path}.", header.FileName, header.Size, savedPath);

            return CommandResult.Success(string.Create(
                CultureInfo.InvariantCulture,
                $"Saved as {savedPath} ({header.Size} bytes, SHA-256 {sha256})."));
        }
        catch (RpcException exception) when (exception.StatusCode != StatusCode.Cancelled)
        {
            return Fail(partialPath, $"The file could not be downloaded: {exception.Status.Detail}");
        }
        catch (IOException exception)
        {
            return Fail(partialPath, $"The file could not be saved: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Fail(partialPath, $"The file could not be saved: {exception.Message}");
        }
        catch (Exception exception) when (exception is OperationCanceledException or RpcException)
        {
            // A timeout or the agent stopping: the caller reports it, but the partial file must not stay behind.
            DeletePartial(partialPath);

            throw;
        }
    }

    /// <summary>
    /// True when the name is a plain file name that stays inside the folder: no folder part, no character Windows
    /// does not allow, no trailing dot and no device name such as CON or NUL.
    /// </summary>
    public static bool IsSafeFileName(string? fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName)
            && fileName.Length <= 200
            && fileName == fileName.Trim()
            && Path.GetFileName(fileName) == fileName
            && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !fileName.EndsWith('.')
            && !ReservedDeviceName().IsMatch(fileName);
    }

    private string PrepareInbox()
    {
        var inbox = InboxPath;
        var directory = new DirectoryInfo(inbox);

        // A service keeps what it receives for the system and administrators only, like its identity.
        if (_options.MachineWideIdentity)
        {
            if (directory.Exists)
            {
                directory.SetAccessControl(ProtectedFileIdentityStore.CreateRestrictedFolderSecurity());
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(inbox)!);
                directory.Create(ProtectedFileIdentityStore.CreateRestrictedFolderSecurity());
            }
        }
        else
        {
            Directory.CreateDirectory(inbox);
        }

        return inbox;
    }

    /// <summary>Gives the file its name, or "name (2).ext" and so on when a file of that name is already there.</summary>
    private static string MoveToFreeName(string partialPath, string inbox, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var number = 1; ; number++)
        {
            var candidate = Path.Combine(inbox, number == 1 ? fileName : $"{stem} ({number}){extension}");

            // The final path must still be inside the inbox, whatever the name was.
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(candidate)), inbox, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The file name leads out of the inbox.");
            }

            try
            {
                File.Move(partialPath, candidate, overwrite: false);

                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Taken; try the next number.
            }
        }
    }

    private CommandResult Fail(string partialPath, string message)
    {
        DeletePartial(partialPath);
        _logger.LogWarning("{Message}", message);

        return CommandResult.Failure(message);
    }

    private void DeletePartial(string partialPath)
    {
        try
        {
            File.Delete(partialPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "A partly received file could not be deleted: {Path}", partialPath);
        }
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedDeviceName();
}
