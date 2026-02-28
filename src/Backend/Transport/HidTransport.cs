namespace Backend.Transport;

using System.Globalization;
using System.Threading.Channels;
using HidSharp;
using Microsoft.Extensions.Logging;

public sealed class HidTransport : ITransport
{
    private readonly string _devicePath;
    private readonly ushort _vid;
    private readonly ushort _pid;
    private readonly ILogger<HidTransport>? _log;

    private HidStream? _stream;

    private const int PayloadLen = 64;
    private const byte ReportId = 0x00;
    private const int PacketQueueCapacity = 1024;
    private const int OverallReadTimeoutMs = 2000;

    private int _maxInputReportLen;
    private int _maxOutputReportLen;

    private Channel<byte[]>? _rxChannel;
    private CancellationTokenSource? _rxCts;
    private Task? _rxTask;

    public HidTransport(string devicePath, ushort vid, ushort pid)
    {
        _devicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));
        _vid = vid;
        _pid = pid;
    }

    public bool IsConnected => _stream is not null;

    public Task Connect(CancellationToken ct = default)
    {
        if (IsConnected) { return Task.CompletedTask; }
        ct.ThrowIfCancellationRequested();

        HidDevice? dev = DeviceList.Local
            .GetHidDevices(_vid, _pid)
            .FirstOrDefault(d => string.Equals(d.DevicePath, _devicePath, StringComparison.OrdinalIgnoreCase));

        if (dev is null)
        {
            throw new InvalidOperationException($"HID device not found (VID=0x{_vid:X4}, PID=0x{_pid:X4}, path='{_devicePath}')");
        }

        if (!dev.TryOpen(out HidStream stream))
        {
            throw new InvalidOperationException("Failed to open HID device stream.");
        }

        _stream = stream;

        _maxInputReportLen = Math.Max(stream.Device.GetMaxInputReportLength(), PayloadLen + 1);
        _maxOutputReportLen = Math.Max(stream.Device.GetMaxOutputReportLength(), PayloadLen + 1);

        _stream.ReadTimeout = 50;
        _stream.WriteTimeout = 200;

        ChannelOptions options = new BoundedChannelOptions(PacketQueueCapacity)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        };

        _rxChannel = Channel.CreateBounded<byte[]>((BoundedChannelOptions)options);
        _rxCts = new CancellationTokenSource();
        _rxTask = Task.Run(() => ReaderLoop(_rxCts.Token), CancellationToken.None);

        _log?.LogInformation("HID transport connected. VID=0x{Vid:X4}, PID=0x{Pid:X4}, maxIn={MaxIn}, maxOut={MaxOut}",
        _vid, _pid, _maxInputReportLen, _maxOutputReportLen);

        return Task.CompletedTask;
    }

    public async Task Disconnect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        CancellationTokenSource? rxCts = _rxCts;
        _rxCts = null;

        if (rxCts is not null)
        {
            try { rxCts.Cancel(); }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Cancel RX loop failed.");
            }
            finally { rxCts.Dispose(); }
        }

        Task? rxTask = _rxTask;
        _rxTask = null;

        if (rxTask is not null)
        {
            try { await rxTask.ConfigureAwait(false); }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "RX task ended with exception.");
            }
        }

        Channel<byte[]>? ch = _rxChannel;
        _rxChannel = null;

        if (ch is not null)
        {
            try
            {
                ch.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Completing RX channel failed.");
            }
        }

        HidStream? stream = _stream;
        _stream = null;

        try
        {
            stream?.Dispose();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Disposing HID stream failed.");
        }
        _log?.LogInformation("HID transport disconnected.");

    }

    public Task Write(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        if (payload.Length > PayloadLen)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"Payload must be <= {PayloadLen} bytes, got {payload.Length}.");
        }

        byte[] outBuf = new byte[_maxOutputReportLen];
        outBuf[0] = ReportId;

        payload.Span.CopyTo(outBuf.AsSpan(1));

        try
        {
            _stream!.Write(outBuf, 0, outBuf.Length);
            _log?.LogTrace("HID OUT wrote {len} bytes (payload {PayloadLen}).", outBuf.Length, payload.Length);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "HID OUT write failed.");
            throw;
        }

        return Task.CompletedTask;
    }

    // Reads one 64-byte payload from the internal FIFO buffer.
    // This does NOT call HidStream.Read(); the reader loop does that.
    public async Task<byte[]> Read(CancellationToken ct = default)
    {
        EnsureConnected();

        Channel<byte[]> ch = _rxChannel ?? throw new InvalidOperationException("RX channel not initialized. Call Connect() first.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(OverallReadTimeoutMs);

        try
        {
            while (await ch.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
            {
                if (ch.Reader.TryRead(out var packet))
                {
                    return packet;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Buffered read exceeded overall timeout of {OverallReadTimeoutMs} ms (no packet available).");
        }

        throw new InvalidOperationException("RX channel completed (device disconnected?).");
    }

    // Drops all currently buffered packets. Useful for resync between commands.

    public void FlushInputBuffer()
    {
        Channel<byte[]>? ch = _rxChannel;
        if (ch is null)
        {
            return;
        }

        int drained = 0;
        while (ch.Reader.TryRead(out _))
        {
            drained++;
        }

        if (drained > 0)
        {
            _log?.LogDebug("Flushed RX buffer: {Count} packets discarded.", drained);
        }

        while (ch.Reader.TryRead(out _))
        {

        }
    }

    public async Task<int> Drain(int expectedPackets, int perPacketTimeoutMs = 100, CancellationToken ct = default)
    {
        if (expectedPackets <= 0)
        {
            return 0;
        }

        Channel<byte[]>? ch = _rxChannel;
        if (ch is null)
        {
            return 0;
        }

        int drained = 0;

        for (int i = 0; i < expectedPackets; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (ch.Reader.TryRead(out _))
            {
                drained++;
                continue;
            }

            using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
            one.CancelAfter(perPacketTimeoutMs);

            try
            {
                if (!await ch.Reader.WaitToReadAsync(one.Token).ConfigureAwait(false))
                {
                    break;
                }

                if (ch.Reader.TryRead(out _))
                {
                    drained++;
                }
                else
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (drained > 0)
        {
            _log?.LogWarning("Drained {Drained}/{Expected} expected response packets (best-effort).", drained, expectedPackets);
        }

        return drained;
    }

    public void Dispose()
    {
        try
        {
            Disconnect(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Dispose->Disconnect failed.");
        }
    }

    private async Task ReaderLoop(CancellationToken ct)
    {
        HidStream? stream = _stream;
        Channel<byte[]>? ch = _rxChannel;

        if (stream is null || ch is null)
        {
            _log?.LogWarning("ReaderLoop started without stream/channel.");
            return;
        }

        byte[] inBuf = new byte[_maxInputReportLen];

        _log?.LogDebug("ReaderLoop started.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                int n = stream.Read(inBuf, 0, inBuf.Length);
                if (n <= 0)
                {
                    await Task.Yield();
                    continue;
                }

                int offset = (n >= PayloadLen + 1) ? 1 : 0;
                int available = n - offset;
                if (available <= 0)
                {
                    await Task.Yield();
                    continue;
                }

                byte[] payload = new byte[PayloadLen];
                int copyLen = Math.Min(PayloadLen, available);
                Array.Copy(inBuf, offset, payload, 0, copyLen);

                await ch.Writer.WriteAsync(payload, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "ReaderLoop fatal error. Completing channel.");
                try { ch.Writer.TryComplete(ex); }
                catch { }
                return;
            }
        }
        _log?.LogDebug("ReaderLoop stopped.");
        try { ch.Writer.TryComplete(); }
        catch { }
    }

    private void EnsureConnected()
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Transport is not connected. Call Connect() first.");
        }
    }
}