namespace Backend.Devices;

public enum LampMode
{
    Off,
    White,
    Fluo405,
    Fluo500
}

public interface ISpectrometer : IDevice
{
    SpectrometerModel Model { get; }
    SpectrometerSession Session { get; }

    Task Initialize(CancellationToken ct = default);
    Task Calibrate(CancellationToken ct = default);
    Task SetIntegrationTime(int ms, CancellationToken ct = default);
    Task SetLampMode(LampMode mode, CancellationToken ct = default);
    Task<ushort[]> AcquireSpetrum(CancellationToken ct = default);
}