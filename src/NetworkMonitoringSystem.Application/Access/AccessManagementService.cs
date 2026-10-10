using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Monitoring;

namespace NetworkMonitoringSystem.Application.Access;

public sealed record UserDto(Guid Id, string UserName, bool IsActive, bool IsLocked, DateTimeOffset CreatedAt, IReadOnlyList<Guid> RoleIds);

/// <param name="IsBuiltIn">A built-in role cannot be changed or deleted.</param>
public sealed record RoleDto(Guid Id, string Name, bool IsBuiltIn, IReadOnlyList<GrantedPermission> Rules);

/// <summary>The item the operation refers to does not exist.</summary>
public sealed class AccessItemNotFoundException(string message) : Exception(message);

/// <summary>
/// The change would break a rule of the system, for example leave it without anyone able to manage users.
/// </summary>
public sealed class AccessChangeRefusedException(string message) : Exception(message);

/// <summary>
/// Management of users, roles and access rules. Every change is written to the event log with the user who made it.
/// </summary>
public interface IAccessManagementService
{
    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentException">The name or password is not acceptable, or the name is taken.</exception>
    /// <exception cref="AccessItemNotFoundException">One of the roles does not exist.</exception>
    Task<UserDto> CreateUserAsync(AuthenticatedUser actor, string userName, string password, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates the user. A deactivated user is signed out everywhere.</summary>
    Task<UserDto> SetUserActiveAsync(AuthenticatedUser actor, Guid userId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Replaces the roles of the user with the given ones.</summary>
    Task<UserDto> SetUserRolesAsync(AuthenticatedUser actor, Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password for another user, unlocks the account and signs the user out everywhere.</summary>
    Task ResetPasswordAsync(AuthenticatedUser actor, Guid userId, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Changes the password of the user themselves and signs them out everywhere.</summary>
    /// <exception cref="AuthenticationFailedException">The current password is wrong.</exception>
    Task ChangeOwnPasswordAsync(AuthenticatedUser actor, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task<RoleDto> CreateRoleAsync(AuthenticatedUser actor, string name, CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(AuthenticatedUser actor, Guid roleId, CancellationToken cancellationToken = default);

    /// <param name="deviceId">The only device the rule applies to, or null for all devices.</param>
    Task<RoleDto> AllowAsync(AuthenticatedUser actor, Guid roleId, Permission permission, Guid? deviceId, CancellationToken cancellationToken = default);

    Task<RoleDto> RevokeAsync(AuthenticatedUser actor, Guid roleId, Permission permission, Guid? deviceId, CancellationToken cancellationToken = default);

    /// <summary>Deletes sessions that have expired.</summary>
    Task<int> DeleteExpiredSessionsAsync(CancellationToken cancellationToken = default);
}

public sealed class AccessManagementService : IAccessManagementService
{
    private readonly IUserRepository _users;
    private readonly IDeviceRepository _devices;
    private readonly IMonitoringHistoryRepository _history;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public AccessManagementService(
        IUserRepository users,
        IDeviceRepository devices,
        IMonitoringHistoryRepository history,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _users = users;
        _devices = devices;
        _history = history;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return (await _users.GetUsersAsync(cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        return (await _users.GetRolesAsync(cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<UserDto> CreateUserAsync(
        AuthenticatedUser actor,
        string userName,
        string password,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(roleIds);

        // The constructor of the user and the hasher validate the name and the password.
        var user = new User(Guid.NewGuid(), userName, PasswordHasher.Hash(password ?? string.Empty), _timeProvider.GetUtcNow());

        if (await _users.GetUserByNameAsync(user.UserName, cancellationToken) is not null)
        {
            throw new ArgumentException($"A user named '{user.UserName}' already exists.", nameof(userName));
        }

        foreach (var role in await GetRolesAsync(roleIds, cancellationToken))
        {
            user.AssignRole(role);
        }

        _users.AddUser(user);
        Record(actor, $"User '{actor.UserName}' created user '{user.UserName}' with roles: {DescribeRoles(user)}.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<UserDto> SetUserActiveAsync(AuthenticatedUser actor, Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var user = await GetUserAsync(userId, cancellationToken);

        if (user.IsActive == isActive)
        {
            return ToDto(user);
        }

        if (isActive)
        {
            user.Activate();
        }
        else
        {
            await EnsureAnotherAdministratorRemainsAsync(user, cancellationToken);
            user.Deactivate();
            await RemoveSessionsAsync(user, cancellationToken);
        }

        Record(actor, $"User '{actor.UserName}' {(isActive ? "activated" : "deactivated")} user '{user.UserName}'.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<UserDto> SetUserRolesAsync(
        AuthenticatedUser actor,
        Guid userId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(roleIds);

        var user = await GetUserAsync(userId, cancellationToken);
        var roles = await GetRolesAsync(roleIds, cancellationToken);

        if (IsAdministrator(user) && roles.All(role => !IsBuiltIn(role)))
        {
            await EnsureAnotherAdministratorRemainsAsync(user, cancellationToken);
        }

        foreach (var removed in user.Roles.Where(role => roles.All(kept => kept.Id != role.Id)).ToList())
        {
            user.RemoveRole(removed.Id);
        }

        foreach (var role in roles)
        {
            user.AssignRole(role);
        }

        Record(actor, $"User '{actor.UserName}' set the roles of user '{user.UserName}' to: {DescribeRoles(user)}.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task ResetPasswordAsync(AuthenticatedUser actor, Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var user = await GetUserAsync(userId, cancellationToken);

        user.ChangePasswordHash(PasswordHasher.Hash(newPassword ?? string.Empty));
        user.RecordSuccessfulLogin();
        await RemoveSessionsAsync(user, cancellationToken);

        Record(actor, $"User '{actor.UserName}' set a new password for user '{user.UserName}'.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangeOwnPasswordAsync(
        AuthenticatedUser actor,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var user = await GetUserAsync(actor.Id, cancellationToken);

        var now = _timeProvider.GetUtcNow();

        // A session left open on a computer must not be enough to take the account over, and it must not
        // allow guessing the password without limit either: wrong attempts count towards locking the account.
        if (user.IsLocked(now))
        {
            throw new AuthenticationFailedException();
        }

        if (!PasswordHasher.Verify(currentPassword ?? string.Empty, user.PasswordHash))
        {
            user.RecordFailedLogin(now);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new AuthenticationFailedException();
        }

        user.RecordSuccessfulLogin();

        user.ChangePasswordHash(PasswordHasher.Hash(newPassword ?? string.Empty));
        await RemoveSessionsAsync(user, cancellationToken);

        Record(actor, $"User '{actor.UserName}' changed their password.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<RoleDto> CreateRoleAsync(AuthenticatedUser actor, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var role = new Role(Guid.NewGuid(), name);

        if ((await _users.GetRolesAsync(cancellationToken)).Any(existing => string.Equals(existing.Name, role.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"A role named '{role.Name}' already exists.", nameof(name));
        }

        _users.AddRole(role);
        Record(actor, $"User '{actor.UserName}' created role '{role.Name}'.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(role);
    }

    public async Task DeleteRoleAsync(AuthenticatedUser actor, Guid roleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var role = await GetChangeableRoleAsync(roleId, cancellationToken);

        _users.RemoveRole(role);
        Record(actor, $"User '{actor.UserName}' deleted role '{role.Name}'.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<RoleDto> AllowAsync(
        AuthenticatedUser actor,
        Guid roleId,
        Permission permission,
        Guid? deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var role = await GetChangeableRoleAsync(roleId, cancellationToken);
        var scope = await DescribeScopeAsync(deviceId, cancellationToken);

        if (!Enum.IsDefined(permission))
        {
            throw new ArgumentException("Unknown permission.", nameof(permission));
        }

        if (role.Allow(permission, deviceId))
        {
            Record(actor, $"User '{actor.UserName}' allowed '{permission}' on {scope} to role '{role.Name}'.", deviceId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToDto(role);
    }

    public async Task<RoleDto> RevokeAsync(
        AuthenticatedUser actor,
        Guid roleId,
        Permission permission,
        Guid? deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var role = await GetChangeableRoleAsync(roleId, cancellationToken);

        if (role.Revoke(permission, deviceId))
        {
            var scope = deviceId is null ? "all devices" : "one device";

            Record(actor, $"User '{actor.UserName}' revoked '{permission}' on {scope} from role '{role.Name}'.");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToDto(role);
    }

    public Task<int> DeleteExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        return _users.DeleteSessionsExpiredBeforeAsync(_timeProvider.GetUtcNow(), cancellationToken);
    }

    private static bool IsBuiltIn(Role role) => role.Name == AuthenticationService.AdministratorRoleName;

    private static bool IsAdministrator(User user) => user.Roles.Any(IsBuiltIn);

    private static string DescribeRoles(User user)
    {
        return user.Roles.Count == 0 ? "none" : string.Join(", ", user.Roles.Select(role => role.Name).Order());
    }

    private static UserDto ToDto(User user)
    {
        return new UserDto(user.Id, user.UserName, user.IsActive, user.LockedUntil is not null, user.CreatedAt, user.Roles.Select(role => role.Id).ToList());
    }

    private static RoleDto ToDto(Role role)
    {
        return new RoleDto(
            role.Id,
            role.Name,
            IsBuiltIn(role),
            role.Rules.Select(rule => new GrantedPermission(rule.Permission, rule.DeviceId)).ToList());
    }

    private void Record(AuthenticatedUser actor, string message, Guid? deviceId = null)
    {
        _history.AddEvent(new MonitoringEvent(
            _timeProvider.GetUtcNow(),
            MonitoringEventType.AccessChanged,
            EventSeverity.Info,
            message,
            deviceId,
            actor.Id));
    }

    private async Task<User> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _users.GetUserByIdAsync(userId, cancellationToken)
            ?? throw new AccessItemNotFoundException("The user does not exist.");
    }

    private async Task<IReadOnlyList<Role>> GetRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var roles = new List<Role>();

        foreach (var roleId in roleIds.Distinct())
        {
            roles.Add(await _users.GetRoleByIdAsync(roleId, cancellationToken)
                ?? throw new AccessItemNotFoundException("The role does not exist."));
        }

        return roles;
    }

    /// <summary>The administrator role always holds every permission, so it can be neither changed nor deleted.</summary>
    private async Task<Role> GetChangeableRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await _users.GetRoleByIdAsync(roleId, cancellationToken)
            ?? throw new AccessItemNotFoundException("The role does not exist.");

        return IsBuiltIn(role)
            ? throw new AccessChangeRefusedException($"The role '{role.Name}' is built in and cannot be changed.")
            : role;
    }

    private async Task<string> DescribeScopeAsync(Guid? deviceId, CancellationToken cancellationToken)
    {
        if (deviceId is null)
        {
            return "all devices";
        }

        var device = await _devices.GetByIdAsync(deviceId.Value, cancellationToken)
            ?? throw new AccessItemNotFoundException("The device does not exist.");

        return $"device '{device.Name}'";
    }

    /// <summary>Someone must always be able to sign in and manage users, otherwise the system locks itself.</summary>
    private async Task EnsureAnotherAdministratorRemainsAsync(User leaving, CancellationToken cancellationToken)
    {
        if (!leaving.IsActive || !IsAdministrator(leaving))
        {
            return;
        }

        var users = await _users.GetUsersAsync(cancellationToken);

        if (!users.Any(user => user.Id != leaving.Id && user.IsActive && IsAdministrator(user)))
        {
            throw new AccessChangeRefusedException(
                $"User '{leaving.UserName}' is the last active administrator. Make another user an administrator first.");
        }
    }

    private async Task RemoveSessionsAsync(User user, CancellationToken cancellationToken)
    {
        foreach (var session in await _users.GetSessionsOfUserAsync(user.Id, cancellationToken))
        {
            _users.RemoveSession(session);
        }
    }
}
