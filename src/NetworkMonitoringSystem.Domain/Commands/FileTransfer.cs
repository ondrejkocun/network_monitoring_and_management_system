using System.Text.RegularExpressions;

namespace NetworkMonitoringSystem.Domain.Commands;

/// <summary>
/// A file an administrator sent to a device. It belongs to the <see cref="RemoteCommand"/> of type
/// <see cref="RemoteCommandType.SendFile"/> with the same identifier. The name, size and hash stay as part of the audit;
/// the contents, kept in <see cref="FileTransferChunk"/>s, are deleted once the command has finished.
/// </summary>
public sealed partial class FileTransfer
{
    /// <summary>Largest file that can be sent. The whole file passes through the server's memory.</summary>
    public const long MaxSize = 100L * 1024 * 1024;

    /// <summary>Size of the parts the contents are stored and sent in.</summary>
    public const int ChunkSize = 1024 * 1024;

    public const int MaxFileNameLength = 200;

    public FileTransfer(Guid commandId, string fileName, long size, string sha256, DateTimeOffset createdAt)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("Command identifier must not be empty.", nameof(commandId));
        }

        if (size is < 0 or > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A file can have at most {MaxSize / (1024 * 1024)} MB.");
        }

        if (!Sha256Pattern().IsMatch(sha256))
        {
            throw new ArgumentException("The hash must be SHA-256 in 64 hexadecimal digits.", nameof(sha256));
        }

        CommandId = commandId;
        FileName = NormalizeFileName(fileName);
        Size = size;
        Sha256 = sha256.ToUpperInvariant();
        CreatedAt = createdAt;
    }

    private FileTransfer()
    {
        FileName = null!;
        Sha256 = null!;
    }

    public Guid CommandId { get; private set; }

    public string FileName { get; private set; }

    public long Size { get; private set; }

    /// <summary>SHA-256 of the contents in upper-case hexadecimal, checked by the agent after download.</summary>
    public string Sha256 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int ChunkCount => (int)((Size + ChunkSize - 1) / ChunkSize);

    /// <summary>
    /// Returns the name if a file of that name can be safely created on Windows, inside the inbox and nowhere else.
    /// A name is refused rather than altered, so that the administrator knows what the file will be called.
    /// </summary>
    /// <exception cref="ArgumentException">The name is empty, too long, contains a folder or a character Windows does
    /// not allow, or is a reserved device name such as CON or NUL.</exception>
    public static string NormalizeFileName(string? fileName)
    {
        var name = fileName?.Trim() ?? string.Empty;

        if (name.Length == 0 || name.Length > MaxFileNameLength)
        {
            throw new ArgumentException($"The file name must have 1 to {MaxFileNameLength} characters.", nameof(fileName));
        }

        // Slashes and colons would leave the inbox; the rest are not allowed in Windows file names at all.
        if (name.Any(character => char.IsControl(character) || "\\/:*?\"<>|".Contains(character)))
        {
            throw new ArgumentException("The file name must not contain a folder or any of the characters \\ / : * ? \" < > |.", nameof(fileName));
        }

        // Windows drops a trailing dot, so such a name would not be the one shown; it also rules out "." and "..".
        if (name.EndsWith('.'))
        {
            throw new ArgumentException("The file name must not end with a dot.", nameof(fileName));
        }

        if (ReservedDeviceName().IsMatch(name))
        {
            throw new ArgumentException("The file name is reserved by Windows for a device.", nameof(fileName));
        }

        return name;
    }

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Sha256Pattern();

    // CON, PRN, AUX, NUL, COM1-9 and LPT1-9 name devices, with or without an extension.
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedDeviceName();
}

/// <summary>One part of the contents of a <see cref="FileTransfer"/>.</summary>
public sealed class FileTransferChunk
{
    public FileTransferChunk(Guid commandId, int index, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, FileTransfer.ChunkSize, nameof(data));

        CommandId = commandId;
        Index = index;
        Data = data;
    }

    private FileTransferChunk()
    {
        Data = null!;
    }

    public Guid CommandId { get; private set; }

    public int Index { get; private set; }

    public byte[] Data { get; private set; }
}
