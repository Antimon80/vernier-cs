namespace Backend.Devices.GoDirect;


public enum OperatingMode
{
    Absorbance,
    Transmission,
    Fluorescence405,
    Fluorescence500,
    Intensity,
    RawCounts
}


public interface ISpectrometer : IDevice
{
    SpectrometerModel Model { get; }
    SpectrometerSession Session { get; }
    OperatingMode Mode { get; }

    event Action<ushort[], DateTimeOffset>? SpectrumReceived;

    Task Initialize(CancellationToken ct = default);
    Task Calibrate(CancellationToken ct = default);
    Task SetIntegrationTime(int ms, CancellationToken ct = default);
    Task SetOperatingMode(OperatingMode mode, CancellationToken ct = default);
    void StartStreaming();
    Task StopStreaming(CancellationToken ct = default);
    Task<ushort[]> AcquireSingleSpectrum(CancellationToken ct = default);
}