using System.Security.Cryptography;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Commands;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Commands;

/// <summary>Storage for remote commands. Changes are saved through <see cref="IUnitOfWork"/>.</summary>
public interface IRemoteCommandRepository
{
    void Add(RemoteCommand command);

    Task<RemoteCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the commands of the device that are pending or sent, the oldest first.</summary>
    Task<IReadOnlyList<RemoteCommand>> GetUnfinishedAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent commands of the device, the newest first.</summary>
    Task<IReadOnlyList<RemoteCommand>> GetRecentAsync(Guid deviceId, int limit, CancellationToken cancellationToken = default);
}

public sealed record RemoteCommandDto(
    Guid Id,
    RemoteCommandType Type,
    string? Parameter,
    RemoteCommandStatus Status,
    string RequestedByUserName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    string? ResultMessage)
{
    public static RemoteCommandDto FromCommand(RemoteCommand command)
    {
        return new RemoteCommandDto(
            command.Id,
            command.Type,
            command.Parameter,
            command.Status,
            command.RequestedByUserName,
            command.RequestedAt,
            command.CompletedAt,
            command.ResultMessage);
    }
}

/// <summary>A command handed to the agent to carry out.</summary>
public sealed record AgentCommand(Guid Id, RemoteCommandType Type, string? Parameter);

/// <summary>The command cannot be sent to the device as it is now, for example because it is offline.</summary>
public sealed class RemoteCommandRefusedException(string message) : Exception(message);

