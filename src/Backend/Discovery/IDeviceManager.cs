using Backend.Devices;
using Backend.Devices.GoDirect;
using Backend.Util;

namespace Backend.Discovery;

public interface IDeviceManager : IDisposable
{
    event Action<IReadOnlyList<DeviceDescriptor>>? DevicesChanged;
    IDevice? CurrentDevice { get; }
    ISpectrometer? CurrentSpectrometer { get; }
    IReadOnlyList<DiagnosticEntry> Diagnostics { get; }
    IReadOnlyList<DeviceDescriptor> ListDevices();
    Task ConnectSingle(CancellationToken ct = default);
    Task Connect(int deviceIndex, CancellationToken ct = default);
    Task Disconnect(CancellationToken ct = default);
    void NotifyDeviceTopologyChanged();
}