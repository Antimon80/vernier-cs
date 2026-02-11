namespace Backend.Transport;

using HidSharp;

public sealed class HidTransport(string devicePath, ushort vid, ushort pid) : ITransport
{
    private readonly string _devicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));
    private readonly ushort _vid = vid;
    private readonly ushort _pid = pid;

    private HidStream? _stream;

    private const int PayloadLen = 64;
    private const byte ReportId = 0x00;

    private int _maxInputReportLen;
    private int _maxOutputReportLen;

    public bool IsConnected => _stream is not null;

    public Task Connect(CancellationToken ct = default)
    {
        if (IsConnected) { return Task.CompletedTask; }
        ct.ThrowIfCancellationRequested();

        var dev = DeviceList.Local.GetHidDevices(_vid, _pid).FirstOrDefault(d =>
        string.Equals(d.DevicePath, _devicePath, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException(
        $"HID device not found (VID=0x{_vid:X4}, PID=0x{_pid:X4}, path='{_devicePath}')");

        if (!dev.TryOpen(out var stream))
        {
            throw new InvalidOperationException("Failed to open HID device stream.");
        }

        _stream = stream;

        _maxInputReportLen = Math.Max(stream.Device.GetMaxInputReportLength(), PayloadLen + 1);
        _maxOutputReportLen = Math.Max(stream.Device.GetMaxOutputReportLength(), PayloadLen + 1);

        _stream.ReadTimeout = 50;
        _stream.WriteTimeout = 200;

        return Task.CompletedTask;
    }

    public Task Disconnect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var stream = _stream;
        _stream = null;

        stream?.Dispose();
        return Task.CompletedTask;
    }

    public Task Write(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        if (payload.Length > PayloadLen)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), $"Payload must be <= {PayloadLen} bytes, got {payload.Length}.");
        }

        var outBuf = new byte[_maxOutputReportLen];
        outBuf[0] = ReportId;

        payload.Span.CopyTo(outBuf.AsSpan(1));

        _stream!.Write(outBuf, 0, outBuf.Length);
        return Task.CompletedTask;
    }

    public async Task<byte[]> Read(CancellationToken ct = default)
    {
        EnsureConnected();

        var inBuf = new byte[_maxInputReportLen];

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                int n = _stream!.Read(inBuf, 0, inBuf.Length);
                if (n <= 0)
                {
                    await Task.Yield();
                    continue;
                }

                int offset = 0;
                int len = n;

                if (n == PayloadLen + 1 || inBuf[0] == ReportId)
                {
                    offset = 1;
                    len = Math.Max(0, n - 1);
                }

                var payload = new byte[PayloadLen];
                int copyLen = Math.Min(PayloadLen, len);
                Array.Copy(inBuf, offset, payload, 0, copyLen);

                return payload;
            }
            catch (TimeoutException)
            {
                continue;
            }
        }
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); }
        finally { _stream = null; }
    }

    private void EnsureConnected()
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Transport is not connected. Call Connect() first.");
        }
    }
}