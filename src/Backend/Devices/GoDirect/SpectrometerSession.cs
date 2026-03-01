using Backend.Measurements;

namespace Backend.Devices.GoDirect;

public sealed class SpectrometerSession
{
    private readonly List<(DateTimeOffset Timestamp, DisplaySpectrum Spectrum, string? Label)> _snapshots = new();
    private readonly object _recLock = new();
    public int IntegrationTime { get; set; }
    public OperatingMode Mode { get; set; } = OperatingMode.RawCounts;
    public ushort[]? DarkCounts { get; set; }
    public ushort[]? BlankCounts { get; set; }

    public bool IsReady { get; set; }
    public bool IsCalibrated { get; set; }

    public void AddSnapshot(DisplaySpectrum spectrum, DateTimeOffset timestamp, string? label = null)
    {
        lock (_recLock)
        {
            _snapshots.Add((timestamp, spectrum, label));
        }
    }
}