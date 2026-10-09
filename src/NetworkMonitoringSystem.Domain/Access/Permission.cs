namespace NetworkMonitoringSystem.Domain.Access;

/// <summary>
/// What a user may be allowed to do. The list is fixed, because every permission is checked by code.
/// </summary>
public enum Permission
{
    /// <summary>See devices, their state, resources, activity and history.</summary>
    ViewDevices = 1,

    /// <summary>Add and remove devices.</summary>
    ManageDevices = 2,

    /// <summary>Change the monitoring settings, such as the synchronization interval.</summary>
    ManageSettings = 3,

    /// <summary>Manage users, roles and access rules.</summary>
    ManageUsers = 4,
}
