namespace Backend.Transport;

using System.Threading.Channels;
using HidSharp;
using Microsoft.Extensions.Logging;

/// <summary>
/// HID-based transport implementation using HidSharp.
/// 
/// Architecture:
/// - A background ReaderLoop continuously reads HID input reports.
/// - Incoming 64-byte payloads are pushed into a bounded Channel (FIFO).
/// - Public Read() consumes from that buffered channel.
/// - Write() sends one 64-byte payload per HID output report.
/// 
/// This decouples low-level blocking HID I/O from higher-level protocol logic.
/// </summary>
public sealed class HidTransport(string devicePath, ushort vid, ushort pid, ILogger<HidTransport>? log = null) : ITransport {
    private readonly string _devicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));
    private readonly ushort _vid = vid;
    private readonly ushort _pid = pid;
    private readonly ILogger<HidTransport>? _log = log;

    private HidStream? _stream;

    // Fixed payload size expected by the device protocol
    private const int PayloadLen = 64;
    // HID report ID (0x00 for most devices without numbered reports)
    private const byte ReportId = 0x00;
    // Max number of buffered packets in the RX channel
    private const int PacketQueueCapacity = 1024;
    // Overall timeout for buffered Read()
    private const int OverallReadTimeoutMs = 2000;

    private int _maxInputReportLen;
    private int _maxOutputReportLen;

    // Channel uses as a thread-safe FIFO between ReaderLoop and Read()
    private Channel<byte[]>? _rxChannel;
    // Controls cancellation of ReaderLoop
    private CancellationTokenSource? _rxCts;
    // Background reader task
    private Task? _rxTask;

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// True if the HID stream is currently open.
    /// </summary>
    public bool IsConnected => _stream is not null;

    /// <summary>
    /// Opens the HID device, initializes report sizes,
    /// and starts the background ReaderLoop.
    /// </summary>
    public Task Connect(CancellationToken ct = default) {
        if (IsConnected) { return Task.CompletedTask; }
        ct.ThrowIfCancellationRequested();

        // Locate matching HID device by VID/PID and device path
        HidDevice? dev = DeviceList.Local
            .GetHidDevices(_vid, _pid)
            .FirstOrDefault(d => string.Equals(d.DevicePath, _devicePath, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"HID device not found (VID=0x{_vid:X4}, PID=0x{_pid:X4}, path='{_devicePath}')");
        if (!dev.TryOpen(out HidStream stream)) {
            throw new InvalidOperationException("Failed to open HID device stream.");
        }

        _stream = stream;

        // Ensure report buffers are large enough (ReportID + 64 payload)
        _maxInputReportLen = Math.Max(stream.Device.GetMaxInputReportLength(), PayloadLen + 1);
        _maxOutputReportLen = Math.Max(stream.Device.GetMaxOutputReportLength(), PayloadLen + 1);

        // Block until an input report arrives or the stream is closed during Disconnect()
        _stream.ReadTimeout = Timeout.Infinite;
        // Write timeout for outbound commands
        _stream.WriteTimeout = 200;

        // Bounded FIFO channel between ReaderLoop (writer) and Read() (readers)
        ChannelOptions options = new BoundedChannelOptions(PacketQueueCapacity) {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        };

        _rxChannel = Channel.CreateBounded<byte[]>((BoundedChannelOptions)options);
        _rxCts = new CancellationTokenSource();

        // Start background reader (decoupled from caller cancellation)
        _rxTask = Task.Run(() => ReaderLoop(_rxCts.Token), CancellationToken.None);

        _log?.LogInformation("HID transport connected. VID=0x{Vid:X4}, PID=0x{Pid:X4}, maxIn={MaxIn}, maxOut={MaxOut}",
        _vid, _pid, _maxInputReportLen, _maxOutputReportLen);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops ReaderLoop, completes RX channel,
    /// and disposes the HID stream.
    /// </summary>
    public async Task Disconnect(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        CancellationTokenSource? rxCts = _rxCts;
        _rxCts = null;

        if (rxCts is not null) {
            try { rxCts.Cancel(); }
            catch (Exception ex) {
                _log?.LogWarning(ex, "Cancel RX loop failed.");
            }
            finally { rxCts.Dispose(); }
        }

        HidStream? stream = _stream;
        _stream = null;

        if (stream is not null) {
            try {
                stream.Dispose();
            }
            catch (Exception ex) {
                _log?.LogWarning(ex, "Disposing HID stream failed.");
            }
        }

        Task? rxTask = _rxTask;
        _rxTask = null;

        if (rxTask is not null) {
            try { await rxTask.ConfigureAwait(false); }
            catch (Exception ex) {
                _log?.LogWarning(ex, "RX task ended with exception.");
            }
        }

        if (rxCts is not null) {
            try {
                rxCts.Dispose();
            }
            catch (Exception ex) {
                _log?.LogWarning(ex, "Disposing RX cancellation source failed.");
            }
        }

        Channel<byte[]>? ch = _rxChannel;
        _rxChannel = null;

        if (ch is not null) {
            try {
                ch.Writer.TryComplete();
            }
            catch (Exception ex) {
                _log?.LogWarning(ex, "Completing RX channel failed.");
            }
        }

        _log?.LogInformation("HID transport disconnected.");
    }

    /// <summary>
    /// Sends one 64-byte protocol payload as a HID output report.
    /// The payload is copied into a full report buffer including ReportID.
    /// </summary>
    public async Task Write(ReadOnlyMemory<byte> payload, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        if (payload.Length > PayloadLen) {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"Payload must be <= {PayloadLen} bytes, got {payload.Length}.");
        }

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);

        try {
            EnsureConnected();

            byte[] outBuf = new byte[_maxOutputReportLen];
            outBuf[0] = ReportId;

            payload.Span.CopyTo(outBuf.AsSpan(1));

            try {
                _stream!.Write(outBuf, 0, outBuf.Length);
                _log?.LogTrace("HID OUT wrote {len} bytes (payload {payloadLen}).",
                        outBuf.Length, payload.Length);
            }
            catch (Exception ex) {
                _log?.LogError(ex, "HID OUT write failed.");
                throw;
            }
        }
        finally {
            _writeGate.Release();
        }
    }

    //// <summary>
    /// Returns one 64-byte payload from the internal RX buffer.
    /// Does NOT access the HID stream directly.
    /// 
    /// Throws TimeoutException if no packet becomes available
    /// within OverallReadTimeoutMs.
    /// </summary>
    public async Task<byte[]> Read(CancellationToken ct = default) {
        EnsureConnected();

        Channel<byte[]> ch = _rxChannel ?? throw new InvalidOperationException("RX channel not initialized. Call Connect() first.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(OverallReadTimeoutMs);

        try {
            while (await ch.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false)) {
                if (ch.Reader.TryRead(out var packet)) {
                    return packet;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            _log?.LogWarning("Buffered read timeout after {Ms}ms.", OverallReadTimeoutMs);
            throw new TimeoutException($"Buffered read exceeded overall timeout of {OverallReadTimeoutMs} ms (no packet available).");
        }

        throw new InvalidOperationException("RX channel completed (device disconnected?).");
    }


    /// <summary>
    /// Discards all currently buffered packets in the RX channel.
    /// Useful to resynchronize protocol state between commands.
    /// </summary>
    public void FlushInputBuffer() {
        Channel<byte[]>? ch = _rxChannel;
        if (ch is null) {
            return;
        }

        int drained = 0;
        while (ch.Reader.TryRead(out _)) {
            drained++;
        }

        if (drained > 0) {
            _log?.LogDebug("Flushed RX buffer: {Count} packets discarded.", drained);
        }
    }

    /// <summary>
    /// Best-effort drain of up to expectedPackets from the RX buffer.
    /// 
    /// Waits perPacketTimeoutMs for each missing packet.
    /// Intended for command-response cleanup scenarios.
    /// </summary>
    public async Task<int> Drain(int expectedPackets, int perPacketTimeoutMs = 100, CancellationToken ct = default) {
        if (expectedPackets <= 0) {
            return 0;
        }

        Channel<byte[]>? ch = _rxChannel;
        if (ch is null) {
            return 0;
        }

        int drained = 0;

        for (int i = 0; i < expectedPackets; i++) {
            ct.ThrowIfCancellationRequested();

            if (ch.Reader.TryRead(out _)) {
                drained++;
                continue;
            }

            using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
            one.CancelAfter(perPacketTimeoutMs);

            try {
                if (!await ch.Reader.WaitToReadAsync(one.Token).ConfigureAwait(false)) {
                    break;
                }

                if (ch.Reader.TryRead(out _)) {
                    drained++;
                } else {
                    break;
                }
            }
            catch (OperationCanceledException) {
                break;
            }
        }

        if (drained > 0) {
            _log?.LogWarning("Drained {Drained}/{Expected} expected response packets (best-effort).", drained, expectedPackets);
        }

        return drained;
    }

    /// <summary>
    /// Stops transport and releases resources.
    /// </summary>
    public void Dispose() {
        try {
            Disconnect(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) {
            _log?.LogWarning(ex, "Dispose->Disconnect failed.");
        }

        _writeGate.Release();
    }

    /// <summary>
    /// Background loop reading HID input reports.
    /// 
    /// Behavior:
    /// - Reads raw HID reports.
    /// - Strips ReportID if present.
    /// - Extracts up to 64 payload bytes.
    /// - Pushes payload into RX channel.
    /// 
    /// Terminates on cancellation or fatal stream error.
    /// </summary>
    private async Task ReaderLoop(CancellationToken ct) {
        HidStream? stream = _stream;
        Channel<byte[]>? ch = _rxChannel;

        if (stream is null || ch is null) {
            _log?.LogWarning("ReaderLoop started without stream/channel.");
            return;
        }

        byte[] inBuf = new byte[_maxInputReportLen];

        _log?.LogDebug("ReaderLoop started.");

        try {
            while (!ct.IsCancellationRequested) {
                int n;

                try {
                    // Blocking read, no polling timeout
                    n = stream.Read(inBuf, 0, inBuf.Length);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested) {
                    break;
                }
                catch (IOException) when (ct.IsCancellationRequested) {
                    break;
                }
                catch (TimeoutException) when (ct.IsCancellationRequested) {
                    break;
                }
                catch (Exception ex) when (ct.IsCancellationRequested) {
                    _log?.LogDebug(ex, "ReaderLoop read interrupted during shutdown.");
                    break;
                }
                catch (Exception ex) {
                    _log?.LogError(ex, "ReaderLoop fatal read error. Completing channel.");

                    try {
                        ch.Writer.TryComplete(ex);
                    }
                    catch {
                        // Ignore completion races
                    }

                    return;
                }

                if (n <= 0) {
                    continue;
                }

                // Skip ReportID if present
                int offset = (n >= PayloadLen + 1) ? 1 : 0;
                int available = n - offset;

                if (available <= 0) {
                    continue;
                }

                byte[] payload = new byte[PayloadLen];
                int copyLen = Math.Min(PayloadLen, available);
                Array.Copy(inBuf, offset, payload, 0, copyLen);

                try {
                    await ch.Writer.WriteAsync(payload, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (InvalidOperationException) {
                    // Channel was completed during disconnect
                    break;
                }
            }
        }
        finally {
            _log?.LogDebug("ReaderLoop stopped.");

            try {
                ch.Writer.TryComplete();
            }
            catch {
                // Ignore completion races
            }
        }
    }

    private void EnsureConnected() {
        if (_stream is null) {
            throw new InvalidOperationException("Transport is not connected. Call Connect() first.");
        }
    }
}