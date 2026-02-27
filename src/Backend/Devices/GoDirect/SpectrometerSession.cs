namespace Backend.Devices.GoDirect;

public sealed class SpectrometerSession
{
    public int IntegrationTime { get; set; }
    public LampMode LampMode { get; set; } = LampMode.Off;

    public ushort[]? DarkCounts { get; set; }
    public ushort[]? BlankCounts { get; set; }

    public bool IsReady { get; set; }
    public bool IsCalibrated { get; set; }
}