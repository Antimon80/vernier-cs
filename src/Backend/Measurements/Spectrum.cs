namespace Backend.Measurements;

public enum Spectrum
{
    Intensity,
    Transmission,
    Absorbance
}

public sealed record DisplaySpectrum(
    double[] WavelengthNm,
    double[] YAxis,
    Spectrum Spectrum
);