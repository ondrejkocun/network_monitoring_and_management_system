namespace NetworkMonitoringSystem.Tests.Infrastructure;

using Microsoft.Extensions.Configuration;
using NetworkMonitoringSystem.Infrastructure;
using Npgsql;

public class ConnectionStringTests
{
    private const string WithoutPassword = "Host=localhost;Database=network_monitoring;Username=nms";

    [Fact]
    public void BuildConnectionString_AddsSeparatelyConfiguredPassword()
    {
        var configuration = CreateConfiguration(WithoutPassword, password: "secret");

        var result = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(configuration));

        Assert.Equal("secret", result.Password);
        Assert.Equal("localhost", result.Host);
        Assert.Equal("nms", result.Username);
    }

    [Fact]
    public void BuildConnectionString_Throws_WhenPasswordIsMissing()
    {
        var configuration = CreateConfiguration(WithoutPassword, password: null);

        var exception = Assert.Throws<InvalidOperationException>(
            () => DependencyInjection.BuildConnectionString(configuration));

        Assert.Contains(DependencyInjection.PasswordKey, exception.Message);
    }

    [Fact]
    public void BuildConnectionString_Throws_WhenConnectionStringIsMissing()
    {
        var configuration = CreateConfiguration(connectionString: null, password: "secret");

        Assert.Throws<InvalidOperationException>(() => DependencyInjection.BuildConnectionString(configuration));
    }

    private static IConfiguration CreateConfiguration(string? connectionString, string? password)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = connectionString,
                [DependencyInjection.PasswordKey] = password,
            })
            .Build();
    }
}
