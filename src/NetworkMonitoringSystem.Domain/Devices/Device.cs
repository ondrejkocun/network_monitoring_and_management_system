namespace NetworkMonitoringSystem.Domain.Devices;

public sealed class Device
{
    public Device(Guid id, string name, string hostName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Device identifier must not be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Device name must not be empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(hostName))
        {
            throw new ArgumentException("Device host name must not be empty.", nameof(hostName));
        }

        Id = id;
        Name = name.Trim();
        HostName = hostName.Trim();
    }

    public Guid Id { get; }

    public string Name { get; }

    public string HostName { get; }
}
