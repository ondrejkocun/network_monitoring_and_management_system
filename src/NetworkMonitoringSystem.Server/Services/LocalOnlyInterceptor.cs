using System.Net;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// Refuses calls that do not come from the computer the server runs on.
/// It protects the administration API until users and access control exist.
/// </summary>
public sealed class LocalOnlyInterceptor : Interceptor
{
    private readonly ILogger<LocalOnlyInterceptor> _logger;

    public LocalOnlyInterceptor(ILogger<LocalOnlyInterceptor> logger)
    {
        _logger = logger;
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var remoteAddress = context.GetHttpContext().Connection.RemoteIpAddress;

        if (!IsLocal(remoteAddress))
        {
            _logger.LogWarning("Administration call {Method} from {Address} was refused.", context.Method, remoteAddress);

            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "The administration API is available only from the computer the server runs on."));
        }

        return continuation(request, context);
    }

    /// <summary>
    /// A connection is local when it comes over the loopback interface. An address is missing only for
    /// in-process connections (the in-memory test server), which cannot come from another computer.
    /// </summary>
    public static bool IsLocal(IPAddress? remoteAddress)
    {
        return remoteAddress is null || IPAddress.IsLoopback(remoteAddress);
    }
}
