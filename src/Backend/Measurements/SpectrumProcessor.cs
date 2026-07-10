using System.Diagnostics;
using System.Drawing;
using Backend.Devices.GoDirect;

namespace Backend.Measurements;

/// <summary>
/// Processes raw CCD counts into spectra suitable for display,
/// comparison and export.
///
/// Processing includes:
/// - temporal averaging of consecutive raw spectra,
/// - selection of the model-specific CCD region,
/// - mapping CCD pixels to wavelengths,
/// - dark-count correction,
/// - blank normalization,
/// - calculation of transmission and absorbance,
/// - mode-specific scaling.
///
/// Every processed spectrum is stored as the current live spectrum
/// in the associated SpectrometerSession.
/// </summary>
public sealed class SpectrumProcessor
{
    private const double MaximumRawCount = ushort.MaxValue;

    /// <summary>
    /// Guards the moving-average window and processing operations.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Static model data such as CCD region and wavelength mapping.
    /// </summary>
    private readonly SpectrometerModel _model;

    /// <summary>
    /// Current measurement session containing operating mode,
    /// calibration references and processed results.
    /// </summary>
    private readonly SpectrometerSession _session;

    /// <summary>
    /// Number of consecutive raw spectra used for temporal averaging.
    /// </summary>
    private readonly int _windowSpectra;

    /// <summary>
    /// Most recent full-sensor raw spectra.
    /// </summary>
    private readonly Queue<ushort[]> _window = new();

