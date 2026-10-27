using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Core.Interceptors;
using NetworkMonitoringSystem.Application.Access;

namespace NetworkMonitoringSystem.Server.Services;

/// <summary>
/// Lets a call to the administration API through only when it carries the token of a valid session.
/// Signing in is the one call that needs no session. What the user may do is checked by the operation itself.
/// </summary>
public sealed class AdminAuthenticationInterceptor : Interceptor
{
    private const string LoginMethod = "/nms.admin.v1.AdminApi/Login";
    private const string AuthorizationHeader = "authorization";
    private const string BearerPrefix = "Bearer ";

    private static readonly object UserKey = new();

    private readonly IAuthenticationService _authenticationService;

    public AdminAuthenticationInterceptor(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await AuthenticateAsync(context);

        return await continuation(request, context);
    }

    /// <summary>Uploads stream from the client, such as a file; they need a session just as much.</summary>
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthenticateAsync(context);

        return await continuation(requestStream, context);
    }

    /// <summary>Any call that streams from the server; none does yet, but one added later is covered too.</summary>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthenticateAsync(context);
        await continuation(request, responseStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthenticateAsync(context);
        await continuation(requestStream, responseStream, context);
    }

    private async Task AuthenticateAsync(ServerCallContext context)
    {
        if (context.Method == LoginMethod)
        {
            return;
        }

        var token = ReadToken(context);
        var user = token is null ? null : await _authenticationService.AuthenticateAsync(token, context.CancellationToken);

        if (user is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Sign in to use the administration API."));
        }

        context.GetHttpContext().Items[UserKey] = user;
    }

    /// <summary>Returns the session token the call carries, or null when it carries none.</summary>
    public static string? ReadToken(ServerCallContext context)
    {
        var header = context.RequestHeaders.GetValue(AuthorizationHeader);

        return header is not null && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? header[BearerPrefix.Length..].Trim()
            : null;
    }

    /// <summary>Returns the user the interceptor authenticated for this call.</summary>
    public static AuthenticatedUser GetUser(ServerCallContext context)
    {
        return context.GetHttpContext().Items[UserKey] as AuthenticatedUser
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Sign in to use the administration API."));
    }
}

/// <summary>
/// Remembers which refusals were written to the event log recently, so that a client repeating
/// the same refused call every few seconds produces one event a minute and not one per call.
/// </summary>
public sealed class DeniedAccessLog
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(Guid UserId, string Operation, Guid? DeviceId), DateTimeOffset> _recorded = new();
    private readonly TimeProvider _timeProvider;

    public DeniedAccessLog(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool ShouldRecord(Guid userId, string operation, Guid? deviceId)
    {
        var now = _timeProvider.GetUtcNow();
        var key = (userId, operation, deviceId);

        // Old entries are dropped here, so the table does not grow without limit.
        foreach (var entry in _recorded)
        {
            if (now - entry.Value >= Interval)
            {
                _recorded.TryRemove(entry);
            }
        }

        return _recorded.TryAdd(key, now);
    }
}
