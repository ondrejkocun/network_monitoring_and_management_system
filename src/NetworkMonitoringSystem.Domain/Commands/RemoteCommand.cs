using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using NetworkMonitoringSystem.Domain.Access;

namespace NetworkMonitoringSystem.Domain.Commands;

/// <summary>
/// Everything that can be asked of a device remotely. The list is fixed: there is no way to run an arbitrary program.
/// </summary>
public enum RemoteCommandType
{
    RestartDevice = 1,
    ShutDownDevice = 2,
    KillProcess = 3,
    StartService = 4,
    StopService = 5,
    RestartService = 6,
    SynchronizeNow = 7,
    Ping = 8,
    Traceroute = 9,
    FlushDnsCache = 10,
}

public enum RemoteCommandStatus
{
    /// <summary>Waiting for the agent to pick it up.</summary>
    Pending = 0,

    /// <summary>Handed to the agent; the result has not arrived yet.</summary>
    Sent = 1,

    Succeeded = 2,

    Failed = 3,

    /// <summary>The agent did not pick it up or did not report a result in time.</summary>
    Expired = 4,
}

/// <summary>
/// A command a user asked a device to carry out. It is the queue entry for the agent and, at the same time,
/// the audit record: who asked, when, for what, on which device and how it ended.
/// </summary>
public sealed partial class RemoteCommand
{
    public const int MaxParameterLength = 253;
    public const int MaxResultLength = 4000;

