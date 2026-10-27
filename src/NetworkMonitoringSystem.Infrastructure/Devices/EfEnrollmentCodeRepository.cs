using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Infrastructure.Persistence;

namespace NetworkMonitoringSystem.Infrastructure.Devices;

public sealed class EfEnrollmentCodeRepository : IEnrollmentCodeRepository
{
    private readonly MonitoringDbContext _dbContext;

    public EfEnrollmentCodeRepository(MonitoringDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task<EnrollmentCode?> GetByHashAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        return _dbContext.EnrollmentCodes.FirstOrDefaultAsync(code => code.CodeHash == codeHash, cancellationToken);
    }

    public Task<EnrollmentCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.EnrollmentCodes.FirstOrDefaultAsync(code => code.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<EnrollmentCode>> GetCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EnrollmentCodes
            .AsNoTracking()
            .Where(code => code.CreatedAt >= since)
            .OrderByDescending(code => code.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public void Add(EnrollmentCode code) => _dbContext.EnrollmentCodes.Add(code);
}
