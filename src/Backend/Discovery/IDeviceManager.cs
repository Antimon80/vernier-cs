namespace Backend.Discovery;

using Backend.Devices;

public interface IDeviceManager : IDisposable
{
    IReadOnlyList<DeviceDescriptor> ListDevices();

    IDevice? CurrentDevice { get; }
    ISpectrometer? CurrentSpectrometer { get; }

    Task Connect(int deviceIndex, CancellationToken ct = default);
    Task Disconnect(CancellationToken ct = default);
}