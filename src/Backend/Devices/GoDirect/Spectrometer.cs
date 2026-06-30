using System.Diagnostics;
using Backend.Measurements;
using Backend.Protocol;
using Backend.Transport;
using Microsoft.Extensions.Logging;

namespace Backend.Devices.GoDirect
{
    /// <summary>
    /// High-level spectrometer device facade for Vernier Go Direct models.
    ///
    /// Responsibilities:
    /// - Connect/disconnect transport and run initialization checks.
    /// - Manage device configuration (integration time, lamp mode, operating mode).
    /// - Perform calibration (blank/dark capture) for absorbance/transmission modes.
    /// - Provide live streaming of raw spectra and processed display spectra.
    ///
    /// Concurrency model:
    /// - All protocol operations are guarded by an exclusive semaphore to prevent
    ///   overlapping 0x40 acquisition cycles with configuration/calibration commands.
    /// - Streaming runs in a background Task and repeatedly acquires raw counts.
    ///
    /// Notes:
    /// - This class intentionally caches the latest spectrum for a UI pull-model.
    /// - Event handlers are invoked defensively (exceptions are caught and logged).
    /// </summary>
    public sealed class Spectrometer : ISpectrometer, IDisposable
    {
        /// <summary>Default integration time applied after initialization (ms).</summary>
        private const int DefaultIntegrationTime = 30;

        // Calibration configuration
        private const int CalibrationAverages = 5;
        private const double TargetLo = 0.70;
        private const double TargetHi = 0.90;

        // Required ON-time of the white lamp for sanity-check during initialization
        private static readonly TimeSpan InitializationWarmup = TimeSpan.FromSeconds(5);

        // Required cumulative ON-time of the white lamp for calibration.
        private static readonly TimeSpan CalibrationWarmup = TimeSpan.FromMinutes(5);
        private bool _skipWarmup;

        // CCD linearity check parameters
        private const int LinearityMinRun = 64;
        private const int LinearityTolerance = 3;

        // Dependencies
        private readonly ITransport _transport;
        private readonly SpectrometerProtocol _proto;
        private readonly SpectrometerModel _model;
        private readonly ILogger<Spectrometer>? _log;

        // Session state and diagnostics
        private readonly Stopwatch _whiteOnStopwatch = new();
        private bool _whiteIsOn;
        private LampMode _lampMode = LampMode.Off;

        // Exclusive access to the protocol (0x40 cycles vs config/calibration)
        private readonly SemaphoreSlim _exclusive = new(1, 1);

        // Stream (measurement data) loop management
        private CancellationTokenSource? _streamCts;
        private Task? _streamTask;
        private volatile bool _streamFaulted;
        private bool IsStreamingActive => _streamTask is not null && !_streamTask.IsCompleted;

        // Processing
        private readonly SpectrumProcessor _processor;

        /// <summary>
        /// Creates a new spectrometer façade for a given transport + model.
        /// </summary>
        /// <param name="transport">Underlying transport (HID, etc.). Must be connected via <see cref="Connect"/>.</param>
        /// <param name="model">Static model description (pixel mapping, lamp presence, PID, etc.).</param>
        /// <param name="log">Optional logger.</param>
        public Spectrometer(ITransport transport, SpectrometerModel model, ILoggerFactory? loggerFactory = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _model = model;
            _proto = new SpectrometerProtocol(_transport, _model);
            Session = new SpectrometerSession(loggerFactory?.CreateLogger<SpectrometerSession>());

            // Processor uses the current Session (mode, dark/blank, etc.) and emits display spectra.
            _processor = new SpectrumProcessor(_model, Session, windowSpectra: 4);

            _log = loggerFactory?.CreateLogger<Spectrometer>();
        }

        // Public properties
        public SpectrometerModel Model => _model;

        /// <summary>
        /// Mutable runtime session state (integration time, mode, blank/dark, snapshots, flags).
        /// </summary>
        public SpectrometerSession Session { get; }

        public OperatingMode Mode => Session.Mode;
        public LampMode LampMode => _lampMode;

        /// <summary>
        /// If true, the white lamp warm-up wait is skipped (dev/test).
        /// </summary>
        public bool SkipWarmup
        {
            get => _skipWarmup;
            set => _skipWarmup = value;
        }

        /// <summary>
        /// Non-fatal issues found during initialization/calibration (echo mismatch, weak lamp check, etc.).
        /// </summary>
        public ushort Vid => DeviceCatalog.VernierVid;
        public ushort Pid => _model.Pid;
        public string DeviceName => _model.Name;

        /// <summary>True if the underlying transport is connected.</summary>
        public bool IsConnected => _transport.IsConnected;

        // Events

        /// <summary>
        /// Fired when a raw spectrum is acquired (device counts, full CCD length).
        /// </summary>
        public event Action<ushort[], DateTimeOffset>? CountsReceived;

        // Public API: lifecycle

        /// <summary>
        /// Connects the transport and initializes the device if needed.
        /// </summary>
        public async Task Connect(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsConnected)
            {
                await _transport.Connect(ct).ConfigureAwait(false);
            }

