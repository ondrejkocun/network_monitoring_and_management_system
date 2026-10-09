namespace NetworkMonitoringSystem.Tests.Server;

using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using NetworkMonitoringSystem.Contracts.Admin;

/// <summary>The first administrator of the server hosted by a test, and a way to make calls as them.</summary>
internal static class TestAdministrator
{
    public const string UserName = "admin";
    public const string Password = "test-admin-password";

    /// <summary>Makes the server create the administrator when it starts with an empty database.</summary>
    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Access:InitialAdminPassword", Password);
    }

    /// <summary>Signs the administrator in and returns a connection whose calls carry the session.</summary>
    public static async Task<CallInvoker> SignInAsync(GrpcChannel channel)
    {
        var reply = await new AdminApi.AdminApiClient(channel).LoginAsync(new LoginRequest { UserName = UserName, Password = Password });

        return WithToken(channel, reply.Token);
    }

    public static CallInvoker WithToken(GrpcChannel channel, string token)
    {
        return channel.Intercept(metadata =>
        {
            metadata.Add("authorization", $"Bearer {token}");

            return metadata;
        });
    }
}
