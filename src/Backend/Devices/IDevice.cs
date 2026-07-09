namespace Backend.Devices;

public interface IDevice : IDisposable
{
    ushort Vid { get; }
    ushort Pid { get; }
    string DeviceName { get; }

    bool IsConnected { get; }
    bool IsInitialized { get; }
    bool CanCalibrate { get; }
    bool IsCalibrated { get; }
    bool RequiresWarmupForCalibration { get; }

    Task Connect(CancellationToken ct = default);
    Task Disconnect(CancellationToken ct = default);

    Task Initialize(CancellationToken ct = default);
    Task Calibrate(bool? skipWarmup = null, CancellationToken ct = default);

    void StartMeasurement();
    Task StopMeasurement(CancellationToken ct = default);
}