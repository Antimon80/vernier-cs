using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Backend.Protocol;
using Microsoft.Extensions.Logging;

namespace Backend.Devices.GoDirect
{

    public sealed class Spectrometer : ISpectrometer, IDisposable
    {
        private const int DefaultIntegrationTime = 30;

        // Calibration
        private const int CalibrationAverages = 5;
        private const double TargetLo = 0.70;
        private const double TargetHi = 0.90;

        // Warm-up: cumulative ON-time of white lamp (excluding short OFF windows during calibration dark capture)
        private static readonly TimeSpan RequiredWarmup = TimeSpan.FromMinutes(5);

        // CCD linearity check parameters
        private const int LinearityMinRun = 64;
        private const int LinearityTolearance = 3;

        // Live streaming
        private const int PauseTimeoutMs = 1500;

        private readonly SpectrometerProtocol _proto;
        private readonly SpectrometerModel _model;
        private readonly ILogger<Spectrometer>? _log;

        private readonly List<string> _warnings = [];

        private readonly Stopwatch _whiteOnStopwatch = new();
        private bool _whiteIsOn;

        private bool _isInitialized;

        // Exclusive access to the protocol (0x40 cycles vs config/calibration)
        private readonly SemaphoreSlim _exclusive = new(1, 1);

        // Stream (measurement data) loop management
        private CancellationTokenSource? _streamCts;
        private Task? _streamTask;

        private volatile bool _pauseRequested;
        private TaskCompletionSource<bool>? _pauseTcs;
        private TaskCompletionSource<bool>? _resumeTcs;

        // Latest spectrum cache for UI pull-model
        private readonly object _latestLock = new();
        private ushort[]? _latestSpectrum;
        private DateTimeOffset _latestSpectrumAt;

        public Spectrometer(SpectrometerProtocol protocol, ILogger<Spectrometer>? log = null)
        {
            _proto = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _model = protocol.Model;
            _log = log;
        }

        public SpectrometerModel Model => _model;
        public SpectrometerSession Session { get; } = new();

        public IReadOnlyList<string> Warnings => _warnings;

        public ushort Vid => throw new NotImplementedException();

        public ushort Pid => throw new NotImplementedException();

        public string DeviceName => throw new NotImplementedException();

        public bool IsConnected => throw new NotImplementedException();

        public event Action<ushort[], DateTimeOffset>? SpectrumReceived;

        public async Task Initialize(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _warnings.Clear();

            // Ensure measurement data stream loop is not running during initialization
            await StopStream(ct).ConfigureAwait(false);

            // 1) Model code (optional sanity check)
            ushort modelCode = await _proto.GetModelCode(ct).ConfigureAwait(false);
            _log?.LogInformation("Device reported model code 0x{Code:X4} (PID=0x{Pid:X4}, Name={Name})",
                modelCode, _model.Pid, _model.Name);

            // 2) CCD linearity: raw bytes + auto-decoder
            byte[] linBytes = await _proto.ReadLinearitySequence(ct).ConfigureAwait(false);
            var linRes = CcdLinearity.EvaluateAuto(linBytes, tolerance: LinearityTolearance, minRunLength: LinearityMinRun);

            _log?.LogInformation(
                "CCD linearity: {Level} (decoder={Decoder}, coreLen={CoreLen}, outTol={OutTol}, stepMedian={StepMedian}, stepMad={StepMad}, start={Start})",
                linRes.Level, linRes.Decoder, linRes.CoreLength, linRes.OutOfToleranceSteps, linRes.StepMedian, linRes.StepMad, linRes.CoreStartIndex);

            if (linRes.Level == CcdLinearity.Levels.Fail)
            {
                throw new InvalidOperationException($"CCD linearity check failed: {linRes.Message}");
            }

            if (linRes.Level == CcdLinearity.Levels.Warn)
            {
                _warnings.Add($"CCD linearity warning: {linRes.Message}");
            }

            // 3) Dark noise sanity at two integration times
        }

        public void StartStream()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Device not initialized. Call Initialize() first.");
            }

            if (_streamTask is not null && !_streamTask.IsCompleted)
            {
                return;
            }

            _pauseRequested = false;
            _pauseTcs = null;
            _resumeTcs = null;

            _streamCts = new CancellationTokenSource();
            _streamTask = Task.Run(() => StreamLoop(_streamCts.Token), CancellationToken.None);

            _log?.LogInformation("Live streaming started.");
        }

        public async Task StopStream(CancellationToken ct = default)
        {
            CancellationTokenSource? cts = _streamCts;
            _streamCts = null;

            if (cts is not null)
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                    cts.Dispose();
                }
            }

            Task? task = _streamTask;
            _streamTask = null;

            if (task is not null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log?.LogWarning(ex, "Stream loop ended with exception.");
                }
            }

            _pauseTcs?.TrySetCanceled(ct);
            _resumeTcs?.TrySetCanceled(ct);
        }

        public Task Calibrate(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task SetIntegrationTime(int ms, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task SetLampMode(LampMode mode, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<ushort[]> AcquireSpetrum(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task Connect(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task Disconnect(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        // Measurement data stream + pause/resume boundary control
        private async Task StreamLoop(CancellationToken ct)
        {
            
        }
    }
}