            if (!Session.IsInitialized)
            {
                await Initialize(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops streaming (if running), turns off lamps, clears session readiness,
        /// and disconnects the transport.
        /// </summary>
        public async Task Disconnect(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsConnected)
            {
                return;
            }

            await StopStreaming(ct).ConfigureAwait(false);
            await _exclusive.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Ensure all lamps off on disconnect
                if (_model.HasWhiteLamp)
                {
                    await _proto.SetLamp(LampMode.White, false, ct).ConfigureAwait(false);
                }
                if (_model.HasLed405)
                {
                    await _proto.SetLamp(LampMode.Fluo405, false, ct).ConfigureAwait(false);
                }
                if (_model.HasLed500)
                {
                    await _proto.SetLamp(LampMode.Fluo500, false, ct).ConfigureAwait(false);
                }

                _whiteIsOn = false;
                if (_whiteOnStopwatch.IsRunning)
                {
                    _whiteOnStopwatch.Stop();
                }

                Session.IsCalibrated = false;
                Session.IsInitialized = false;
                _streamFaulted = false;
            }
            finally
            {
                _exclusive.Release();
            }

            await _transport.Disconnect(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Releases resources. This synchronously calls <see cref="Disconnect"/> and logs failures.
        /// </summary>
        public void Dispose()
        {
            try
            {
                Disconnect(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Dispose -> Disconnect failed.");
            }
        }

        // Public API: initialization/calibration

        /// <summary>
        /// Runs device sanity checks and sets a safe default configuration:
        /// - Reads model code.
        /// - Performs CCD linearity check from raw bytes.
        /// - Checks dark noise at two integration times to detect a dead/mismatched CCD.
        /// - Sets default integration time and initializes lamp state.
        /// - Optionally checks "light vs dark" response for white lamp models.
        /// </summary>
        public async Task Initialize(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            Session.IsInitialized = false;
            Session.IsCalibrated = false;
            Session.ModelCode = null;
            Session.ClearDiagnostics(DiagnosticCategory.Initialization);
            _streamFaulted = false;

            // Ensure measurement data stream loop is not running during initialization
            await StopStreaming(ct).ConfigureAwait(false);

            // 1) Wake up device and switch off all lamps
            if (!await PrepareInitialization(ct).ConfigureAwait(false))
            {
                return;
            }

            // 2) Read model code
            if (!await ReadAndStoreModelCode(ct).ConfigureAwait(false))
            {
                return;
            }

            // 3) CCD linearity: raw bytes + auto-decoder
            if (!await CheckCcdLinearity(ct).ConfigureAwait(false))
            {
                return;
            }

            // 4) Dark noise sanity at two integration times
            // restore 30 ms and acquire the dark reference at 30 ms
            (bool noiseCheckSucceeded, ushort[]? darkSpectrum) = await CheckDarkNoise(ct).ConfigureAwait(false);

            if (!noiseCheckSucceeded || darkSpectrum is null)
            {
                return;
            }

            // 5) Light-vs-dark sanity check (only if white lamp exists).
            if (_model.HasWhiteLamp && !await CheckWhiteLampResponse(darkSpectrum, ct).ConfigureAwait(false))
            {
                return;
            }

            Session.Mode = OperatingMode.RawCounts;
            Session.IsCalibrated = false;
            Session.IsInitialized = true;
            _streamFaulted = false;

            _log?.LogInformation("Spectrometer initialized. ModelCode={ModelCode:X4}, Calibrated={Calibrated}, WhiteOn={WhiteOn}, Warmup={WarmupSeconds}s",
                Session.ModelCode, Session.IsCalibrated, _whiteIsOn, (int)_whiteOnStopwatch.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// Performs absorbance/transmission calibration:
        /// - Ensures white lamp warmup.
        /// - Finds an integration time that yields a target mean ROI ratio (TargetLo..TargetHi).
        /// - Captures averaged blank (white ON) and dark (white OFF) spectra.
        /// </summary>
        public async Task Calibrate(CancellationToken ct = default)
        {
            await RunWithStreamingPaused(async () =>
            {
                ct.ThrowIfCancellationRequested();
                if (!Session.IsInitialized)
                {
                    throw new InvalidOperationException("Device is not initialized. Call Initialize() first.");
                }
                if (!_model.HasWhiteLamp)
                {
                    throw new InvalidOperationException("This model has no white lamp; absorbance/transmission calibration is not applicable.");
                }

                // Ensure white lamp mode before calibration steps
                await SetLampMode(LampMode.White, ct).ConfigureAwait(false);

                // Warm-up: cumulative ON time
                await EnsureWarmUp(ct).ConfigureAwait(false);

                // Find optimal integration time for blank/reference (5 - 6 iterations)
                (int tFound, double ratio, bool inBand) = await FindIntegrationTimeForTargetBand(
                    tMin: 1, tMax: 1000, maxIter: 10, probeAverages: 3, ct).ConfigureAwait(false);

                int echoedFinal = await _proto.SetIntegrationTime(tFound, ct).ConfigureAwait(false);
                Session.IntegrationTime = echoedFinal;

                if (!inBand)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.CALIBRATION.TARGET_BAND_NOT_REACED",
                        severity: DiagnosticSeverity.Waring,
                        category: DiagnosticCategory.Calibration,
                        message: "The calibration target range could not be reached",
                        technicalDetails: $"Target={TargetLo:P0}..{TargetHi:P0}; selected integration time={echoedFinal} ms; ratio={ratio:P1}.",
                        operation: nameof(Calibrate),
                        source: nameof(Spectrometer)
                    );
                }


                // Capture and average blank spectra (white ON)
                await SetLampMode(LampMode.White, ct).ConfigureAwait(false);
                ushort[] blankAvg = await AcquireAverageRaw(CalibrationAverages, ct).ConfigureAwait(false);
                Session.BlankCounts = blankAvg;

                // Capture and average dark spectra (white OFF, but do NOT count this off-time against warmup)
                await SetWhiteLamp(on: false, countWarmupTime: false, ct).ConfigureAwait(false);
                ushort[] darkAvg = await AcquireAverageRaw(CalibrationAverages, ct).ConfigureAwait(false);
                Session.DarkCounts = darkAvg;

                // Restore default: white ON, measurement ready
                await SetWhiteLamp(on: true, countWarmupTime: true, ct).ConfigureAwait(false);

                Session.IsCalibrated = true;
                _processor.Reset();

                _log?.LogInformation("Calibration completed. t={T}ms, blank/dark average over {N} spectra, warmup={WarmupSeconds}s",
                Session.IntegrationTime, CalibrationAverages, (int)_whiteOnStopwatch.Elapsed.TotalSeconds);
            }, ct).ConfigureAwait(false);
        }

        // Public API: configuration and acquisition

        /// <summary>
        /// Sets the operating mode and adjusts lamp mode accordingly.
        /// </summary>
        public async Task SetOperatingMode(OperatingMode mode, CancellationToken ct = default)
        {
            await RunWithStreamingPaused(async () =>
            {
                switch (mode)
                {
                    case OperatingMode.Absorbance:
                    case OperatingMode.Transmission:
                        await SetLampMode(_model.HasWhiteLamp ? LampMode.White : LampMode.Off, ct).ConfigureAwait(false);
                        break;
                    case OperatingMode.Intensity:
                        await SetLampMode(LampMode.Off, ct).ConfigureAwait(false);
                        break;
                    case OperatingMode.Fluorescence405:
                        await SetLampMode(LampMode.Fluo405, ct).ConfigureAwait(false);
                        break;
                    case OperatingMode.Fluorescence500:
                        await SetLampMode(LampMode.Fluo500, ct).ConfigureAwait(false);
                        break;
                }

                Session.Mode = mode;
                Session.IsCalibrated = false;
                _processor.Reset();
                _streamFaulted = false;
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets integration time (ms). This invalidates calibration because blank/dark
        /// spectra depend on integration time.
        /// </summary>
        public async Task SetIntegrationTime(int ms, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            await RunWithStreamingPaused(async () =>
            {
                int echoed = await _proto.SetIntegrationTime(ClampIntegrationTime(ms, 1, 1000), ct).ConfigureAwait(false);
                Session.IntegrationTime = echoed;
                Session.IsCalibrated = false;
                _processor.Reset();
                _streamFaulted = false;
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquires one raw spectrum (exclusive protocol access, no streaming required).
        /// </summary>
        public async Task AcquireSingleSpectrum(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            ushort[] raw = await ExecuteExclusive(() => _proto.AcquireRawCounts(ct), ct).ConfigureAwait(false);

            DateTimeOffset timestamp = DateTimeOffset.UtcNow;

            _processor.ProcessSingle(raw, timestamp);

            try
            {
                CountsReceived?.Invoke(raw, timestamp);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "CountsReceived handler threw.");
            }
        }

        // Public API: streaming

        /// <summary>
        /// Starts live streaming (repeated acquisitions in a background loop).
        /// Requires successful <see cref="Initialize"/>.
        /// </summary>
        public void StartStreaming()
        {
            if (!Session.IsInitialized)
            {
                throw new InvalidOperationException("Device not initialized. Call Initialize() first.");
            }

            if (_streamTask is not null && !_streamTask.IsCompleted)
            {
                return;
            }

            _streamCts = new CancellationTokenSource();
            _streamTask = Task.Run(() => StreamingLoop(_streamCts.Token), CancellationToken.None);

            _log?.LogInformation("Live streaming started.");
        }

        /// <summary>
        /// Stops live streaming and waits for the loop to terminate.
        /// </summary>
        public async Task StopStreaming(CancellationToken ct = default)
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
        }

        // Private helpers: lamps and warmup

        /// <summary>
        /// Sets the requested lamp mode. This always switches all lamps off first
        /// to avoid mixed illumination states.
        /// </summary>
        private async Task SetLampMode(LampMode mode, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Turn all lamps off first (prevents mixed states).
            if (_model.HasWhiteLamp)
            {
                await SetWhiteLamp(false, countWarmupTime: true, ct).ConfigureAwait(false);
            }
            if (_model.HasLed405)
            {
                await SetLed(LampMode.Fluo405, false, ct).ConfigureAwait(false);
            }
            if (_model.HasLed500)
            {
                await SetLed(LampMode.Fluo500, false, ct).ConfigureAwait(false);
            }

            switch (mode)
            {
                case LampMode.Off:
                    _lampMode = LampMode.Off;
                    break;
                case LampMode.White:
                    if (!_model.HasWhiteLamp)
                    {
                        throw new InvalidOperationException("White lamp not supported by this model.");
                    }
                    await SetWhiteLamp(true, countWarmupTime: true, ct).ConfigureAwait(false);
                    _lampMode = LampMode.White;
                    break;
                case LampMode.Fluo405:
                    if (!_model.HasLed405)
                    {
                        throw new InvalidOperationException("405 nm LED not supported by this model.");
                    }
                    await SetLed(LampMode.Fluo405, true, ct).ConfigureAwait(false);
                    _lampMode = LampMode.Fluo405;
                    Session.IsCalibrated = false;
                    break;
                case LampMode.Fluo500:
                    if (!_model.HasLed500)
                    {
                        throw new InvalidOperationException("500 nm LED not supported by this model.");
                    }
                    await SetLed(LampMode.Fluo500, true, ct).ConfigureAwait(false);
                    _lampMode = LampMode.Fluo500;
                    Session.IsCalibrated = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown lamp mode.");
            }
        }

        /// <summary>
        /// Turns the white lamp on/off and optionally accounts warmup time.
        /// </summary>
        private async Task SetWhiteLamp(bool on, bool countWarmupTime, CancellationToken ct)
        {
            if (!_model.HasWhiteLamp)
            {
                return;
            }

            await _proto.SetLamp(LampMode.White, on, ct).ConfigureAwait(false);

            if (on)
            {
                _whiteIsOn = true;
                if (countWarmupTime && !_whiteOnStopwatch.IsRunning)
                {
                    _whiteOnStopwatch.Start();
                }
            }
            else
            {
                _whiteIsOn = false;
                if (_whiteOnStopwatch.IsRunning)
                {
                    _whiteOnStopwatch.Stop();
                }
            }
        }

        /// <summary>
        /// Turns a fluorescence LED on/off (guarded by model capability checks).
        /// </summary>
        private async Task SetLed(LampMode ledMode, bool on, CancellationToken ct)
        {
            if (ledMode == LampMode.Fluo405 && !_model.HasLed405)
            {
                _log?.LogDebug("Operating mode not supported: {ledMode}", ledMode);
                return;
            }
            if (ledMode == LampMode.Fluo500 && !_model.HasLed500)
            {
                _log?.LogDebug("Operating mode not supported: {ledMode}", ledMode);
                return;
            }

            await _proto.SetLamp(ledMode, on, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Ensures the white lamp has been on for at least <see cref="RequiredWarmup"/>.
        /// Uses cumulative on-time (Stopwatch) rather than wall time.
        /// </summary>
        private async Task EnsureWarmUp(CancellationToken ct)
        {
            if (SkipWarmup)
            {
                _log?.LogInformation("White lamp warm-up skipped by configuration.");
                return;
            }

            if (!_model.HasWhiteLamp)
            {
                return;
            }

            if (!_whiteIsOn)
            {
                await SetWhiteLamp(on: true, countWarmupTime: true, ct).ConfigureAwait(false);
            }

            TimeSpan onTime = _whiteOnStopwatch.Elapsed;
            if (onTime >= CalibrationWarmup)
            {
                return;
            }

            TimeSpan remaining = CalibrationWarmup - onTime;
            _log?.LogInformation("White lamp warm-up: elapsed={Elapsed}s, remaining={Remaining}s",
                (int)onTime.TotalSeconds, (int)remaining.TotalSeconds);

            await Task.Delay(remaining, ct).ConfigureAwait(false);
        }

        // Private helpers: acquisition averaging

        /// <summary>
        /// Acquires <paramref name="n"/> raw spectra and returns the per-pixel average.
        /// </summary>
        private async Task<ushort[]> AcquireAverageRaw(int n, CancellationToken ct)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);

            uint[]? acc = null;

            for (int i = 0; i < n; i++)
            {
                ct.ThrowIfCancellationRequested();
                ushort[] s = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);

                acc ??= new uint[s.Length];
                if (s.Length != acc.Length) throw new InvalidOperationException("Spectrum length changed during acquisition.");

                for (int k = 0; k < s.Length; k++)
                    acc[k] += s[k];
            }

            ushort[] avg = new ushort[acc!.Length];
            for (int k = 0; k < avg.Length; k++)
                avg[k] = (ushort)(acc[k] / (uint)n);

            return avg;
        }

        // Private helpers: streaming loop

        /// <summary>
        /// Streaming loop: acquires spectra repeatedly and pushes them into the processor.
        /// All protocol access is protected by <see cref="_exclusive"/>.
        /// </summary>
        private async Task StreamingLoop(CancellationToken ct)
        {
            _log?.LogDebug("Measurement data stream loop started.");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _exclusive.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        ushort[] raw = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);
                        DateTimeOffset timeStamp = DateTimeOffset.UtcNow;
                        _processor.PushRaw(raw, timeStamp);

                        // Notify listeners
                        try
                        {
                            CountsReceived?.Invoke(raw, timeStamp);
                        }
                        catch (Exception ex) { _log?.LogWarning(ex, "SpectrumReceived handler threw."); }
                    }
                    finally
                    {
                        _exclusive.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log?.LogError(ex, "Measurement data stream error; continuing.");
                    try
                    {
                        await Task.Delay(50, ct).ConfigureAwait(false);
                    }
                    catch
                    {

                    }
                }
            }
            _log?.LogDebug("Measurement data stream loop stopped.");
        }

        // Private helpers: exclusive protocol execution

        /// <summary>
        /// Executes an asynchronous protocol operation under exclusive access.
        /// </summary>
        private async Task ExecuteExclusive(Func<Task> op, CancellationToken ct)
        {
            await _exclusive.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await op().ConfigureAwait(false);
            }
            finally
            {
                _exclusive.Release();
            }
        }

        private async Task RunWithStreamingPaused(Func<Task> op, CancellationToken ct)
        {
            bool wasStreaming = IsStreamingActive;
            if (wasStreaming)
            {
                await StopStreaming(ct).ConfigureAwait(false);
            }

            try
            {
                await ExecuteExclusive(op, ct).ConfigureAwait(false);
            }
            finally
            {
                if (wasStreaming && !_streamFaulted)
                {
                    StartStreaming();
                }
            }
        }

        /// <summary>
        /// Executes an asynchronous protocol operation under exclusive access and returns its result.
        /// </summary>
        private async Task<T> ExecuteExclusive<T>(Func<Task<T>> op, CancellationToken ct)
        {
            await _exclusive.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await op().ConfigureAwait(false);
            }
            finally
            {
                _exclusive.Release();
            }
        }

        // Private helpers: sanity checks

        private async Task<bool> PrepareInitialization(CancellationToken ct)
        {
            try
            {
                await _proto.WakeUp(ct).ConfigureAwait(false);
                await SetLampMode(LampMode.Off, ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Session.AddDiagnostic(
                    code: "SPECTROVIS.INIT.COMMUNICATION_FAILED",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Initialization,
                    message: "Communication with the spectrometer failed during initialization.",
                    technicalDetails: ex.Message,
                    operation: "Wake up device and switch off lamps",
                    source: nameof(Spectrometer),
                    exception: ex
                );

                return false;
            }
        }

        private async Task<bool> ReadAndStoreModelCode(CancellationToken ct)
        {
            try
            {
                Session.ModelCode = await _proto.GetModelCode(ct).ConfigureAwait(false);

                _log?.LogInformation("Devicd reported model code 0x{Code:X4} (PID=0x{Pid:X4}, Name={Name}).",
                Session.ModelCode, _model.Pid, _model.Name);

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Session.AddDiagnostic(
                    code: "SPECTROVIS.INIT.MODEL_CODE_READ_FAILED",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Initialization,
                    message: "The device model code could not be read.",
                    technicalDetails: ex.Message,
                    operation: "Read model code",
                    exception: ex
                );

                return false;
            }
        }

        private async Task<bool> CheckCcdLinearity(CancellationToken ct)
        {
            try
            {
                byte[] rawBytes = await _proto.ReadLinearitySequence(ct).ConfigureAwait(false);
                CcdLinearity.CcdLinResult result = CcdLinearity.Evaluate(rawBytes, tolerance: LinearityTolerance, minRunLength: LinearityMinRun);

                _log?.LogInformation(
                    "CCD linearity: {Level} (coreLen={CoreLen}, outTol={OutTol}, stepMedian={StepMedian}, stepMad={StepMad}, start={Start}).",
                    result.Level, result.CoreLength, result.OutOfToleranceSteps, result.StepMedian, result.StepMad, result.CoreStartIndex);

                if (result.Level == CcdLinearity.Levels.Fail)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT:LINEARITY_CHECK_FAILED",
                        severity: DiagnosticSeverity.Error,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD linearity check failed",
                        technicalDetails: result.Message,
                        operation: "CCD linearity check",
                        source: nameof(Spectrometer)
                    );

                    return false;
                }

                if (result.Level == CcdLinearity.Levels.Warn)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.LINEARITY_CHECK_WARNING",
                        severity: DiagnosticSeverity.Waring,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD linearity check returned a warning.",
                        operation: "CCD linearity check",
                        source: nameof(Spectrometer)
                    );
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Session.AddDiagnostic(

                    code: "SPECTROVIS.INIT.LINEARITY_CHECK_FAILED",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Initialization,
                    message: "The CCD linearity check could not be completed.",
                    technicalDetails: ex.Message,
                    operation: "CCD linearity check",
                    source: nameof(Spectrometer),
                    exception: ex
                );

                return false;
            }
        }

