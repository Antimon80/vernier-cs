namespace Backend.Devices.GoDirect;

public sealed record SpectrometerModel(
    ushort Pid,
    string Name,
    int PacketCount,
    int PacketPayloadBytes,
    bool HasWhiteLamp,
    bool HasLed405,
    bool HasLed500,
    double WavelengthMinNm,
    double WavelengthMaxNm,
    int CCDPixelIndexMin,
    int CCDPixelIndexMax,
    int IntegrationTimeMsMean
);