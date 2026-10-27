using System.Security.Cryptography;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Agents;

/// <summary>An enrollment code as an administrator sees it; the code itself is shown only once, when it is created.</summary>
public sealed record EnrollmentCodeDto(
    Guid Id,
    string? Note,
    Guid? DeviceId,
    string? DeviceName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string CreatedByUserName,
    EnrollmentCodeState State,
    DateTimeOffset? UsedAt,
    string? UsedByHostName);

/// <summary>A newly created code together with the code itself, which is never available again.</summary>
public sealed record CreatedEnrollmentCode(EnrollmentCodeDto Info, string Code);

/// <summary>Storage for enrollment codes. Changes are saved through <see cref="IUnitOfWork"/>.</summary>
public interface IEnrollmentCodeRepository
{
    Task<EnrollmentCode?> GetByHashAsync(string codeHash, CancellationToken cancellationToken = default);

    Task<EnrollmentCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Codes created since the given time, the newest first.</summary>
    Task<IReadOnlyList<EnrollmentCode>> GetCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    void Add(EnrollmentCode code);
}

/// <summary>Creates enrollment codes, lists and revokes them.</summary>
public interface IEnrollmentCodeService
{
    /// <param name="deviceId">The existing device the code registers again, or null for a new computer.</param>
    /// <returns>Null when there is no such device.</returns>
    /// <exception cref="ArgumentException">The note or the lifetime is not acceptable.</exception>
    Task<CreatedEnrollmentCode?> CreateAsync(
        AuthenticatedUser actor,
        string? note,
        Guid? deviceId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>Codes of the last <see cref="EnrollmentCodeService.ListedPeriod"/>, the newest first.</summary>
    Task<IReadOnlyList<EnrollmentCodeDto>> GetRecentAsync(CancellationToken cancellationToken = default);

    /// <returns>Null when there is no such code; otherwise the code, revoked unless it was already spent.</returns>
    Task<EnrollmentCodeDto?> RevokeAsync(AuthenticatedUser actor, Guid id, CancellationToken cancellationToken = default);
}

public sealed class EnrollmentCodeService : IEnrollmentCodeService
{
    /// <summary>How long codes stay in the list after they were created.</summary>
    public static readonly TimeSpan ListedPeriod = TimeSpan.FromDays(30);

    private readonly IEnrollmentCodeRepository _codes;
    private readonly IDeviceRepository _devices;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public EnrollmentCodeService(
        IEnrollmentCodeRepository codes,
        IDeviceRepository devices,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _codes = codes;
        _devices = devices;
        _history = history;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<CreatedEnrollmentCode?> CreateAsync(
        AuthenticatedUser actor,
        string? note,
        Guid? deviceId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Device? device = null;

        if (deviceId is { } id)
        {
            device = await _devices.GetByIdAsync(id, cancellationToken);

            if (device is not { MonitoringMode: MonitoringMode.Agent })
            {
                return null;
            }
        }

        var text = EnrollmentCodeText.Generate();
        var now = _timeProvider.GetUtcNow();
        EnrollmentCode code;

        try
        {
            code = new EnrollmentCode(Guid.NewGuid(), EnrollmentCodeText.Hash(text), note, device?.Id, now, lifetime, actor.UserName);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            // The message names the range; the parameter and value .NET appends mean nothing to the user.
            throw new ArgumentException(exception.Message.Split(" (Parameter")[0], nameof(lifetime), exception);
        }

        _codes.Add(code);
        _history.AddEvent(new MonitoringEvent(
            now,
            MonitoringEventType.EnrollmentCodeCreated,
            EventSeverity.Info,
            device is null
                ? $"User '{actor.UserName}' created an enrollment code for a new computer{DescribeNote(code)}, valid until {code.ExpiresAt:yyyy-MM-dd HH:mm} UTC."
                : $"User '{actor.UserName}' created an enrollment code for device '{device.Name}'{DescribeNote(code)}, valid until {code.ExpiresAt:yyyy-MM-dd HH:mm} UTC.",
            device?.Id,
            actor.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreatedEnrollmentCode(ToDto(code, device?.Name, now), text);
    }

    public async Task<IReadOnlyList<EnrollmentCodeDto>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var codes = await _codes.GetCreatedSinceAsync(now - ListedPeriod, cancellationToken);
        var names = new Dictionary<Guid, string?>();

        foreach (var deviceId in codes.Select(code => code.DeviceId).OfType<Guid>().Distinct())
        {
            names[deviceId] = (await _devices.GetByIdAsync(deviceId, cancellationToken))?.Name;
        }

        return codes
            .Select(code => ToDto(code, code.DeviceId is { } id ? names.GetValueOrDefault(id) : null, now))
            .ToList();
    }

    public async Task<EnrollmentCodeDto?> RevokeAsync(AuthenticatedUser actor, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var code = await _codes.GetByIdAsync(id, cancellationToken);

        if (code is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var deviceName = code.DeviceId is { } deviceId ? (await _devices.GetByIdAsync(deviceId, cancellationToken))?.Name : null;

        if (code.Revoke(now))
        {
            _history.AddEvent(new MonitoringEvent(
                now,
                MonitoringEventType.EnrollmentCodeRevoked,
                EventSeverity.Info,
                $"User '{actor.UserName}' revoked an enrollment code{DescribeNote(code)}.",
                code.DeviceId,
                actor.Id));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToDto(code, deviceName, now);
    }

    internal static string DescribeNote(EnrollmentCode code) => code.Note is null ? string.Empty : $" ('{code.Note}')";

    private static EnrollmentCodeDto ToDto(EnrollmentCode code, string? deviceName, DateTimeOffset now)
    {
        return new EnrollmentCodeDto(
            code.Id,
            code.Note,
            code.DeviceId,
            deviceName,
            code.CreatedAt,
            code.ExpiresAt,
            code.CreatedByUserName,
            code.StateAt(now),
            code.UsedAt,
            code.UsedByHostName);
    }
}

/// <summary>
/// The text form of enrollment codes: three groups of four characters, for example "K7QM-4XTD-9HPA", from an
/// alphabet without characters that are easy to confuse (0 and O, 1 and I). That is 60 random bits; with a limited
/// lifetime and one use that is far more than can be guessed.
/// </summary>
public static class EnrollmentCodeText
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 12;

    public static string Generate()
    {
        var characters = new char[Length];

        for (var index = 0; index < Length; index++)
        {
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var text = new string(characters);

        return $"{text[..4]}-{text[4..8]}-{text[8..]}";
    }

    /// <summary>
    /// The code as it is compared: without spaces and dashes, in capitals. Whatever a person typed or pasted, the same
    /// code gives the same text.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return string.Concat(text.Where(character => !char.IsWhiteSpace(character) && character != '-')).ToUpperInvariant();
    }

    public static string Hash(string text) => AgentKey.Hash(Normalize(text));
}