        private async Task<(bool Succeeded, ushort[]? DarkSpectrum)> CheckDarkNoise(CancellationToken ct)
        {
            try
            {
                await SetLampMode(LampMode.Off, ct).ConfigureAwait(false);

                int integrationTime1 = ClampIntegrationTime(40, 1, 1000);

                int integrationTime2 = ClampIntegrationTime(90, 1, 1000);

                // First dark spectrum
                if (!await SetAndVerifyIntegrationTime(integrationTime1, "first dark-noise measurement", ct).ConfigureAwait(false))
                {
                    return (false, null);
                }

                ushort[] darkSpectrum1 = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);

                if (!ValidateCcdSpectrum(darkSpectrum1, "dark-noise@integrationTime1", out string? firstError, out bool firstWarning))
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.CCD_NOISE_CHECK_FAILED",
                        severity: DiagnosticSeverity.Error,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD returned invalid values during the first dark-noise check.",
                        technicalDetails: firstError,
                        operation: "First dark-noise check",
                        source: nameof(Spectrometer));

                    return (false, null);
                }

                if (firstWarning)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.CCD_NOISE_CHECK_WARNING",
                        severity: DiagnosticSeverity.Waring,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD values varied only minimally during the first dark-noise check.",
                        technicalDetails: firstError,
                        operation: "First dark-noise check",
                        source: nameof(Spectrometer));
                }

                // Second dark spectrum
                if (!await SetAndVerifyIntegrationTime(integrationTime2, "second dark-noise measurement", ct).ConfigureAwait(false))
                {
                    return (false, null);
                }

                ushort[] darkSpectrum2 = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);

                if (!ValidateCcdSpectrum(darkSpectrum2, "dark-noise@integrationTime2", out string? secondError, out bool secondWarning))
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.CCD_NOISE_CHECK_FAILED",
                        severity: DiagnosticSeverity.Error,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD returned invalid values during the second dark-noise check.",
                        technicalDetails: secondError,
                        operation: "Second dark-noise check",
                        source: nameof(Spectrometer));

                    return (false, null);
                }

                if (secondWarning)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.CCD_NOISE_CHECK_WARNING",
                        severity: DiagnosticSeverity.Waring,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD values varied only minimally during the second dark-noise check.",
                        technicalDetails: secondError,
                        operation: "Second dark-noise check",
                        source: nameof(Spectrometer));
                }

                // Restore the common default integration time of 30 ms.
                if (!await SetAndVerifyIntegrationTime(DefaultIntegrationTime, "default integration time", ct).ConfigureAwait(false))
                {
                    return (false, null);
                }

                ushort[] darkSpectrum3 = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);

                if (!ValidateCcdSpectrum(darkSpectrum3, "dark spectrum at default integration time", out string? defaultError, out bool defaultWarning))
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.CCD_DARK_REFERENCE_INVALID",
                        severity: DiagnosticSeverity.Error,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD returned an invalid dark spectrum at the default integration time.",
                        technicalDetails: defaultError,
                        operation: "Acquire dark reference at 30 ms",
                        source: nameof(Spectrometer));

                    return (false, null);
                }

                if (defaultWarning)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.CCD_DARK_REFERENCE_WARNING",
                        severity: DiagnosticSeverity.Waring,
                        category: DiagnosticCategory.Initialization,
                        message: "The dark spectrum at the default integration time showed very little variation.",
                        technicalDetails: defaultError,
                        operation: "Acquire dark reference at 30 ms",
                        source: nameof(Spectrometer));
                }

                Session.IntegrationTime = DefaultIntegrationTime;

                return (true, darkSpectrum3);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Session.AddDiagnostic(
                    code: "SPECTROVIS.INIT.CCD_NOISE_CHECK_FAILED",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Initialization,
                    message: "The CCD dark-noise check could not be completed.",
                    technicalDetails: ex.Message,
                    operation: "CCD dark-noise check",
                    source: nameof(Spectrometer),
                    exception: ex);

                return (false, null);
            }
        }

        private async Task<bool> SetAndVerifyIntegrationTime(int requestedMs, string context, CancellationToken ct)
        {
            try
            {
                int echoedMs = await _proto.SetIntegrationTime(requestedMs, ct).ConfigureAwait(false);

                if (echoedMs != requestedMs)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.INTEGRATION_TIME_CHECK_FAILED",
                        severity: DiagnosticSeverity.Error,
                        category: DiagnosticCategory.Initialization,
                        message: "The device did not confirm the requested integration time.",
                        technicalDetails: $"Context={context}; requested={requestedMs} ms; reported={echoedMs} ms",
                        operation: "Set and verify integration time.",
                        source: nameof(Spectrometer)
                    );

                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Session.AddDiagnostic(
                    code: "SPECTROVIS.INIT.INTEGRATION_TIME_CHECK_FAILED",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Initialization,
                    message: "The integration time could not be set or queried.",
                    technicalDetails: $"Context={context}; requested={requestedMs} ms; error={ex.Message}",
                    operation: "Set and verify integration time",
                    source: nameof(Spectrometer),
                    exception: ex
                );

                return false;
            }
        }

        private async Task<bool> CheckWhiteLampResponse(ushort[] darkSpectrum, CancellationToken ct)
        {
            try
            {
                await SetLampMode(LampMode.White, ct).ConfigureAwait(false);

                // Mandatory initialization warm-up.
                await Task.Delay(InitializationWarmup, ct).ConfigureAwait(false);

                ushort[] lightSpectrum = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);

                if (!ValidateCcdSpectrum(lightSpectrum, "white-lamp light spectrum", out string? lightDetails, out bool lightWarning))
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.LIGHT_RESPONSE_CHECK_FAILED",
                        severity: DiagnosticSeverity.Error,
                        category: DiagnosticCategory.Initialization,
                        message: "The CCD returned invalid values during the white-lamp check.",
                        technicalDetails: lightDetails,
                        operation: "White-lamp response check",
                        source: nameof(Spectrometer));

                    return false;
                }

                if (lightWarning)
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.LIGHT_RESPONSE_CHECK_WARNING",
                        severity: DiagnosticSeverity.Waring,
                        category: DiagnosticCategory.Initialization,
                        message: "The white-light spectrum showed very little variation.",
                        technicalDetails: lightDetails,
                        operation: "White-lamp response check",
                        source: nameof(Spectrometer));
                }

                double darkMean = MeanInRoi(darkSpectrum);
                double lightMean = MeanInRoi(lightSpectrum);
                double ratio = darkMean > 0 ? lightMean / darkMean : double.PositiveInfinity;

                if (!MeanInRoiIsHigher(lightSpectrum, darkSpectrum, factor: 5.0))
                {
                    Session.AddDiagnostic(
                        code: "SPECTROVIS.INIT.LIGHT_RESPONSE_CHECK_FAILED",
                        severity: DiagnosticSeverity.Error,
                        category: DiagnosticCategory.Initialization,
                        message: "The white lamp produced insufficient CCD response.",
                        technicalDetails:
                            $"Dark ROI mean={darkMean:F2}; " +
                            $"light ROI mean={lightMean:F2}; " +
                            $"ratio={ratio:F2}; required ratio>=5.00.",
                        operation: "White-lamp response check",
                        source: nameof(Spectrometer));

                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Session.AddDiagnostic(
                    code: "SPECTROVIS.INIT.LIGHT_RESPONSE_CHECK_FAILED",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Initialization,
                    message: "The white-lamp response check could not be completed.",
                    technicalDetails: ex.Message,
                    operation: "White-lamp response check",
                    source: nameof(Spectrometer),
                    exception: ex);

                return false;
            }
        }

        /// <summary>
        /// Basic CCD "alive" check: rejects empty spectra and constant 0/65535 outputs.
        /// Also emits a warning if ROI variation is extremely small.
        /// </summary>
        private bool ValidateCcdSpectrum(ushort[]? counts, string context, out string? diagnosticDetails, out bool hasWarning)
        {
            diagnosticDetails = null;
            hasWarning = false;

            if (counts is null)
            {
                diagnosticDetails = $"CCD check failed ({context}): no spectrum was recorded.";

                return false;
            }

            if (counts.Length == 0)
            {
                diagnosticDetails = $"CCD check failed ({context}): the spectrum is empty.";

                return false;
            }

            bool allZero = counts.All(value => value == 0);
            bool allMaximum = counts.All(value => value == ushort.MaxValue);

            if (allZero || allMaximum)
            {
                diagnosticDetails = $"CCD check failed ({context}): spectrum is constant " +
                    $"{(allZero ? "0" : "65535")} " + "(likely defective CCD or protocol mismatch).";

                return false;
            }

            (int lo, int hi) = GetRoi(counts.Length);

            ushort minimum = ushort.MaxValue;
            ushort maximum = ushort.MinValue;

            for (int i = lo; i <= hi; i++)
            {
                ushort value = counts[i];

                if (value < minimum)
                {
                    minimum = value;
                }

                if (value > maximum)
                {
                    maximum = value;
                }
            }

            int range = maximum - minimum;

            if (range < 2)
            {
                diagnosticDetails = $"CCD check warning ({context}): ROI variation is extremely small " +
                    $"(maximum - minimum = {range}, minimum = {minimum}, maximum = {maximum}).";

                hasWarning = true;
            }

            return true;
        }

        // Private helpers: integration time search

        /// <summary>
        /// Searches an integration time in [tMin..tMax] that places the ROI mean ratio
        /// inside the target band [TargetLo..TargetHi]. Uses a binary-search-like approach
        /// and tracks the best candidate if the band is not reached.
        /// </summary>
        private async Task<(int tMs, double ratio, bool inBand)> FindIntegrationTimeForTargetBand(
            int tMin, int tMax, int maxIter, int probeAverages, CancellationToken ct)
        {
            int lo = tMin;
            int hi = tMax;

            int bestT = lo;
            double bestRatio = double.NaN;
            double bestDist = double.PositiveInfinity;

            const double targetMid = (TargetLo + TargetHi) / 2.0;

            for (int i = 0; i < maxIter && lo <= hi; i++)
            {
                ct.ThrowIfCancellationRequested();

                int t = lo + (hi - lo) / 2;

                int echoed = await _proto.SetIntegrationTime(t, ct).ConfigureAwait(false);
                Session.IntegrationTime = echoed;

                ushort[] probe = (probeAverages <= 1)
                    ? await _proto.AcquireRawCounts(ct).ConfigureAwait(false)
                    : await AcquireAverageRaw(probeAverages, ct).ConfigureAwait(false);

                double ratio = MeanRatioInRoi(probe);

                double dist = Math.Abs(ratio - targetMid);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestT = echoed;
                    bestRatio = ratio;
                }

                if (ratio < TargetLo)
                {
                    lo = t + 1;   // too dark -> longer integration
                }
                else if (ratio > TargetHi)
                {
                    hi = t - 1;   // too bright -> shorter integration
                }
                else
                {
                    return (echoed, ratio, true);
                }
            }

            return (bestT, bestRatio, false);
        }

        // Private helpers: ROI (region of interest) and statistics

        /// <summary>
        /// Returns ROI pixel indices clamped to a given spectrum length.
        /// Uses model-specific ROI if provided; otherwise falls back to a default range.
        /// </summary>
        private (int lo, int hi) GetRoi(int spectrumLen)
        {
            if (_model.CCDPixelIndexMin > 0 || _model.CCDPixelIndexMax > 0)
            {
                int lo = Math.Clamp(_model.CCDPixelIndexMin, 0, spectrumLen - 1);
                int hi = Math.Clamp(_model.CCDPixelIndexMax, 0, spectrumLen - 1);
                if (hi < lo)
                {
                    (lo, hi) = (hi, lo);
                }
                return (lo, hi);
            }

            int a = Math.Clamp(100, 0, spectrumLen - 1);
            int b = Math.Clamp(900, 0, spectrumLen - 1);
            if (b < a)
            {
                (a, b) = (b, a);
            }
            return (a, b);
        }

        /// <summary>
        /// Returns ROI indices without clamping (model values or default 100..900).
        /// Consumers must clamp to actual spectrum length.
        /// </summary>
        private (int lo, int hi) GetRoi()
        {
            if (_model.CCDPixelIndexMin > 0 || _model.CCDPixelIndexMax > 0)
            {
                return (_model.CCDPixelIndexMin, _model.CCDPixelIndexMax);
            }

            return (100, 900);
        }

        /// <summary>
        /// Compares two spectra by the mean in ROI. Returns true if mean(a) >= factor * mean(b).
        /// </summary>
        private bool MeanInRoiIsHigher(ushort[] a, ushort[] b, double factor)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            double ma = MeanInRoi(a);
            double mb = MeanInRoi(b);

            _log?.LogInformation("Ratio of mean in ROI is {ma}/{mb}.", ma, mb);

            if (mb <= 0)
            {
                return ma > 0;
            }

            return (ma / mb) >= factor;

        }

        /// <summary>
        /// Computes the mean raw count value in ROI.
        /// </summary>
        private double MeanInRoi(ushort[] counts)
        {
            (int lo, int hi) = GetRoi();
            lo = Math.Clamp(lo, 0, counts.Length - 1);
            hi = Math.Clamp(hi, 0, counts.Length - 1);
            if (hi < lo)
            {
                (lo, hi) = (hi, lo);
            }

            double sum = 0;
            int n = 0;
            for (int i = lo; i <= hi; i++)
            {
                sum += counts[i];
                n++;
            }

            return n == 0 ? 0.0 : sum / n;
        }

        /// <summary>
        /// Computes ROI mean normalized to [0..1] by dividing by ushort.MaxValue.
        /// Used to steer integration time search against the target band.
        /// </summary>
        private double MeanRatioInRoi(ushort[] counts)
        {
            (int lo, int hi) = GetRoi();
            lo = Math.Clamp(lo, 0, counts.Length - 1);
            hi = Math.Clamp(hi, 0, counts.Length - 1);
            if (hi < lo)
            {
                (lo, hi) = (hi, lo);
            }

            double sum = 0;
            int n = 0;
            for (int i = lo; i <= hi; i++)
            {
                sum += counts[i];
                n++;
            }

            if (n == 0)
            {
                return 0.0;
            }

            return sum / n / ushort.MaxValue;
        }

        /// <summary>
        /// Clamps integration time to a safe operating interval.
        /// </summary>
        private static int ClampIntegrationTime(int ms, int min, int max) => Math.Min(max, Math.Max(min, ms));
    }
}