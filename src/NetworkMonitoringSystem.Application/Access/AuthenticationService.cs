using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Access;

public sealed class AccessOptions
{
    public const string SectionName = "Access";

    /// <summary>
    /// Password of the first administrator, used only while the system has no users at all.
    /// It belongs in user secrets or the environment, never in a committed settings file.
    /// </summary>
    public string? InitialAdminPassword { get; set; }

    /// <summary>How long a user stays signed in.</summary>
    public int SessionHours { get; set; } = 12;
}

/// <param name="Token">The secret the client sends with every call. It is not stored and cannot be read again.</param>
public sealed record LoginResult(string Token, DateTimeOffset ExpiresAt, AuthenticatedUser User);

/// <summary>Signing in failed. The reason is deliberately not given, so that it does not help guessing.</summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException()
        : base("The user name or password is not correct.")
    {
    }
}

public interface IAuthenticationService
{
    /// <exception cref="AuthenticationFailedException">The credentials are wrong, or the account is locked or deactivated.</exception>
    Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>Returns the user the session token belongs to, or null when the token is not valid any more.</summary>
    Task<AuthenticatedUser?> AuthenticateAsync(string token, CancellationToken cancellationToken = default);

    Task LogoutAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Writes to the event log that the user was refused an operation they have no permission for.</summary>
    Task RecordAccessDeniedAsync(AuthenticatedUser user, string operation, Guid? deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the administrator role and, while there are no users, the first administrator.
    /// </summary>
    /// <returns>True when the first administrator was created.</returns>
    Task<bool> EnsureInitialAdministratorAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthenticationService : IAuthenticationService
{
    public const string AdministratorRoleName = "Administrator";
    public const string InitialAdministratorUserName = "admin";

    private const int TokenSizeBytes = 32;

    // Verified when the user does not exist, so that an unknown name takes as long to refuse as a wrong password.
    private static readonly string DecoyHash = PasswordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    private readonly IUserRepository _users;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AccessOptions _options;
    private readonly TimeProvider _timeProvider;

    public AuthenticationService(
        IUserRepository users,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        IOptions<AccessOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _users = users;
        _history = history;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var normalizedName = User.NormalizeUserName(userName ?? string.Empty);
        password ??= string.Empty;

        var user = normalizedName.Length is 0 or > User.MaxUserNameLength
            ? null
            : await _users.GetUserByNameAsync(normalizedName, cancellationToken);

        if (user is null)
        {
            PasswordHasher.Verify(password, DecoyHash);

            // The name is not written to the log: people sometimes type their password into the name field.
            _history.AddEvent(new MonitoringEvent(
                now,
                MonitoringEventType.LoginFailed,
                EventSeverity.Warning,
                "Sign-in failed: unknown user name."));
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new AuthenticationFailedException();
        }

        // A locked account refuses even the right password, otherwise locking would not slow guessing down.
        if (user.IsLocked(now) || !user.IsActive)
        {
            PasswordHasher.Verify(password, DecoyHash);

            _history.AddEvent(new MonitoringEvent(
                now,
                MonitoringEventType.LoginFailed,
                EventSeverity.Warning,
                user.IsActive
                    ? $"Sign-in of user '{user.UserName}' was refused: the account is locked."
                    : $"Sign-in of user '{user.UserName}' was refused: the account is deactivated.",
                userId: user.Id));
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new AuthenticationFailedException();
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            var locked = user.RecordFailedLogin(now);

            _history.AddEvent(new MonitoringEvent(
                now,
                MonitoringEventType.LoginFailed,
                EventSeverity.Warning,
                locked
                    ? $"Sign-in of user '{user.UserName}' failed: wrong password. The account is locked for {User.LockDuration.TotalMinutes:0} minutes."
                    : $"Sign-in of user '{user.UserName}' failed: wrong password.",
                userId: user.Id));
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new AuthenticationFailedException();
        }

        user.RecordSuccessfulLogin();

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenSizeBytes));
        var expiresAt = now.AddHours(Math.Clamp(_options.SessionHours, 1, 24 * 30));

        _users.AddSession(new UserSession(user.Id, HashToken(token), now, expiresAt));
        _history.AddEvent(new MonitoringEvent(
            now,
            MonitoringEventType.UserLoggedIn,
            EventSeverity.Info,
            $"User '{user.UserName}' signed in.",
            userId: user.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResult(token, expiresAt, AuthenticatedUser.FromUser(user));
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var session = await _users.GetSessionAsync(HashToken(token), cancellationToken);

        if (session is null || session.IsExpired(_timeProvider.GetUtcNow()))
        {
            return null;
        }

        // The user is read again on every call, so a deactivated user or a changed role takes effect at once.
        var user = await _users.GetUserByIdAsync(session.UserId, cancellationToken);

        return user is { IsActive: true } ? AuthenticatedUser.FromUser(user) : null;
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var session = await _users.GetSessionAsync(HashToken(token), cancellationToken);

        if (session is not null)
        {
            _users.RemoveSession(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RecordAccessDeniedAsync(
        AuthenticatedUser user,
        string operation,
        Guid? deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        _history.AddEvent(new MonitoringEvent(
            _timeProvider.GetUtcNow(),
            MonitoringEventType.AccessDenied,
            EventSeverity.Warning,
            $"User '{user.UserName}' was refused the operation '{operation}': missing permission.",
            deviceId,
            user.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> EnsureInitialAdministratorAsync(CancellationToken cancellationToken = default)
    {
        var role = await _users.GetRoleByNameAsync(AdministratorRoleName, cancellationToken);

        if (role is null)
        {
            role = new Role(Guid.NewGuid(), AdministratorRoleName);
            _users.AddRole(role);
        }

        // A permission added in a later version is given to the administrator role as well.
        foreach (var permission in Enum.GetValues<Permission>())
        {
            role.Allow(permission);
        }

        var created = false;

        if (!string.IsNullOrEmpty(_options.InitialAdminPassword) && !await _users.AnyUserExistsAsync(cancellationToken))
        {
            var administrator = new User(
                Guid.NewGuid(),
                InitialAdministratorUserName,
                PasswordHasher.Hash(_options.InitialAdminPassword),
                _timeProvider.GetUtcNow());
            administrator.AssignRole(role);
            _users.AddUser(administrator);
            created = true;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return created;
    }

    /// <summary>A token is 256 random bits, so a plain SHA-256 hash is enough to store it safely.</summary>
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
