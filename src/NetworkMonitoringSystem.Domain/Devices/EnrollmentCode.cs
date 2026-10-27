namespace NetworkMonitoringSystem.Domain.Devices;

public enum EnrollmentCodeState
{
    Valid = 0,
    Used = 1,
    Expired = 2,
    Revoked = 3,
}

/// <summary>
/// A code an administrator gives to one computer so that its agent can register: valid for a limited time and once.
/// A code made for an existing device can only register that device again; one without a device registers a new
/// computer. Only a hash of the code is kept, so the code cannot be read back from the database.
/// </summary>
public sealed class EnrollmentCode
{
    public const int MaxNoteLength = 200;

    public static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(7);

    public EnrollmentCode(
        Guid id,
        string codeHash,
        string? note,
        Guid? deviceId,
        DateTimeOffset createdAt,
        TimeSpan lifetime,
        string createdByUserName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Enrollment code identifier must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(codeHash))
        {
            throw new ArgumentException("Code hash must not be empty.", nameof(codeHash));
        }

        if (lifetime < MinLifetime || lifetime > MaxLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                $"An enrollment code must be valid for {MinLifetime.TotalMinutes:0} minutes to {MaxLifetime.TotalDays:0} days.");
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (note?.Length > MaxNoteLength)
        {
            throw new ArgumentException($"The note of an enrollment code must have at most {MaxNoteLength} characters.", nameof(note));
        }

        Id = id;
        CodeHash = codeHash;
        Note = note;
        DeviceId = deviceId;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + lifetime;
        CreatedByUserName = createdByUserName;
    }

    /// <summary>For Entity Framework, which sets the properties itself.</summary>
    private EnrollmentCode()
    {
        CodeHash = string.Empty;
        CreatedByUserName = string.Empty;
    }

    public Guid Id { get; private set; }

    public string CodeHash { get; private set; }

    /// <summary>What the administrator wrote to recognise the code, for example where the computer stands.</summary>
    public string? Note { get; private set; }

    /// <summary>The device the code registers again; null for a code that registers a new computer.</summary>
    public Guid? DeviceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public string CreatedByUserName { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>The host name of the computer that used the code.</summary>
    public string? UsedByHostName { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public EnrollmentCodeState StateAt(DateTimeOffset now)
    {
        if (UsedAt is not null)
        {
            return EnrollmentCodeState.Used;
        }

        if (RevokedAt is not null)
        {
            return EnrollmentCodeState.Revoked;
        }

        return now >= ExpiresAt ? EnrollmentCodeState.Expired : EnrollmentCodeState.Valid;
    }

    /// <summary>Spends the code on the registration of the computer.</summary>
    public void Use(string hostName, DateTimeOffset now)
    {
        if (StateAt(now) != EnrollmentCodeState.Valid)
        {
            throw new InvalidOperationException("Only a valid enrollment code can be used.");
        }

        UsedAt = now;
        UsedByHostName = hostName;
    }

    /// <summary>Makes a code that has not been used unusable.</summary>
    /// <returns>False when the code was already used, expired or revoked, so there was nothing to revoke.</returns>
    public bool Revoke(DateTimeOffset now)
    {
        if (StateAt(now) != EnrollmentCodeState.Valid)
        {
            return false;
        }

        RevokedAt = now;

        return true;
    }
}
