using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
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
            int integrationTime1 = ClampIntegrationTime(_model.IntegrationTimeMsMean, 1, 1000);
            int integrationTime2 = ClampIntegrationTime(Math.Max(integrationTime1 + 50, 10), 1, 1000);

            int echoed1 = await _proto.SetIntegrationTime(integrationTime1, ct).ConfigureAwait(false);
            if (echoed1 != integrationTime1)
            {
                _warnings.Add($"Integration time echo mismatch (expected {integrationTime1} ms, got {echoed1} ms).");
            }

            ushort[] darkSpectrum1 = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);
            EnsureCcdAlive(darkSpectrum1, "dark-noise@integrationTime1");

            int echoed2 = await _proto.SetIntegrationTime(integrationTime2, ct).ConfigureAwait(false);
            if (echoed2 != integrationTime2)
            {
                _warnings.Add($"Integration time echo mismatched (expected {integrationTime2} ms, got {echoed2} ms).");
            }

            ushort[] darkSpectrum2 = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);
            EnsureCcdAlive(darkSpectrum2, "dark-noise@integrationTime2");

            // 4) Set default integration time (30 ms)
            int echoedDefault = await _proto.SetIntegrationTime(DefaultIntegrationTime, ct).ConfigureAwait(false);
            if (echoedDefault != DefaultIntegrationTime)
            {
                _warnings.Add($"Integration time echo mismatch (expected {DefaultIntegrationTime} ms, got {echoedDefault} ms).");
            }
            Session.IntegrationTime = echoedDefault;

            // 5) Lamp checks: switch OFF all lamps (where present) and validate echo.
            //  Then switch ON white lamp (where present) as default mode.
            await InitializeLamps(ct).ConfigureAwait(false);

            // 6) Light-vs-dark sanity check (only if white lamp exists).
            if (_model.HasWhiteLamp)
            {
                // Compare dark spectrum from above with light spectrum
                ushort[] lightSpectrum = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);
                if (!MeanInRoiIsHigher(lightSpectrum, darkSpectrum2, factor: 2.0))
                {
                    _warnings.Add("White lamp check: light spectrum is not significantly higher than dark spectrum (ROI mean factor < 2). Lamp may be weak or optical path blocked.");
                }
            }

            Session.IsReady = true;
            Session.IsCalibrated = false;
            _isInitialized = true;

            _log?.LogInformation("Spectrometer initialized. Ready={Ready}, Calibrated={Calibrated}, WhiteOn={WhiteOn}, Warmup={WarmupSeconds}s, Warnings={WarningsCount}",
                Session.IsReady, Session.IsCalibrated, _whiteIsOn, (int)_whiteOnStopwatch.Elapsed.TotalSeconds, _warnings.Count);
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

        public async Task Calibrate(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_isInitialized)
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
            int integrationTime = ClampIntegrationTime(Session.IntegrationTime > 0 ? Session.IntegrationTime : _model.IntegrationTimeMsMean, 1, 1000);
            int step = Math.Max(5, integrationTime / 2);
            double lastRatio = double.NaN;

            for (int i = 0; i < 6; i++)
            {
                ct.ThrowIfCancellationRequested();

                int echoed = await _proto.SetIntegrationTime(integrationTime, ct).ConfigureAwait(false);
                Session.IntegrationTime = echoed;

                ushort[] spectrum = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);
                double ratio = MeanRatioInRoi(spectrum);

                lastRatio = ratio;

                if (ratio < TargetLo)
                {
                    integrationTime = ClampIntegrationTime(integrationTime + step, 1, 1000);
                }
                else if (ratio > TargetHi)
                {
                    integrationTime = ClampIntegrationTime(integrationTime - step, 1, 1000);
                }
                else
                {
                    // good enough
                    break;
                }

                step = Math.Max(1, step / 2);
            }

            if (double.IsNaN(lastRatio) || lastRatio < 0)
            {
                throw new InvalidOperationException("Calibration failed: invalid intensity ratio in ROI.");
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
            Session.IsReady = true;

            _log?.LogInformation("Calibration completed. t={T}ms, blank/dark average over {N} spectra, warmup={WarmupSeconds}s",
            Session.IntegrationTime, CalibrationAverages, (int)_whiteOnStopwatch.Elapsed.TotalSeconds);
        }

        public async Task SetIntegrationTime(int ms, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            int echoed = await _proto.SetIntegrationTime(ClampIntegrationTime(ms, 1, 1000), ct).ConfigureAwait(false);
            Session.IntegrationTime = echoed;

            Session.IsCalibrated = false;
        }

        public async Task SetLampMode(LampMode mode, CancellationToken ct = default)
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
                    Session.LampMode = LampMode.Off;
                    break;
                case LampMode.White:
                    if (!_model.HasWhiteLamp)
                    {
                        throw new InvalidOperationException("White lamp not supported by this model.");
                    }
                    await SetWhiteLamp(true, countWarmupTime: true, ct).ConfigureAwait(false);
                    Session.LampMode = LampMode.White;
                    break;
                case LampMode.Fluo405:
                    if (!_model.HasLed405)
                    {
                        throw new InvalidOperationException("405 nm LED not supported by this model.");
                    }
                    await SetLed(LampMode.Fluo405, true, ct).ConfigureAwait(false);
                    Session.LampMode = LampMode.Fluo405;
                    Session.IsCalibrated = false;
                    break;
                case LampMode.Fluo500:
                    if (!_model.HasLed500)
                    {
                        throw new InvalidOperationException("500 nm LED not supported by this model.");
                    }
                    await SetLed(LampMode.Fluo500, true, ct).ConfigureAwait(false);
                    Session.LampMode = LampMode.Fluo500;
                    Session.IsCalibrated = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown lamp mode.");
            }
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

        private async Task InitializeLamps(CancellationToken ct)
        {
            if (_model.HasWhiteLamp)
            {
                await SetWhiteLamp(on: false, countWarmupTime: true, ct).ConfigureAwait(false);
            }
            if (_model.HasLed405)
            {
                await _proto.SetLamp(LampMode.Fluo405, false, ct).ConfigureAwait(false);
            }
            if (_model.HasLed500)
            {
                await _proto.SetLamp(LampMode.Fluo500, false, ct).ConfigureAwait(false);
            }

            if (_model.HasWhiteLamp)
            {
                await SetWhiteLamp(on: true, countWarmupTime: true, ct).ConfigureAwait(false);
                Session.LampMode = LampMode.White;
            }
            else
            {
                Session.LampMode = LampMode.Off;
            }
        }

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

        private async Task EnsureWarmUp(CancellationToken ct)
        {
            if (!_model.HasWhiteLamp)
            {
                return;
            }

            if (!_whiteIsOn)
            {
                await SetWhiteLamp(on: true, countWarmupTime: true, ct).ConfigureAwait(false);
            }

            TimeSpan onTime = _whiteOnStopwatch.Elapsed;
            if (onTime >= RequiredWarmup)
            {
                return;
            }

            TimeSpan remaining = RequiredWarmup - onTime;
            _log?.LogInformation("White lamp warm-up: elapsed={Elapsed}s, remaining={Remaining}s",
                (int)onTime.TotalSeconds, (int)remaining.TotalSeconds);

            await Task.Delay(remaining, ct).ConfigureAwait(false);
        }

        private async Task SetLed(LampMode ledMode, bool on, CancellationToken ct)
        {
            if (ledMode == LampMode.Fluo405 && !_model.HasLed405)
            {
                return;
            }
            if (ledMode == LampMode.Fluo500 && !_model.HasLed500)
            {
                return;
            }

            await _proto.SetLamp(ledMode, on, ct).ConfigureAwait(false);
        }

        private async Task<ushort[]> AcquireAverageRaw(int n, CancellationToken ct)
        {
            if (n < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(n));
            }

            ushort[]? sum = null;

            for (int i = 0; i < n; i++)
            {
                ct.ThrowIfCancellationRequested();

                ushort[] spectrum = await _proto.AcquireRawCounts(ct).ConfigureAwait(false);
                if (sum is null)
                {
                    sum = new ushort[spectrum.Length];
                }
                if (spectrum.Length != sum.Length)
                {
                    throw new InvalidOperationException("Spectrum length changed during acquisition.");
                }

                for (int k = 0; k < spectrum.Length; k++)
                {
                    sum[k] = (ushort)(sum[k] + spectrum[k] / n);
                }
            }

            return sum!;
        }

        // Measurement data stream + pause/resume boundary control
        private async Task StreamLoop(CancellationToken ct)
        {

        }

        private void EnsureCcdAlive(ushort[] counts, string context)
        {
            if (counts.Length == 0)
            {
                throw new InvalidOperationException($"CCD check failed ({context}): empty spectrum.");
            }

            bool allZero = counts.All(v => v == 0);
            bool allMax = counts.All(v => v == ushort.MaxValue);

            if (allZero || allMax)
            {
                throw new InvalidOperationException($"CCD check failed ({context}): spectrum is constant {(allZero ? "0" : "66535")} (likely defective CCD or protocol mismatch).");
            }

            (int lo, int hi) = GetRoi(counts.Length);
            ushort min = ushort.MaxValue, max = 0;
            for (int i = lo; i <= hi; i++)
            {
                ushort v = counts[i];
                if (v < min)
                {
                    min = v;
                }
                if (v > max)
                {
                    max = v;
                }
            }

            if (max - min < 2)
            {
                _warnings.Add($"CCD check warning ({context}): ROI variation extremely small (max-min={max - min}).");
            }
        }

        // ROI: Region of interest
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

        private (int lo, int hi) GetRoi()
        {
            if (_model.CCDPixelIndexMin > 0 || _model.CCDPixelIndexMax > 0)
            {
                return (_model.CCDPixelIndexMin, _model.CCDPixelIndexMax);
            }

            return (100, 900);
        }

        private bool MeanInRoiIsHigher(ushort[] a, ushort[] b, double factor)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            double ma = MeanInRoi(a);
            double mb = MeanInRoi(b);

            if (mb <= 0)
            {
                return ma > 0;
            }

            return (ma / mb) >= factor;

        }

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
            for (int i = 0; i <= hi; i++)
            {
                sum += counts[i];
                n++;
            }

            return n == 0 ? 0.0 : sum / n;
        }

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

        // Integration time should not exceed a certain threshold to avoid CCD saturation
        private static int ClampIntegrationTime(int ms, int min, int max) => Math.Min(max, Math.Max(min, ms));
    }
}