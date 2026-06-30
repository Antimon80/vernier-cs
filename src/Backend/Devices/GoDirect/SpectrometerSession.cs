using Backend.Measurements;
using Microsoft.Extensions.Logging;

namespace Backend.Devices.GoDirect;

/// <summary>
/// Holds the mutable state and measurement results of the current
/// spectrometer session.
///
/// The session contains:
/// - current device and measurement settings,
/// - calibration references,
/// - readiness and calibration state,
/// - the latest processed live spectrum,
/// - processed spectra captured for comparison, overlay or export.
/// </summary>
public sealed class SpectrometerSession(ILogger<SpectrometerSession>? log = null)
{
    private readonly ILogger<SpectrometerSession>? _log = log;
    /// <summary>
    /// In-memory list of captured and processed spectra with timestamp and optional label.
    /// Used for manual captures (e.g., "before/after") or later export.
    /// </summary>
    private readonly List<Spectrum> _snapshots = [];
    private readonly Lock _spectrumLock = new();

    private Spectrum? _currentSpectrum;

    private readonly List<DiagnosticEntry> _diagnostics = [];

    /// <summary>
    /// Current device integration time in milliseconds (as last echoed/applied by the device).
    /// Changing this typically invalidates calibration references.
    /// </summary>
    public int IntegrationTime { get; set; }

    /// <summary>
    /// Current operating mode (raw counts, intensity, transmission, absorbance, fluorescence, ...).
    /// </summary>
    public OperatingMode Mode { get; set; } = OperatingMode.RawCounts;

    /// <summary>
    /// Averaged dark reference spectrum used for dark-count correction.
    /// The array contains full-sensor raw CCD counts.
    /// </summary>
    public ushort[]? DarkCounts { get; set; }

    /// <summary>
    /// Averaged blank reference spectrum used for transmission and
    /// absorbance calculations.
    /// The array contains full-sensor raw CCD counts.
    /// </summary>
    public ushort[]? BlankCounts { get; set; }

    /// <summary>
    /// True if the session is in a usable state (initialization succeeded).
    /// </summary>
    public bool IsInitialized { get; internal set; }

    /// <summary>
    /// True if blank/dark references are available and consistent with the current integration time.
    /// </summary>
    public bool IsCalibrated { get; internal set; }

    public ushort? ModelCode { get; internal set; }
    public List<DiagnosticEntry> Diagnostics => _diagnostics;

    /// <summary>
    /// Latest fully processed live spectrum.
    ///
    /// A defensive copy is returned so callers cannot mutate the
    /// spectrum stored by this session.
    /// </summary>
    public Spectrum? CurrentSpectrum
    {
        get
        {
            lock (_spectrumLock)
            {
                return _currentSpectrum is null ? null : CopySpectrum(_currentSpectrum);
            }
        }
    }

    /// <summary>
    /// Gets copies of all processed spectra captured during this session.
    ///
    /// Changes made to the returned collection or its spectra do not
    /// affect the spectra stored in the session.
    /// </summary>
    public IReadOnlyList<Spectrum> Snapshots
    {
        get
        {
            lock (_spectrumLock)
            {
                return [.. _snapshots.Select(CopySpectrum)];
            }
        }
    }

    /// <summary>
    /// Raised whenever a new processed live spectrum is stored.
    /// </summary>
    public event Action<Spectrum>? CurrentSpectrumChanged;

    /// <summary>
    /// Captures the current processed live spectrum for later overlay,
    /// comparison or export.
    /// </summary>
    /// <param name="label">
    /// Optional descriptive label for the captured spectrum.
    /// </param>
    /// <returns>
    /// True if a current spectrum was available and captured;
    /// otherwise false.
    /// </returns>
    public bool CaptureCurrentSpectrum(string? label = null)
    {
        lock (_spectrumLock)
        {
            if (_currentSpectrum is null)
            {
                return false;
            }

            Spectrum snapshot = CopySpectrum(_currentSpectrum with
            {
                Id = Guid.NewGuid(),
                Label = label
            });

            _snapshots.Add(snapshot);
            return true;
        }
    }

    /// <summary>
    /// Adds an already processed spectrum to the captured spectra of
    /// this session.
    ///
    /// This can be used for imported spectra or processed single
    /// measurements.
    /// </summary>
    public Spectrum AddSnapshot(Spectrum spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);

        Spectrum snapshot = CopySpectrum(spectrum with
        {
            Id = Guid.NewGuid()
        }
        );

        lock (_spectrumLock)
        {
            _snapshots.Add(snapshot);
        }

        return CopySpectrum(spectrum);
    }

    /// <summary>
    /// Removes the specified captured spectrum with the specified identifier.
    /// </summary>
    public bool RemoveSnapshot(Guid spectrumId)
    {
        lock (_spectrumLock)
        {
            int index = _snapshots.FindIndex(spectrum => spectrum.Id == spectrumId);

            if (index < 0)
            {
                return false;
            }

            _snapshots.RemoveAt(index);
            return true;
        }
    }

    /// <summary>
    /// Removes all captured spectra while retaining the current live
    /// spectrum and all device and calibration state.
    /// </summary>
    public void ClearSnapshots()
    {
        lock (_spectrumLock)
        {
            _snapshots.Clear();
        }
    }

    /// <summary>
    /// Stores a newly processed live spectrum in the session.
    ///
    /// This method is intended to be called by SpectrumProcessor.
    /// </summary>
    internal void UpdateCurrentSpectrum(Spectrum spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);

        Spectrum storedSpectrum = CopySpectrum(spectrum);
        Spectrum eventSpectrum;

        lock (_spectrumLock)
        {
            _currentSpectrum = storedSpectrum;
            eventSpectrum = CopySpectrum(storedSpectrum);
        }

        CurrentSpectrumChanged?.Invoke(eventSpectrum);
    }

    /// <summary>
    /// Clears the current live spectrum without deleting captured
    /// spectra.
    /// </summary>
    internal void ClearCurrentSpectrum()
    {
        lock (_spectrumLock)
        {
            _currentSpectrum = null;
        }
    }

    /// <summary>
    /// Gets copies of all processed spectra captured during this session.
    /// Changes made to the returned collection or its spectra do not affect
    /// the data stored in the session.
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