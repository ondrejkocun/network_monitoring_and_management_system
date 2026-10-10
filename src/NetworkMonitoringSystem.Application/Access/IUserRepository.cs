using NetworkMonitoringSystem.Domain.Access;

namespace NetworkMonitoringSystem.Application.Access;

/// <summary>
/// Storage for users, roles and sessions. Changes are saved through <see cref="Common.IUnitOfWork"/>.
/// Users are returned together with their roles and the rules of those roles.
/// </summary>
public interface IUserRepository
{
    Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken = default);

    /// <param name="userName">Normalized with <see cref="User.NormalizeUserName"/>.</param>
    Task<User?> GetUserByNameAsync(string userName, CancellationToken cancellationToken = default);

    Task<User?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Role?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all users ordered by name.</summary>
    Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all roles with their rules, ordered by name.</summary>
    Task<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<Role?> GetRoleByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void AddUser(User user);

    void AddRole(Role role);

    /// <summary>Removes the role; users who had it lose it.</summary>
    void RemoveRole(Role role);

    Task<IReadOnlyList<UserSession>> GetSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserSession?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken = default);

    void AddSession(UserSession session);

    void RemoveSession(UserSession session);

    /// <summary>Deletes sessions that expired before the given time, immediately and without <see cref="Common.IUnitOfWork"/>.</summary>
    Task<int> DeleteSessionsExpiredBeforeAsync(DateTimeOffset time, CancellationToken cancellationToken = default);
}
