namespace Backend.Devices;

using Backend.Devices.GoDirect;

public static class DeviceCatalog
{
    public const ushort VernierVid = 0x08f7;

    public static readonly IReadOnlyDictionary<ushort, SpectrometerModel> SpectrometerModels = new Dictionary<ushort, SpectrometerModel>
    {
        {
            0x0006,
            new SpectrometerModel(
                Pid: 0x0006,
                Name: "SpectroVis",
                PacketCount: 64,
                PacketPayloadBytes: 8,
                HasWhiteLamp: true,
                HasLed405: false,
                HasLed500: false,
                WavelengthMinNm: 403.3,
                WavelengthMaxNm: 723.2,
                CCDPixelIndexMin: 58,
                CCDPixelIndexMax: 162,
                IntegrationTimeMsMean: 40
            )
        },

        {
            0x0009,
            new SpectrometerModel(
                Pid: 0x0009,
                Name: "SpectroVisPlus",
                PacketCount: 56,
                PacketPayloadBytes: 64,
                HasWhiteLamp: true,
                HasLed405: true,
                HasLed500: true,
                WavelengthMinNm: 380.6,
                WavelengthMaxNm: 899.6,
                CCDPixelIndexMin: 577,
                CCDPixelIndexMax: 1238,
                IntegrationTimeMsMean: 110
            )
        },

        {
            0x0011,
            new SpectrometerModel(
                Pid: 0x0011,
                Name: "SpectroVisPlus (BLE)",
                PacketCount: 56,
                PacketPayloadBytes: 64,
                HasWhiteLamp: true,
                HasLed405: true,
                HasLed500: true,
                WavelengthMinNm: 380.6,
                WavelengthMaxNm: 948.8,
                CCDPixelIndexMin: 562,
                CCDPixelIndexMax: 1337,
                IntegrationTimeMsMean: 70
            )
        },

        {
            0x000a,
            new SpectrometerModel(
                Pid: 0x000a,
                Name: "UV/Vis Spectrometer",
                PacketCount: 56,
                PacketPayloadBytes: 64,
                HasWhiteLamp: true,
                HasLed405: false,
                HasLed500: false,
                WavelengthMinNm: 240.4,
                WavelengthMaxNm: 849.5,
                CCDPixelIndexMin: 511,
                CCDPixelIndexMax: 1450,
                IntegrationTimeMsMean: 25
            )
        },

        {
            0x000d,
            new SpectrometerModel(
                Pid: 0x000d,
                Name: "Emission Spectrometer",
                PacketCount: 56,
                PacketPayloadBytes: 64,
                HasWhiteLamp: false,
                HasLed405: false,
                HasLed500: false,
                WavelengthMinNm: 350.6,
                WavelengthMaxNm: 899.6,
                CCDPixelIndexMin: 611,
                CCDPixelIndexMax: 1421,
                IntegrationTimeMsMean: 0    // no white lamp
            )
        },
    };

    public static bool IsSpectrometerPid(ushort pid) => SpectrometerModels.ContainsKey(pid);

    public static SpectrometerModel GetModel(ushort pid) => SpectrometerModels.TryGetValue(pid, out var model)
        ? model : throw new ArgumentOutOfRangeException(nameof(pid), $"Unknown spectrometer PID 0x{pid:X4}");
}