namespace Backend.Devices;

public static class SpectrometerCatalog
{
    public const ushort VernierVid = 0x08f7;

    public static readonly IReadOnlyDictionary<ushort, SpectrometerModel> Models = new Dictionary<ushort, SpectrometerModel>
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
                WavelengthMaxNm: 723.2
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
                WavelengthMaxNm: 899.6
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
                WavelengthMaxNm: 948.8
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
                WavelengthMaxNm: 849.5
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
                WavelengthMaxNm: 899.6
            )
        },
    };

    public static bool IsSpectrometerPid(ushort pid) => Models.ContainsKey(pid);

    public static SpectrometerModel GetModel(ushort pid) => Models.TryGetValue(pid, out var model)
        ? model : throw new ArgumentOutOfRangeException(nameof(pid), $"Unknown spectrometer PID 0x{pid:X4}");
}