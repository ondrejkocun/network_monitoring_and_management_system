namespace NetworkMonitoringSystem.Domain.Access;

/// <summary>A named set of access rules that can be given to users.</summary>
public sealed class Role
{
    public const int MaxNameLength = 100;

    private readonly List<AccessRule> _rules = [];

    public Role(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Role identifier must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            throw new ArgumentException($"Role name must have 1 to {MaxNameLength} characters.", nameof(name));
        }

        Id = id;
        Name = name.Trim();
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<AccessRule> Rules => _rules;

    /// <summary>Allows the permission on one device, or on all devices when no device is given.</summary>
    /// <returns>False when the role already has this rule.</returns>
    public bool Allow(Permission permission, Guid? deviceId = null)
    {
        if (!Enum.IsDefined(permission))
        {
            throw new ArgumentOutOfRangeException(nameof(permission), permission, "Unknown permission.");
        }

        if (_rules.Any(rule => rule.Permission == permission && rule.DeviceId == deviceId))
        {
            return false;
        }

        _rules.Add(new AccessRule(permission, deviceId));

        return true;
    }

    /// <returns>False when the role has no such rule.</returns>
    public bool Revoke(Permission permission, Guid? deviceId = null)
    {
        return _rules.RemoveAll(rule => rule.Permission == permission && rule.DeviceId == deviceId) > 0;
    }
}

/// <summary>One thing a role allows: a permission on a single device, or on all of them.</summary>
public sealed class AccessRule
{
    internal AccessRule(Permission permission, Guid? deviceId)
    {
        Permission = permission;
        DeviceId = deviceId;
    }

    public long Id { get; private set; }

    public Permission Permission { get; }

    /// <summary>The only device the rule applies to, or null when it applies to all devices.</summary>
    public Guid? DeviceId { get; }
}