    /// <summary>
    /// Creates a processor for the specified spectrometer model and session.
    /// </summary>
    /// <param name="model">
    /// Static spectrometer model information.
    /// </param>
    /// <param name="session">
    /// Current spectrometer session.
    /// </param>
    /// <param name="windowSpectra">
    /// Number of consecutive raw spectra to average.
    /// </param>
    public SpectrumProcessor(SpectrometerModel model, SpectrometerSession session, int windowSpectra)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        ArgumentOutOfRangeException.ThrowIfLessThan(windowSpectra, 1);
        _windowSpectra = windowSpectra;
    }

    /// <summary>
    /// Number of raw spectra used for temporal averaging.
    /// </summary>
    public int WindowSpectra => _windowSpectra;

    /// <summary>
    /// Processes one raw CCD spectrum immediately without temporal
    /// averaging.
    ///
    /// The resulting processed spectrum is stored as the current
    /// spectrum in the associated session.
    /// </summary>
    /// <param name="raw">
    /// Full-sensor raw CCD counts.
    /// </param>
    /// <param name="timestamp">
    /// Acquisition time of the raw spectrum.
    /// </param>
    /// <returns>
    /// The processed spectrum.
    /// </returns>
    public Spectrum ProcessSingle(ushort[] raw, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(raw);

        Spectrum spectrum;

        lock (_lock)
        {
            spectrum = ProcessRawCounts(ConvertToDouble(raw), timestamp);
        }

        _session.UpdateCurrentSpectrum(spectrum);

        return CopySpectrum(spectrum);
    }

    /// <summary>
    /// Adds a raw CCD spectrum to the current averaging block.
    ///
    /// Once the block is full, the averaged raw counts are processed,
    /// the block is cleared, and the resulting spectrum is stored as
    /// the current live spectrum in the associated session.
    /// </summary>
    /// <param name="raw">
    /// Full-sensor raw CCD counts.
    /// </param>
    /// <param name="timestamp">
    /// Acquisition time of the newest raw spectrum. This timestamp is
    /// assigned to the resulting averaged spectrum.
    /// </param>
    public void PushRaw(ushort[] raw, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(raw);
        Spectrum? processedSpectrum = null;

        lock (_lock)
        {
            _window.Enqueue((ushort[])raw.Clone());

            if (_window.Count < _windowSpectra)
            {
                return;
            }

            double[] averagedRawCounts = AverageRawCounts();

            _window.Clear();

            processedSpectrum = ProcessRawCounts(averagedRawCounts, timestamp);
        }

        _session.UpdateCurrentSpectrum(processedSpectrum);
    }

    /// <summary>
    /// Clears the moving-average window.
    ///
    /// This must be called when the operating mode, integration time
    /// or calibration references change so old and new measurements
    /// are not averaged together.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _window.Clear();
        }
    }

    /// <summary>
    /// Calculates the per-pixel arithmetic mean of all spectra in the
    /// current moving-average window.
    /// </summary>
    private double[] AverageRawCounts()
    {
        if (_window.Count == 0)
        {
            throw new InvalidOperationException("Cannot average an empty spectrum window.");
        }

        int spectrumLength = _window.Peek().Length;
        double[] sums = new double[spectrumLength];

        foreach (ushort[] spectrum in _window)
        {
            if (spectrum.Length != spectrumLength)
            {
                throw new InvalidOperationException("Spectrum lenth changed within the averaging window.");
            }

            for (int i = 0; i < spectrumLength; i++)
            {
                sums[i] += spectrum[i];
            }
        }

        double[] averaged = new double[spectrumLength];
        double count = _window.Count;

        for (int i = 0; i < spectrumLength; i++)
        {
            averaged[i] = sums[i] / count;
        }

        return averaged;
    }

    /// <summary>
    /// Converts full-sensor raw or averaged CCD counts into a processed
    /// spectrum according to the current session mode.
    /// </summary>
    private Spectrum ProcessRawCounts(double[] rawCounts, DateTimeOffset timestamp)
    {
        ValidateRawCounts(rawCounts);

        (int firstPixel, int lastPixel) = GetRegionOfInterest();

        int pointCount = lastPixel - firstPixel + 1;

        double[] wavelengthAxis = _model.GetWavelengthAxis();

        if (wavelengthAxis.Length != pointCount)
        {
            throw new InvalidOperationException($"Model '{_model.Name}' returned " +
            $"{wavelengthAxis.Length} wavelength values, but its " +
            $"CCD region contains {pointCount} pixels.");
        }

        OperatingMode mode = _session.Mode;
        ushort[]? darkCounts = _session.DarkCounts;
        ushort[]? blankCounts = _session.BlankCounts;
        bool IsCalibrated = _session.IsCalibrated;

        double[] yAxis = mode switch
        {
            OperatingMode.RawCounts => CreateRawCountAxis(rawCounts, firstPixel, pointCount),
            OperatingMode.Intensity or OperatingMode.Fluorescence405 or OperatingMode.Fluorescence500 => CreateIntensityAxis(rawCounts, darkCounts, firstPixel, pointCount),
            OperatingMode.Transmission => CreateTransmissionAxis(rawCounts, darkCounts, blankCounts, IsCalibrated, firstPixel, pointCount),
            OperatingMode.Absorbance => CreateAbsorbanceAxis(rawCounts, darkCounts, blankCounts, IsCalibrated, firstPixel, pointCount),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported operating mode.")
        };

        return new Spectrum(Id: Guid.NewGuid(), WavelengthNm: (double[])wavelengthAxis.Clone(),
        YAxis: yAxis, Mode: mode, Timestamp: timestamp, Label: null);
    }


    /// <summary>
    /// Validates that the supplied raw spectrum contains every pixel
    /// required by the model-specific CCD region.
    /// </summary>
    private void ValidateRawCounts(double[] rawCounts)
    {
        if (rawCounts.Length == 0)
        {
            throw new ArgumentException("Raw spectrum must not be empty.", nameof(rawCounts));
        }

        (_, int lastPixel) = GetRegionOfInterest();

        if (lastPixel >= rawCounts.Length)
        {
            throw new ArgumentException($"Model '{_model.Name}' requires CCD pixel " +
            $"{lastPixel}, but the raw spectrum contains only " +
            $"{rawCounts.Length} values.", nameof(rawCounts));
        }
    }

    /// <summary>
    /// Returns raw or averaged CCD counts for the configured region
    /// without correction or normalization.
    /// </summary>
    private static double[] CreateRawCountAxis(double[] rawCounts, int firstPixel, int pointCount)
    {
        double[] result = new double[pointCount];
        Array.Copy(rawCounts, firstPixel, result, 0, pointCount);

        return result;
    }

    /// <summary>
    /// Creates a dark-corrected, normalized intensity spectrum.
    ///
    /// If no valid dark reference is available, the raw counts are
    /// normalized without dark correction.
    /// </summary>
    private static double[] CreateIntensityAxis(double[] rawCounts, ushort[]? darkCounts, int firstPixel, int pointCount)
    {
        bool hasValidDarkReference = darkCounts is not null && darkCounts.Length == rawCounts.Length;

        double[] result = new double[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            int pixel = firstPixel + i;
            double correctedCounts = rawCounts[pixel];

            if (hasValidDarkReference)
            {
                correctedCounts -= darkCounts![pixel];
            }

            correctedCounts = Math.Max(correctedCounts, 0.0);

            result[i] = correctedCounts / MaximumRawCount;
        }

        return result;
    }

    /// <summary>
    /// Calculates transmission using:
    ///
    /// T = (sample - dark) / (blank - dark)
    /// </summary>
    private static double[] CreateTransmissionAxis(double[] rawCounts, ushort[]? darkCounts, ushort[]? blankCounts,
    bool isCalibrated, int firstPixel, int pointCount)
    {
        ValidateCalibrationReferences(rawCounts.Length, darkCounts, blankCounts, isCalibrated, OperatingMode.Transmission);

        double[] transmission = new double[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            int pixel = firstPixel + i;

            double numerator = rawCounts[pixel] - darkCounts![pixel];

            double denominator = blankCounts![pixel] - darkCounts[pixel];

            if (denominator <= 0.0)
            {
                transmission[i] = double.NaN;

                continue;
            }

            transmission[i] = numerator / denominator * 100.0;
        }

        return transmission;
    }

    /// <summary>
    /// Calculates absorbance using:
    ///
    /// A = -log10(T)
    /// </summary>
    private static double[] CreateAbsorbanceAxis(double[] rawCounts, ushort[]? darkCounts, ushort[]? blankCounts,
    bool isCalibrated, int firstPixel, int pointCount)
    {
        ValidateCalibrationReferences(rawCounts.Length, darkCounts, blankCounts, isCalibrated, OperatingMode.Absorbance);

        double[] absorbance = new double[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            int pixel = firstPixel + i;

            double numerator = rawCounts[pixel] - darkCounts![pixel];
            double denominator = blankCounts![pixel] - darkCounts[pixel];

            if (denominator <= 0.0)
            {
                absorbance[i] = double.NaN;

                continue;
            }

            double transmission = numerator / denominator;
            if (transmission <= 0.0)
            {
                absorbance[i] = 3.0;
                continue;
            }

            double value = -Math.Log10(transmission);
            absorbance[i] = Math.Min(value, 3.0);
        }

        return absorbance;
    }

    /// <summary>
    /// Verifies that dark and blank references required by calibrated
    /// operating modes are present and match the raw spectrum length.
    /// </summary>
    private static void ValidateCalibrationReferences(int rawSpectrumLength, ushort[]? darkCounts, ushort[]? blankCounts,
    bool isCalibrated, OperatingMode mode)
    {
        if (!isCalibrated)
        {
            throw new InvalidOperationException($"{mode} mode requires a dark reference.");
        }

        if (blankCounts is null)
        {
            throw new InvalidOperationException($"{mode} mode requires a blank reference.");
        }

        if (darkCounts?.Length != rawSpectrumLength)
        {
            throw new InvalidOperationException("The dark reference length does not match the raw spectrum length.");
        }

        if (blankCounts.Length != rawSpectrumLength)
        {
            throw new InvalidOperationException("The blank reference length does not match the raw spectrum length.");
        }
    }

    /// <summary>
    /// Returns the configured CCD region with ascending bounds.
    /// </summary>
    private (int FirstPixel, int LastPixel) GetRegionOfInterest()
    {
        int firstPixel = _model.CCDPixelIndexMin;
        int lastPixel = _model.CCDPixelIndexMax;

        if (firstPixel <= 0 && lastPixel <= 0)
        {
            throw new InvalidOperationException($"Model '{_model.Name}': CCD ROI not configured.");
        }

        if (lastPixel < firstPixel)
        {
            (firstPixel, lastPixel) = (lastPixel, firstPixel);
        }

        if (firstPixel < 0)
        {
            throw new InvalidOperationException($"Model '{_model.Name}' contains an invalid " +
            $"CCD start index: {firstPixel}.");
        }

        return (firstPixel, lastPixel);
    }

    /// <summary>
    /// Converts ushort raw counts to double without changing their
    /// numerical values.
    /// </summary>
    private static double[] ConvertToDouble(ushort[] rawCounts)
    {
        double[] result = new double[rawCounts.Length];

        for (int i = 0; i < rawCounts.Length; i++)
        {
            result[i] = rawCounts[i];
        }

        return result;
    }

    /// <summary>
    /// Creates a deep copy of a spectrum because the arrays contained
    /// in the record remain mutable.
    /// </summary>
    private static Spectrum CopySpectrum(Spectrum spectrum)
    {
        return spectrum with
        {
            WavelengthNm = (double[])spectrum.WavelengthNm.Clone(),
            YAxis = (double[])spectrum.YAxis.Clone()
        };
    }
}