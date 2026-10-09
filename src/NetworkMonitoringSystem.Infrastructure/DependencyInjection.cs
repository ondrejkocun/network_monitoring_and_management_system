using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetworkMonitoringSystem.Application.Common;
using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Devices;
using NetworkMonitoringSystem.Infrastructure.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Persistence;
using Npgsql;

namespace NetworkMonitoringSystem.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Database";
    public const string PasswordKey = "Database:Password";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = BuildConnectionString(configuration);

        services.AddDbContext<MonitoringDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDeviceRepository, EfDeviceRepository>();
        services.AddScoped<IMonitoringSettingsRepository, EfMonitoringSettingsRepository>();
        services.AddScoped<IMonitoringHistoryRepository, EfMonitoringHistoryRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }

    /// <summary>
    /// Combines the connection string with the password, which is configured separately
    /// so that it never has to be stored in a committed settings file.
    /// </summary>
    public static string BuildConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var password = configuration[PasswordKey];

        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        if (string.IsNullOrEmpty(builder.Password))
        {
            throw new InvalidOperationException(
                $"Database password is not configured. Set '{PasswordKey}' in user secrets or in the environment.");
        }

        return builder.ConnectionString;
    }
}
