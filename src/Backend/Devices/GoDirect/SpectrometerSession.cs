using Backend.Measurements;

namespace Backend.Devices.GoDirect;

/// <summary>
/// Holds mutable runtime state for a spectrometer device session.
/// 
/// This is intentionally a lightweight data container that is shared with processing
/// components (e.g., a SpectrumProcessor) and updated by the Spectrometer façade.
///
/// Typical contents:
/// - Current integration time and operating mode.
/// - Optional calibration references (blank and dark spectra).
/// - Readiness/calibration flags for the UI.
/// - A small in-memory snapshot log of processed display spectra.
/// </summary>
public sealed class SpectrometerSession
{
    /// <summary>
    /// In-memory list of captured display spectra ("snapshots") with timestamp and optional label.
    /// Used for manual captures (e.g., "before/after") or later export.
    /// </summary>
    private readonly List<(DateTimeOffset Timestamp, Spectrum Spectrum, string? Label)> _snapshots = [];

    /// <summary>
    /// Lock object for snapshot recording, because snapshots may be captured from streaming callbacks.
    /// </summary>
    private readonly object _recLock = new();

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
    /// Averaged dark reference spectrum (lamp OFF) used for correction.
    /// Must match the raw spectrum length of the device; otherwise it is ignored by processors.
    /// </summary>
    public ushort[]? DarkCounts { get; set; }

    /// <summary>
    /// Averaged blank/reference spectrum (lamp ON) used for transmission/absorbance.
    /// Must match the raw spectrum length of the device; otherwise it is ignored by processors.
    /// </summary>
    public ushort[]? BlankCounts { get; set; }

    /// <summary>
    /// True if the session is in a usable state (transport connected + initialization succeeded).
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// True if blank/dark references are available and consistent with the current integration time.
    /// </summary>
    public bool IsCalibrated { get; set; }

    /// <summary>
    /// Adds a captured display spectrum to the snapshot list.
    /// Thread-safe.
    /// </summary>
    /// <param name="spectrum">Processed display spectrum (not raw CCD counts).</param>
    /// <param name="timestamp">Capture time (typically UTC).</param>
    public void AddSnapshot(Spectrum spectrum, DateTimeOffset timestamp, string? label = null)
    {
        lock (_recLock)
        {
            _snapshots.Add((timestamp, spectrum, label));
        }
    }
}