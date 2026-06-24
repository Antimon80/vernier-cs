using Backend.Devices.GoDirect;

namespace Backend.Measurements;

public sealed record Spectrum(
    double[] WavelengthNm,
    double[] YAxis,
    OperatingMode Mode
);