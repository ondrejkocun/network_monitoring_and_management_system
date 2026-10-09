namespace NetworkMonitoringSystem.Domain.Access;

/// <summary>A person who can sign in to the administration of the system.</summary>
public sealed class User
{
    public const int MaxUserNameLength = 100;

    /// <summary>After this many wrong passwords in a row the account is locked for <see cref="LockDuration"/>.</summary>
    public const int MaxFailedLogins = 5;

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly List<Role> _roles = [];

    public User(Guid id, string userName, string passwordHash, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User identifier must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > MaxUserNameLength)
        {
            throw new ArgumentException($"User name must have 1 to {MaxUserNameLength} characters.", nameof(userName));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash must not be empty.", nameof(passwordHash));
        }

        Id = id;
        UserName = NormalizeUserName(userName);
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    /// <summary>Stored in lower case, so that signing in does not depend on the case typed.</summary>
    public string UserName { get; }

    /// <summary>The password itself is never stored.</summary>
    public string PasswordHash { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; }

    public int FailedLoginCount { get; private set; }

    /// <summary>Until when signing in is refused after repeated wrong passwords; null when not locked.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public IReadOnlyList<Role> Roles => _roles;

    public static string NormalizeUserName(string userName) => userName.Trim().ToLowerInvariant();

    public bool IsLocked(DateTimeOffset now) => LockedUntil > now;

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash must not be empty.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }

    /// <returns>True when this attempt locked the account.</returns>
    public bool RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;

        if (FailedLoginCount < MaxFailedLogins)
        {
            return false;
        }

        FailedLoginCount = 0;
        LockedUntil = now + LockDuration;

        return true;
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    /// <returns>False when the user already has the role.</returns>
    public bool AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (_roles.Any(assigned => assigned.Id == role.Id))
        {
            return false;
        }

        _roles.Add(role);

        return true;
    }

    /// <returns>False when the user does not have the role.</returns>
    public bool RemoveRole(Guid roleId) => _roles.RemoveAll(role => role.Id == roleId) > 0;
}

/// <summary>
/// A signed-in session of a user. The client holds a random token; only its hash is stored.
/// </summary>
public sealed class UserSession
{
    public UserSession(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier must not be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Token hash must not be empty.", nameof(tokenHash));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "A session must expire after it was created.");
        }

        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public long Id { get; private set; }

    public Guid UserId { get; }

    public string TokenHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
