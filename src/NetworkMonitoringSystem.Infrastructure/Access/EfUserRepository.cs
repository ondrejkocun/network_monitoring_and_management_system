using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Access;

public sealed class EfUserRepository : IUserRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfUserRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Users.AnyAsync(cancellationToken);
    }

    public Task<User?> GetUserByNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        return UsersWithRoles().FirstOrDefaultAsync(user => user.UserName == userName, cancellationToken);
    }

    public Task<User?> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return UsersWithRoles().FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public Task<Role?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return _dbContext.Roles.Include(role => role.Rules).FirstOrDefaultAsync(role => role.Name == name, cancellationToken);
    }

    public void AddUser(User user) => _dbContext.Users.Add(user);

    public void AddRole(Role role) => _dbContext.Roles.Add(role);

    public Task<UserSession?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserSessions.FirstOrDefaultAsync(session => session.TokenHash == tokenHash, cancellationToken);
    }

    public void AddSession(UserSession session) => _dbContext.UserSessions.Add(session);

    public void RemoveSession(UserSession session) => _dbContext.UserSessions.Remove(session);

    public Task<int> DeleteSessionsExpiredBeforeAsync(DateTimeOffset time, CancellationToken cancellationToken = default)
    {
        return _dbContext.UserSessions.Where(session => session.ExpiresAt < time).ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<User> UsersWithRoles()
    {
        return _dbContext.Users.Include(user => user.Roles).ThenInclude(role => role.Rules).AsSplitQuery();
    }
}
