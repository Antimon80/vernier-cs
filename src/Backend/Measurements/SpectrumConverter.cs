using Backend.Devices.GoDirect;

namespace Backend.Measurements;

/// <summary>
/// Converts raw CCD count (ushort counts over the full sensor) into a processed
/// <see cref="Spectrum"/> according to the current <see cref="SpectrometerSession"/> state.
///
/// Responsibilities:
/// - Apply the model's ROI (CCD pixel index range) to produce the displayed subset.
/// - Map pixel indices to wavelengths via <see cref="SpectrometerModel.GetWavelengthAxis"/>.
/// - Apply optional dark correction (raw - dark) when available.
/// - Apply blank/dark normalization for Transmission and Absorbance when calibrated.
///
/// Output conventions:
/// - Intensity-like modes return values normalized to [0..1] by dividing by 65535.
/// - Transmission returns values clamped to [0..1].
/// - Absorbance returns -log10(T), with T clamped to [eps..1] to avoid log(0).
/// - If a denominator is invalid (blank == dark), the corresponding point becomes NaN.
/// </summary>
public static class SpectrumConverter
{

    /// <summary>
    /// Computes the display spectrum for the given raw CCD counts.
    /// </summary>
    /// <param name="model">Static model data (ROI pixel bounds, wavelength axis mapping, name, etc.).</param>
    /// <param name="session">Current session state (operating mode, calibration references, flags).</param>
    /// <param name="raw">Raw CCD counts across the full sensor length.</param>
    /// <returns>A processed <see cref="Spectrum"/> for UI display and downstream analysis.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the model ROI is not configured or when a calibrated mode is requested without
    /// valid calibration data (dark + blank).
    /// </exception>
    public static Spectrum Compute(SpectrometerModel model, SpectrometerSession session, ushort[] raw)
    {
        OperatingMode mode = session.Mode;

        // ROI (region of interest) pixel bounds: only display/process this subrange.
        int lo = model.CCDPixelIndexMin;
        int hi = model.CCDPixelIndexMax;

        if (lo <= 0 && hi <= 0)
        {
            throw new InvalidOperationException($"Model '{model.Name}': CCD ROI not configured.");
        }
        if (hi < lo)
        {
            (lo, hi) = (hi, lo);
        }

        int n = hi - lo + 1;

        // Wavelength axis is expected to match the ROI length
        double[] wl = model.GetWavelengthAxis();
        double[] yAxis = new double[n];

        // Optional calibration references.
        // They are only considered valid if the array lengths match the raw spectrum length.
        ushort[]? dark = session.DarkCounts;
        ushort[]? blank = session.BlankCounts;

        bool hasDark = dark is not null && dark.Length == raw.Length;
        bool hasBlank = blank is not null && blank.Length == raw.Length;

        const double max = 65535.0;
        const double eps = 1e-3;

        switch (mode)
        {
            case OperatingMode.Absorbance:
                {
                    // Absorbance requires both dark and blank references and a calibrated session.
                    if (!session.IsCalibrated || !hasDark || !hasBlank)
                    {
                        throw new InvalidOperationException("Absorbance mode requires calibration (dark+blank).");
                    }

                    // A = -log10(T), with T = (raw - dark) / (blank - dark)
                    // Guard against invalid denominators and log(0).
                    for (int i = 0; i < n; i++)
                    {
                        double r = raw[lo + i];
                        double d = dark![lo + i];
                        double b = blank![lo + i];

                        double num = r - d;
                        double den = b - d;

                        if (den <= 0)
                        {
                            // Invalid calibration at this pixel -> mark as undefined.
                            yAxis[i] = double.NaN;
                            continue;
                        }

                        double t = num / den;

                        // Clamp transmission into a safe log domain.
                        if (t < eps)
                        {
                            t = eps;
                        }
                        if (t > 1.0)
                        {
                            t = 1.0;
                        }

                        yAxis[i] = -Math.Log10(t);
                    }

                    return new Spectrum(wl, yAxis, OperatingMode.Absorbance);
                }

            case OperatingMode.Transmission:
                {
                    // Transmission requires both dark and blank references and a calibrated session.
                    if (!session.IsCalibrated || !hasDark || !hasBlank)
                    {
                        throw new InvalidOperationException("Transmission mode requires calibration (dark+blank).");
                    }

                    // T = (raw - dark) / (blank - dark), clamped to [0..1]
                    for (int i = 0; i < n; i++)
                    {
                        double r = raw[lo + i];
                        double d = dark![lo + i];
                        double b = blank![lo + i];

                        double num = r - d;
                        double den = b - d;

                        if (den <= 0)
                        {
                            yAxis[i] = double.NaN;
                            continue;
                        }

                        double t = num / den;
                        if (t < 0)
                        {
                            t = 0;
                        }
                        if (t > 1.0)
                        {
                            t = 1.0;
                        }

                        yAxis[i] = t;
                    }

                    return new Spectrum(wl, yAxis, OperatingMode.Transmission);
                }

            case OperatingMode.Fluorescence405:
            case OperatingMode.Fluorescence500:
            case OperatingMode.Intensity:
            default:
                {
                    // Intensity-like modes:
                    // - If dark reference exists: subtract and clamp to 0.
                    // - Normalize to [0..1] by dividing by 65535.
                    if (hasDark)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            double v = raw[lo + i] - dark![lo + i];
                            if (v < 0)
                            {
                                v = 0;
                            }
                            yAxis[i] = v / max;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < n; i++)
                        {
                            yAxis[i] = raw[lo + i] / max;
                        }
                    }

                    return new Spectrum(wl, yAxis, OperatingMode.Intensity);
                }
        }
    }
}