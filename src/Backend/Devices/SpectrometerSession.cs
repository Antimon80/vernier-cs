namespace Backend.Devices;

public sealed class SpectrometerSession
{
    public int IntegrationTime { get; set; } = 30;
    public LampMode LampMode { get; set; } = LampMode.Off;

    public ushort[]? DarkCounts { get; set; }
    public ushort[]? BlankCounts { get; set; }
}