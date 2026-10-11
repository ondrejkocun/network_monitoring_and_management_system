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

    /// <summary>Returns the most recent commands of the device, the newest first.</summary>
    Task<IReadOnlyList<RemoteCommandDto>> GetRecentAsync(Guid deviceId, CancellationToken cancellationToken = default);

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
    private readonly IDeviceRepository _devices;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RemoteCommandService(
        IRemoteCommandRepository commands,
        IDeviceRepository devices,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _commands = commands;
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
        _history.AddEvent(new MonitoringEvent(
            now,
            MonitoringEventType.RemoteCommand,
            EventSeverity.Info,
            $"User '{actor.UserName}' sent the command {Describe(command)} to device '{device.Name}'.",
            device.Id,
            actor.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return RemoteCommandDto.FromCommand(command);
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