public interface IRemoteCommandService
{
    /// <summary>Queues a command for the device. Whether the user may send it is checked by the caller.</summary>
    /// <returns>Null when no such device exists.</returns>
    /// <exception cref="ArgumentException">The parameter is not acceptable for the command type.</exception>
    /// <exception cref="RemoteCommandRefusedException">The device has no agent, is disabled or is not online.</exception>
    Task<RemoteCommandDto?> RequestAsync(
        AuthenticatedUser actor,
        Guid deviceId,
        RemoteCommandType type,
        string? parameter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the file and queues the command to receive it for the device. Whether the user may send files is checked
    /// by the caller.
    /// </summary>
    /// <returns>Null when no such device exists.</returns>
    /// <exception cref="ArgumentException">The file is too large or its name cannot be used.</exception>
    /// <exception cref="RemoteCommandRefusedException">The device has no agent, is disabled or is not online.</exception>
    Task<RemoteCommandDto?> SendFileAsync(
        AuthenticatedUser actor,
        Guid deviceId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent commands of the device, the newest first.</summary>
    Task<IReadOnlyList<RemoteCommandDto>> GetRecentAsync(Guid deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the file of a receive command the agent of the device is carrying out, or null for any other.</summary>
    /// <exception cref="AgentAuthenticationException">The credentials are not valid.</exception>
    Task<FileTransferDto?> GetFileForAgentAsync(AgentCredentials credentials, Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>Returns one part of a file's contents; the caller has checked the agent with <see cref="GetFileForAgentAsync"/>.</summary>
    Task<byte[]?> ReadFileChunkAsync(Guid commandId, int index, CancellationToken cancellationToken = default);

    /// <summary>Deletes the contents of files whose commands have finished, including expired ones.</summary>
    Task<int> DeleteFinishedFileContentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Hands the waiting commands of the device to its agent.</summary>
    /// <exception cref="AgentAuthenticationException">The credentials are not valid.</exception>
    Task<IReadOnlyList<AgentCommand>> FetchPendingAsync(AgentCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Records the result the agent reports for a command it was handed.</summary>
    /// <returns>False when the device has no such command waiting for a result.</returns>
    /// <exception cref="AgentAuthenticationException">The credentials are not valid.</exception>
    Task<bool> CompleteAsync(
        AgentCredentials credentials,
        Guid commandId,
        bool succeeded,
        string? message,
        CancellationToken cancellationToken = default);
}

public sealed class RemoteCommandService : IRemoteCommandService
{
    public const int RecentCommandCount = 50;

    /// <summary>More commands than this waiting for one device means something is flooding it.</summary>
    public const int MaxUnfinishedCommands = 20;

    private readonly IRemoteCommandRepository _commands;
    private readonly IFileTransferRepository _files;
    private readonly IDeviceRepository _devices;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RemoteCommandService(
        IRemoteCommandRepository commands,
        IFileTransferRepository files,
        IDeviceRepository devices,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _commands = commands;
        _files = files;
        _devices = devices;
        _history = history;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<RemoteCommandDto?> RequestAsync(
        AuthenticatedUser actor,
        Guid deviceId,
        RemoteCommandType type,
        string? parameter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (type == RemoteCommandType.SendFile)
        {
            throw new ArgumentException("A file is sent together with its contents, not as a plain command.", nameof(type));
        }

        if (await CreateCommandAsync(actor, deviceId, type, parameter, cancellationToken) is not var (command, device))
        {
            return null;
        }

        _history.AddEvent(new MonitoringEvent(
            command.RequestedAt,
            MonitoringEventType.RemoteCommand,
            EventSeverity.Info,
            $"User '{actor.UserName}' sent the command {Describe(command)} to device '{device.Name}'.",
            device.Id,
            actor.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return RemoteCommandDto.FromCommand(command);
    }

    public async Task<RemoteCommandDto?> SendFileAsync(
        AuthenticatedUser actor,
        Guid deviceId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(content);

        if (content.LongLength > FileTransfer.MaxSize)
        {
            throw new ArgumentException($"A file can have at most {FileTransfer.MaxSize / (1024 * 1024)} MB.", nameof(content));
        }

        if (await CreateCommandAsync(actor, deviceId, RemoteCommandType.SendFile, fileName, cancellationToken) is not var (command, device))
        {
            return null;
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(content));
        var transfer = new FileTransfer(command.Id, command.Parameter!, content.LongLength, sha256, command.RequestedAt);
        _files.Add(transfer);

        for (var index = 0; index < transfer.ChunkCount; index++)
        {
            var offset = index * FileTransfer.ChunkSize;
            _files.AddChunk(new FileTransferChunk(command.Id, index, content[offset..Math.Min(offset + FileTransfer.ChunkSize, content.Length)]));
        }

        _history.AddEvent(new MonitoringEvent(
            command.RequestedAt,
            MonitoringEventType.RemoteCommand,
            EventSeverity.Info,
            $"User '{actor.UserName}' sent the file '{transfer.FileName}' ({transfer.Size} bytes, SHA-256 {transfer.Sha256}) to device '{device.Name}'.",
            device.Id,
            actor.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return RemoteCommandDto.FromCommand(command);
    }

    public async Task<FileTransferDto?> GetFileForAgentAsync(
        AgentCredentials credentials,
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        var device = await AuthenticateAsync(credentials, cancellationToken);
        var command = await _commands.GetByIdAsync(commandId, cancellationToken);

        // Only the agent the file was sent to, and only while it is carrying the command out.
        if (command is not { Type: RemoteCommandType.SendFile, Status: RemoteCommandStatus.Sent } || command.DeviceId != device.Id)
        {
            return null;
        }

        return await _files.GetAsync(commandId, cancellationToken) is { } transfer ? FileTransferDto.FromTransfer(transfer) : null;
    }

    public Task<byte[]?> ReadFileChunkAsync(Guid commandId, int index, CancellationToken cancellationToken = default)
    {
        return _files.GetChunkAsync(commandId, index, cancellationToken);
    }

    public Task<int> DeleteFinishedFileContentsAsync(CancellationToken cancellationToken = default)
    {
        // A command older than its lifetime will expire however long nobody looks at it.
        return _files.DeleteContentOfFinishedCommandsAsync(_timeProvider.GetUtcNow() - RemoteCommand.Lifetime, cancellationToken);
    }

    /// <summary>
    /// Checks that the device can take a command now and creates it; the caller adds the event and saves.
    /// Returns null when no such device exists.
    /// </summary>
    private async Task<(RemoteCommand Command, Device Device)?> CreateCommandAsync(
        AuthenticatedUser actor,
        Guid deviceId,
        RemoteCommandType type,
        string? parameter,
        CancellationToken cancellationToken)
    {
        var device = await _devices.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
        {
            return null;
        }

        if (device.MonitoringMode != MonitoringMode.Agent)
        {
            throw new RemoteCommandRefusedException("Commands can be sent only to a device with an agent.");
        }

        if (!device.IsEnabled)
        {
            throw new RemoteCommandRefusedException("The device is disabled.");
        }

        // A command for a device that is not there would only wait and expire.
        if (device.Status != DeviceStatus.Online)
        {
            throw new RemoteCommandRefusedException("The device is not online.");
        }

        var now = _timeProvider.GetUtcNow();
        var unfinished = await ExpireOverdueAsync(deviceId, now, cancellationToken);

        if (unfinished.Count >= MaxUnfinishedCommands)
        {
            throw new RemoteCommandRefusedException("Too many commands are already waiting for this device.");
        }

        // The constructor validates the parameter for the type.
        var command = new RemoteCommand(Guid.NewGuid(), device.Id, device.Name, actor.Id, actor.UserName, type, parameter, now);

        _commands.Add(command);

        return (command, device);
    }

    public async Task<IReadOnlyList<RemoteCommandDto>> GetRecentAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        // Commands that ran out of time are closed here, so the list never shows one waiting forever.
        await ExpireOverdueAsync(deviceId, _timeProvider.GetUtcNow(), cancellationToken, save: true);

        return (await _commands.GetRecentAsync(deviceId, RecentCommandCount, cancellationToken))
            .Select(RemoteCommandDto.FromCommand)
            .ToList();
    }

    public async Task<IReadOnlyList<AgentCommand>> FetchPendingAsync(AgentCredentials credentials, CancellationToken cancellationToken = default)
    {
        var device = await AuthenticateAsync(credentials, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var pending = (await ExpireOverdueAsync(device.Id, now, cancellationToken))
            .Where(command => command.Status == RemoteCommandStatus.Pending)
            .ToList();

        foreach (var command in pending)
        {
            command.MarkSent(now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return pending.Select(command => new AgentCommand(command.Id, command.Type, command.Parameter)).ToList();
    }

    public async Task<bool> CompleteAsync(
        AgentCredentials credentials,
        Guid commandId,
        bool succeeded,
        string? message,
        CancellationToken cancellationToken = default)
    {
        var device = await AuthenticateAsync(credentials, cancellationToken);
        var command = await _commands.GetByIdAsync(commandId, cancellationToken);

        // An agent may report only on commands of its own device, and only once.
        if (command is null || command.DeviceId != device.Id || command.Status != RemoteCommandStatus.Sent)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();

        command.Complete(succeeded, message, now);
        _history.AddEvent(new MonitoringEvent(
            now,
            MonitoringEventType.RemoteCommand,
            succeeded ? EventSeverity.Info : EventSeverity.Warning,
            $"The command {Describe(command)} sent by '{command.RequestedByUserName}' {(succeeded ? "succeeded" : "failed")} on device '{device.Name}'.",
            device.Id,
            command.RequestedByUserId));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // The file has arrived or never will; its contents are not needed any more, only the audit record.
        if (command.Type == RemoteCommandType.SendFile)
        {
            await _files.DeleteContentAsync(command.Id, cancellationToken);
        }

        return true;
    }

    private static string Describe(RemoteCommand command)
    {
        return command.Parameter is null ? $"'{command.Type}'" : $"'{command.Type}' ({command.Parameter})";
    }

    private async Task<Device> AuthenticateAsync(AgentCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var device = await _devices.GetByIdAsync(credentials.DeviceId, cancellationToken);

        return AgentKey.IsValidFor(device, credentials)
            ? device!
            : throw new AgentAuthenticationException("Agent credentials are not valid.");
    }

    /// <summary>Closes the commands of the device that ran out of time and returns those still unfinished.</summary>
    private async Task<IReadOnlyList<RemoteCommand>> ExpireOverdueAsync(
        Guid deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool save = false)
    {
        var unfinished = await _commands.GetUnfinishedAsync(deviceId, cancellationToken);
        var expired = unfinished.Where(command => command.ExpireIfOverdue(now)).ToList();

        if (save && expired.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return unfinished.Except(expired).ToList();
    }
}
