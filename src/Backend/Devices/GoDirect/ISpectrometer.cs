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
    IReadOnlyList<string> Warnings {get;}


    Task Initialize(CancellationToken ct = default);
    Task Calibrate(CancellationToken ct = default);
    Task SetIntegrationTime(int ms, CancellationToken ct = default);
    Task SetOperatingMode(OperatingMode mode, CancellationToken ct = default);
    void StartStreaming();
    Task StopStreaming(CancellationToken ct = default);
    Task<ushort[]> AcquireSingleRawCounts(CancellationToken ct = default);
}