using NetworkMonitoringSystem.Domain.Access;

namespace NetworkMonitoringSystem.Application.Access;

/// <summary>A permission a user holds through one of their roles, on one device or on all of them.</summary>
public sealed record GrantedPermission(Permission Permission, Guid? DeviceId);

/// <summary>The user a call is made by, with everything their roles allow.</summary>
public sealed record AuthenticatedUser(Guid Id, string UserName, IReadOnlyList<GrantedPermission> Grants)
{
    public static AuthenticatedUser FromUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new AuthenticatedUser(
            user.Id,
            user.UserName,
            user.Roles
                .SelectMany(role => role.Rules)
                .Select(rule => new GrantedPermission(rule.Permission, rule.DeviceId))
                .Distinct()
                .ToList());
    }

    /// <summary>True when the user may do this regardless of device: a rule for a single device is not enough.</summary>
    public bool Can(Permission permission)
    {
        return Grants.Any(grant => grant.Permission == permission && grant.DeviceId is null);
    }

    /// <summary>True when the user may do this on the given device, by a rule for it or a rule for all devices.</summary>
    public bool CanOnDevice(Permission permission, Guid deviceId)
    {
        return Grants.Any(grant => grant.Permission == permission && (grant.DeviceId is null || grant.DeviceId == deviceId));
    }

    /// <summary>True when the user may do this on at least one device.</summary>
    public bool CanOnSomeDevice(Permission permission)
    {
        return Grants.Any(grant => grant.Permission == permission);
    }
}
