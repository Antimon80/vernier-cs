namespace Backend.Transport;

using System.Diagnostics;
using System.Threading.Channels;
using HidSharp;

public sealed class HidTransport : ITransport
{
    private readonly string _devicePath;
    private readonly ushort _vid;
    private readonly ushort _pid;

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
            finally { rxCts.Dispose(); }
        }

        Task? rxTask = _rxTask;
        _rxTask = null;

        if (rxTask is not null)
        {
            try { await rxTask.ConfigureAwait(false); }
            catch { }
        }

        Channel<byte[]>? ch = _rxChannel;
        _rxChannel = null;

        if (ch is not null)
        {
            ch.Writer.TryComplete();
        }

        HidStream? stream = _stream;
        _stream = null;

        stream?.Dispose();
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

        _stream!.Write(outBuf, 0, outBuf.Length);
        return Task.CompletedTask;
    }

    // Reads one 64-byte payload from the internal FIFO buffer.
    // This does NOT call HidStream.Read(); the reader loop does that.
    public async Task<byte[]> Read(CancellationToken ct = default)
    {
        EnsureConnected();

        Channel<byte[]>? ch = _rxChannel;
        if (ch is null)
        {
            throw new InvalidOperationException("RX channel not initialized. Call Connect() first.");
        }

        Stopwatch sw = Stopwatch.StartNew();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (sw.ElapsedMilliseconds > OverallReadTimeoutMs)
            {
                throw new TimeoutException(
                    $"Buffered read exceeded overall timeout of {OverallReadTimeoutMs} ms (no packet available).");
            }


            if (ch.Reader.TryRead(out byte[]? packet))
            {
                return packet;
            }

            ValueTask<bool> waitTask = ch.Reader.WaitToReadAsync(ct);
            bool canRead = await waitTask.ConfigureAwait(false);

            if (!canRead)
            {
                throw new InvalidOperationException("RX channel completed (device disconnected?).");
            }
        }
    }

    // Drops all currently buffered packets. Useful for resync between commands.

    public void FlushInputBuffer()
    {
        Channel<byte[]>? ch = _rxChannel;
        if (ch is null)
        {
            return;
        }

        while (ch.Reader.TryRead(out _))
        {

        }
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); }
        finally { _stream = null; }
    }

    private async Task ReaderLoop(CancellationToken ct)
    {
        HidStream? stream = _stream;
        Channel<byte[]>? ch = _rxChannel;

        if (stream is null || ch is null)
        {
            return;
        }

        byte[] inBuf = new byte[_maxInputReportLen];

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

                int offset;
                int len;

                if (n == PayloadLen + 1)
                {
                    offset = 1;
                    len = n - 1;
                }
                else
                {
                    offset = 0;
                    len = n;
                }
                if (len <= 0)
                {
                    await Task.Yield();
                    continue;
                }

                byte[] payload = new byte[PayloadLen];
                int copyLen = Math.Min(PayloadLen, len);
                Array.Copy(inBuf, offset, payload, 0, copyLen);

                await ch.Writer.WriteAsync(payload, ct).ConfigureAwait(false);
            }
            catch(TimeoutException)
            {
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch(Exception ex)
            {
                try { ch.Writer.TryComplete(ex); }
                catch { }
                break;
            }
        }
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