    /// <summary>
    /// A command that waited this long is not carried out any more: a restart must not surprise anyone
    /// hours after it was asked for.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public RemoteCommand(
        Guid id,
        Guid deviceId,
        string deviceName,
        Guid requestedByUserId,
        string requestedByUserName,
        RemoteCommandType type,
        string? parameter,
        DateTimeOffset requestedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Command identifier must not be empty.", nameof(id));
        }

        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(deviceId));
        }

        if (requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("User identifier must not be empty.", nameof(requestedByUserId));
        }

        Id = id;
        DeviceId = deviceId;
        DeviceName = deviceName;
        RequestedByUserId = requestedByUserId;
        RequestedByUserName = requestedByUserName;
        Type = type;
        Parameter = NormalizeParameter(type, parameter);
        RequestedAt = requestedAt;
    }

    // Used when a stored command is read back: by then the device or the user may be gone.
    private RemoteCommand(
        Guid id,
        string deviceName,
        string requestedByUserName,
        RemoteCommandType type,
        string? parameter,
        DateTimeOffset requestedAt)
    {
        Id = id;
        DeviceName = deviceName;
        RequestedByUserName = requestedByUserName;
        Type = type;
        Parameter = parameter;
        RequestedAt = requestedAt;
    }

    public Guid Id { get; }

    /// <summary>Null once the device has been removed; the record stays.</summary>
    public Guid? DeviceId { get; private set; }

    /// <summary>Name of the device at the time of the request, kept for the audit.</summary>
    public string DeviceName { get; }

    /// <summary>Null once the user has been removed; the record stays.</summary>
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>Name of the user at the time of the request, kept for the audit.</summary>
    public string RequestedByUserName { get; }

    public RemoteCommandType Type { get; }

    /// <summary>The process identifier, service name or target address, depending on the type; null when the type takes none.</summary>
    public string? Parameter { get; }

    public RemoteCommandStatus Status { get; private set; } = RemoteCommandStatus.Pending;

    public DateTimeOffset RequestedAt { get; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? ResultMessage { get; private set; }

    public bool IsFinished => Status is RemoteCommandStatus.Succeeded or RemoteCommandStatus.Failed or RemoteCommandStatus.Expired;

    /// <summary>The permission a user needs on the device to ask for a command of this type.</summary>
    public static Permission PermissionFor(RemoteCommandType type)
    {
        return type switch
        {
            RemoteCommandType.RestartDevice => Permission.RestartDevice,
            RemoteCommandType.ShutDownDevice => Permission.ShutDownDevice,
            RemoteCommandType.KillProcess => Permission.KillProcess,
            RemoteCommandType.StartService => Permission.StartService,
            RemoteCommandType.StopService => Permission.StopService,
            RemoteCommandType.RestartService => Permission.RestartService,
            RemoteCommandType.SynchronizeNow => Permission.SynchronizeNow,
            RemoteCommandType.Ping => Permission.Ping,
            RemoteCommandType.Traceroute => Permission.Traceroute,
            RemoteCommandType.FlushDnsCache => Permission.FlushDnsCache,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown command type."),
        };
    }

    /// <summary>
    /// Checks the parameter against what the command type accepts and returns it in its stored form.
    /// </summary>
    /// <exception cref="ArgumentException">The type is unknown or the parameter is not acceptable for it.</exception>
    public static string? NormalizeParameter(RemoteCommandType type, string? parameter)
    {
        var text = parameter?.Trim() ?? string.Empty;

        switch (type)
        {
            case RemoteCommandType.RestartDevice:
            case RemoteCommandType.ShutDownDevice:
            case RemoteCommandType.SynchronizeNow:
            case RemoteCommandType.FlushDnsCache:
                return text.Length == 0
                    ? null
                    : throw new ArgumentException("This command takes no parameter.", nameof(parameter));

            case RemoteCommandType.KillProcess:
                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0
                    ? pid.ToString(CultureInfo.InvariantCulture)
                    : throw new ArgumentException("The process identifier must be a positive whole number.", nameof(parameter));

            case RemoteCommandType.StartService:
            case RemoteCommandType.StopService:
            case RemoteCommandType.RestartService:
                return ServiceNamePattern().IsMatch(text)
                    ? text
                    : throw new ArgumentException(
                        "The service name must have 1 to 80 characters: letters, digits, spaces and . _ - $ only.",
                        nameof(parameter));

            case RemoteCommandType.Ping:
            case RemoteCommandType.Traceroute:
                return IPAddress.TryParse(text, out var address)
                    ? address.ToString()
                    : HostNamePattern().IsMatch(text) && text.Length <= MaxParameterLength
                        ? text.ToLowerInvariant()
                        : throw new ArgumentException("The target must be an IP address or a host name.", nameof(parameter));

            default:
                throw new ArgumentException("Unknown command type.", nameof(type));
        }
    }

    /// <summary>Records that the agent has taken the command over.</summary>
    public void MarkSent(DateTimeOffset now)
    {
        if (Status != RemoteCommandStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending command can be sent.");
        }

        Status = RemoteCommandStatus.Sent;
        SentAt = now;
    }

    /// <summary>Records the result the agent reported.</summary>
    public void Complete(bool succeeded, string? message, DateTimeOffset now)
    {
        if (Status != RemoteCommandStatus.Sent)
        {
            throw new InvalidOperationException("Only a command the agent has taken over can be completed.");
        }

        Status = succeeded ? RemoteCommandStatus.Succeeded : RemoteCommandStatus.Failed;
        CompletedAt = now;
        ResultMessage = Shorten(message);
    }

    /// <summary>Gives up on a command that was not picked up or answered within <see cref="Lifetime"/>.</summary>
    /// <returns>True when the command expired now.</returns>
    public bool ExpireIfOverdue(DateTimeOffset now)
    {
        if (IsFinished || now - RequestedAt < Lifetime)
        {
            return false;
        }

        ResultMessage = Status == RemoteCommandStatus.Pending
            ? "The agent did not pick the command up in time."
            : "The agent took the command over but did not report a result.";
        Status = RemoteCommandStatus.Expired;
        CompletedAt = now;

        return true;
    }

    private static string? Shorten(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        // Control characters other than line breaks have no place in a result shown to a person.
        var clean = new string(message.Where(character => !char.IsControl(character) || character is '\n' or '\r' or '\t').ToArray()).Trim();

        return clean.Length <= MaxResultLength ? clean : clean[..MaxResultLength];
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.$\-][A-Za-z0-9_.$\- ]{0,79}$")]
    private static partial Regex ServiceNamePattern();

    // Labels of letters, digits and hyphens separated by dots; cannot start with a hyphen, so it can never look like an option.
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9\-]{0,62})(\.[A-Za-z0-9]([A-Za-z0-9\-]{0,62}))*$")]
    private static partial Regex HostNamePattern();
}
