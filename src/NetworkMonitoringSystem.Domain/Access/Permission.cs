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

    /// <summary>Read the event log of the whole system, including sign-ins and refused access.</summary>
    ViewEventLog = 5,

    // One permission for each remote command; a user may send only the commands they hold.
    RestartDevice = 101,
    ShutDownDevice = 102,
    KillProcess = 103,
    StartService = 104,
    StopService = 105,
    RestartService = 106,
    SynchronizeNow = 107,
    Ping = 108,
    Traceroute = 109,
    FlushDnsCache = 110,
}
