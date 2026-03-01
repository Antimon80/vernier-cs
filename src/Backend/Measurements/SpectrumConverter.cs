using Backend.Devices.GoDirect;

namespace Backend.Measurements;

public static class SpectrumConverter
{
    public static DisplaySpectrum Compute(SpectrometerModel model, SpectrometerSession session, ushort[] raw)
    {
        OperatingMode mode = session.Mode;

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
        double[] wl = model.GetWavelengthAxis();
        double[] yAxis = new double[n];

        ushort[]? dark = session.DarkCounts;
        ushort[]? blank = session.BlankCounts;

        bool hasDark = dark is not null && dark.Length == raw.Length;
        bool hasBlank = blank is not null && blank.Length == raw.Length;

        const double max = 65535.0;
        const double eps = 1e-6;

        switch (mode)
        {
            case OperatingMode.Absorbance:
                {
                    if (!session.IsCalibrated || !hasDark || !hasBlank)
                    {
                        throw new InvalidOperationException("Absorbance mode requires calibration (dark+blank).");
                    }

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

                    return new DisplaySpectrum(wl, yAxis, Spectrum.Absorbance);
                }

            case OperatingMode.Transmission:
                {
                    if (!session.IsCalibrated || !hasDark || !hasBlank)
                    {
                        throw new InvalidOperationException("Transmission mode requires calibration (dark+blank).");
                    }

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

                    return new DisplaySpectrum(wl, yAxis, Spectrum.Transmission);
                }

            case OperatingMode.Fluorescence405:
            case OperatingMode.Fluorescence500:
            case OperatingMode.Intensity:
            default:
                {
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

                    return new DisplaySpectrum(wl, yAxis, Spectrum.Intensity);
                }
        }
    }
}