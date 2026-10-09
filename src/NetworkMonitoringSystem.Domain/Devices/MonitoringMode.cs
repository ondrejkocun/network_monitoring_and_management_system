namespace NetworkMonitoringSystem.Domain.Devices;

public enum MonitoringMode
{
    /// <summary>The device runs the agent, which reports its state to the server.</summary>
    Agent = 0,

    /// <summary>The device has no agent; the server checks its availability over the network.</summary>
    Agentless = 1,
}
