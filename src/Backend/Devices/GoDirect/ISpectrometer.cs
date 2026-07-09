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

    Task SetIntegrationTime(int ms, CancellationToken ct = default);
    Task SetOperatingMode(OperatingMode mode, CancellationToken ct = default);
    Task AcquireSingleSpectrum(CancellationToken ct = default);
}