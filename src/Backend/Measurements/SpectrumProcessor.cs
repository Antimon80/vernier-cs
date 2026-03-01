using System.Runtime.CompilerServices;
using Backend.Devices.GoDirect;

namespace Backend.Measurements;

public sealed class SpectrumProcessor
{
    private readonly object _lock = new();
    private readonly SpectrometerModel _model;
    private readonly SpectrometerSession _session;
    private readonly int _windowSpectra;
    private readonly Queue<ushort[]> _window = new();
    private DisplaySpectrum? _lastDisplay;

    public SpectrumProcessor(SpectrometerModel model, SpectrometerSession session, int windowSpectra = 4)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSpectra, 1);
        _windowSpectra = windowSpectra;
    }

    public event Action<DisplaySpectrum, DateTimeOffset>? DisplayUpdated;

    public bool TryGetLastDisplay(out DisplaySpectrum display)
    {
        lock (_lock)
        {
            if (_lastDisplay is null)
            {
                display = default!;
                return false;
            }

            var y = _lastDisplay.YAxis;
            var yCopy = new double[y.Length];
            Array.Copy(y, yCopy, yCopy.Length);

            display = _lastDisplay with { YAxis = yCopy };
            return true;
        }
    }

    public void PushRaw(ushort[] raw, DateTimeOffset timestamp)
    {
        if (raw is null)
        {
            throw new ArgumentNullException(nameof(raw));
        }

        DisplaySpectrum? toEmit = null;

        lock (_lock)
        {
            _window.Enqueue(raw);
            while (_window.Count > _windowSpectra)
            {
                _window.Dequeue();
            }

            if (_window.Count < _windowSpectra)
            {
                return;
            }

            int len = raw.Length;

            var acc = new uint[len];
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

                var meanRaw = new ushort[len];
                uint n = (uint)_window.Count;
                for (int i = 0; i < len; i++)
                {
                    meanRaw[i] = (ushort)(acc[i] / n);
                }

                var display = SpectrumConverter.Compute(_model, _session, meanRaw);

                _lastDisplay = display;
                toEmit = display;
            }
            if (toEmit is not null)
            {
                DisplayUpdated?.Invoke(toEmit, timestamp);

            }
        }
    }
}