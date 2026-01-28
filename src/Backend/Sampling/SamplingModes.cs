namespace Backend.Sampling;

/// <summary>
/// What the device measures.
/// </summary>
public enum MeasurementMode
{
    Absorbance,
    Transmittance,
    Fluorescence405,
    Fluorescence500,
    Intensity,
    RawData
}

/// <summary>
/// How acquisition is driven.
/// </summary>
public enum AcquisitionMode
{
    FullSpectrum,
    TimeResolved,
    EventTriggered
}