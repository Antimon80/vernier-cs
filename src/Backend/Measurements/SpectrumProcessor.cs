using System.Runtime.CompilerServices;
using Backend.Devices.GoDirect;

namespace Backend.Measurements;

/// <summary>
/// Small in-process pipeline that turns incoming raw CCD spectra into smoothed/averaged
/// <see cref="DisplaySpectrum"/> instances and emits them via an event.
///
/// Design goals:
/// - Provide short-window temporal smoothing to reduce noise/jitter in live display.
/// - Keep computation lightweight (simple moving average over the last N spectra).
/// - Be thread-safe: producers may push from a streaming thread, consumers may pull last display.
///
/// Concurrency:
/// - Internal state (window queue + last display) is protected by a single lock.
/// - <see cref="TryGetLastDisplay"/> returns a defensive copy of the Y-axis to avoid
///   accidental mutation by consumers.
/// </summary>
public sealed class SpectrumProcessor
{
    /// <summary>Guard for all mutable internal state.</summary>
    private readonly object _lock = new();

    /// <summary>Static model data (ROI, wavelength mapping, etc.).</summary>
    private readonly SpectrometerModel _model;

    /// <summary>Mutable session state (mode, dark/blank references, calibration flags).</summary>
    private readonly SpectrometerSession _session;

    /// <summary>Number of raw spectra kept in the moving-average window.</summary>
    private readonly int _windowSpectra;

    /// <summary>FIFO buffer of the most recent raw spectra.</summary>
    private readonly Queue<ushort[]> _window = new();

    /// <summary>Last computed display spectrum (smoothed).</summary>
    private DisplaySpectrum? _lastDisplay;

    /// <summary>
    /// Creates a new processor with a fixed moving-average window size.
    /// </summary>
    /// <param name="model">Static spectrometer model (ROI, wavelength axis).</param>
    /// <param name="session">Session state (operating mode, calibration references).</param>
    /// <param name="windowSpectra">Number of spectra to average (must be >= 1).</param>
    public SpectrumProcessor(SpectrometerModel model, SpectrometerSession session, int windowSpectra = 4)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSpectra, 1);
        _windowSpectra = windowSpectra;
    }

    /// <summary>
    /// Raised whenever a new averaged display spectrum is computed.
    /// </summary>
    public event Action<DisplaySpectrum, DateTimeOffset>? DisplayUpdated;

    /// <summary>
    /// Returns the most recent computed display spectrum.
    /// A defensive copy of the Y-axis is returned to prevent external mutation.
    /// </summary>
    /// <param name="display">Receives the display spectrum (copy) on success.</param>
    /// <returns>True if a spectrum is available; otherwise false.</returns>
    public bool TryGetLastDisplay(out DisplaySpectrum display)
    {
        lock (_lock)
        {
            if (_lastDisplay is null)
            {
                display = default!;
                return false;
            }

            // Copy the Y-axis to ensure the cached spectrum cannot be mutated by callers.
            double[] y = _lastDisplay.YAxis;
            double[] yCopy = new double[y.Length];
            Array.Copy(y, yCopy, yCopy.Length);

            display = _lastDisplay with { YAxis = yCopy };
            return true;
        }
    }

    /// <summary>
    /// Pushes a raw CCD spectrum into the smoothing window and, once the window is full,
    /// computes the averaged raw spectrum and converts it to a <see cref="DisplaySpectrum"/>.
    /// </summary>
    /// <param name="raw">Raw CCD counts (full sensor length).</param>
    /// <param name="timestamp">Acquisition time used for event emission.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if spectrum lengths differ within the averaging window (protocol/model mismatch).
    /// </exception>
    public void PushRaw(ushort[] raw, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(raw);

        DisplaySpectrum? toEmit = null;

        lock (_lock)
        {
            // Maintain a fixed-size FIFO window.
            _window.Enqueue(raw);
            while (_window.Count > _windowSpectra)
            {
                _window.Dequeue();
            }

            // Only produce output once the window is full.
            if (_window.Count < _windowSpectra)
            {
                return;
            }

            int len = raw.Length;

            // Accumulate per-pixel sums across the window, then compute mean.
            uint[] acc = new uint[len];
            foreach (var s in _window)
            {
                if (s.Length != len)
                {
                    throw new InvalidOperationException("Spectrum length changed within averaging window.");
                }

                for (int i = 0; i < len; i++)
                {
                    acc[i] += s[i];
                }
            }

            ushort[] meanRaw = new ushort[len];
            uint n = (uint)_window.Count;
            for (int i = 0; i < len; i++)
            {
                meanRaw[i] = (ushort)(acc[i] / n);
            }

            // Convert averaged raw data to a display spectrum using current session state.
            DisplaySpectrum display = SpectrumConverter.Compute(_model, _session, meanRaw);

            _lastDisplay = display;
            toEmit = display;
        }

        // Emit outside the lock to avoid re-entrancy and long-running handlers blocking producers.
        if (toEmit is not null)
        {
            DisplayUpdated?.Invoke(toEmit, timestamp);

        }
    }
}