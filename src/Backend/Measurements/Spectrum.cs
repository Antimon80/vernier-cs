using Backend.Devices.GoDirect;

namespace Backend.Measurements;

public sealed record Spectrum(
    Guid Id,
    double[] WavelengthNm,
    double[] YAxis,
    OperatingMode Mode,
    DateTimeOffset? Timestamp = null,
    string? Label = null
);