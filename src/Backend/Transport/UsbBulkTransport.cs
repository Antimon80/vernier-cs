using System.IO.Pipes;
using HidSharp.Reports.Input;
using LibUsbDotNet;
using LibUsbDotNet.Info;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using Microsoft.Extensions.Logging;

namespace Backend.Transport;

/// <summary>
/// Cross-platform USB bulk transport based on LibUsbDotNet.
///
/// The transport locates a USB device by VID, PID and physical USB location,
/// opens it, claims the interface containing a bulk IN and bulk OUT endpoint,
/// and exposes those endpoints through <see cref="ITransport"/>.
/// </summary>
/// <remarks>
/// Creates a transport for one specific USB device.
/// </remarks>
public sealed class UsbBulkTransport(ushort vid, ushort pid, byte busNumber, byte deviceAddress,
    IReadOnlyList<byte> portNumbers, ILogger<UsbBulkTransport>? log = null) : ITransport
{
    // Default transfer settings used by all endpoint operations.
    private const int DefaultReadBufferSize = 4096;
    private const int DefaultReadTimeotMs = 2000;
    private const int DefaultWriteTimeoutMs = 2000;

    // Device identity and physical USB location.
    private readonly ushort _vid = vid;
    private readonly ushort _pid = pid;
    private readonly byte _busNumber = busNumber;
    private readonly byte _deviceAddress = deviceAddress;
    private readonly IReadOnlyList<byte> _portNumbers = portNumbers?.ToArray() ?? throw new ArgumentNullException(nameof(portNumbers));
    private readonly ILogger<UsbBulkTransport>? _log = log;

    // Resources owned while the transport is connected.
    private UsbContext? _context;
    private UsbDevice? _device;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;

    // Interface that must be released during disconnect.
    private int? _claimedInterface;

    /// <summary>
    /// Indicates whether the device and both bulk endpoints are available.
    /// </summary>
    public bool IsConnected => _device is { IsOpen: true } && _reader is not null && _writer is not null;

    /// <summary>
    /// Opens the device, claims its bulk interface and creates the endpoints.
    /// </summary>
    public Task Connect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (IsConnected)
        {
            return Task.CompletedTask;
        }

        UsbContext? context = null;
        UsbDevice? device = null;

        try
        {
            context = new UsbContext();

            // Locate the exact physical device, not only a matching VID/PID pair.
            foreach (UsbDevice candidate in context.List().Cast<UsbDevice>())
            {
                if (IsMatchingDevice(candidate))
                {
                    device = candidate;
                    break;
                }
            }

            if (device is null)
            {
                throw new InvalidOperationException($"USB device not found (VID=0x{_vid:X4}, PID=0x{_pid:X4}, " +
                    $"bus={_busNumber}, address={_deviceAddress}, ports={FormatPortPath(_portNumbers)}).");
            }

            device.Open();

            UsbEndpointInfo? bulkInEndpoint = null;
            UsbEndpointInfo? bulkOutEndpoint = null;
            int? interfaceNumber = null;

            // Find an interface that provides both required bulk endpoints.
            foreach (UsbConfigInfo config in device.Configs)
            {
                foreach (UsbInterfaceInfo usbInterface in config.Interfaces)
                {
                    UsbEndpointInfo? candidateIn = usbInterface.Endpoints.FirstOrDefault(endpoint =>
                    {
                        EndpointType endpointType = (EndpointType)(endpoint.Attributes & 0x03);
                        bool isInput = (endpoint.EndpointAddress & 0x80) != 0;

                        return endpointType == EndpointType.Bulk && isInput;
                    });

                    UsbEndpointInfo? candidateOut = usbInterface.Endpoints.FirstOrDefault(endpoint =>
                    {
                        EndpointType endpointType = (EndpointType)(endpoint.Attributes & 0x03);
                        bool isOutput = (endpoint.EndpointAddress & 0x80) == 0;

                        return endpointType == EndpointType.Bulk && isOutput;
                    });

                    if (candidateIn is null || candidateOut is null)
                    {
                        continue;
                    }

                    bulkInEndpoint = candidateIn;
                    bulkOutEndpoint = candidateOut;
                    interfaceNumber = usbInterface.Number;

                    break;
                }

                if (interfaceNumber is not null)
                {
                    break;
                }
            }

            if (interfaceNumber is null || bulkInEndpoint is null || bulkOutEndpoint is null)
            {
                throw new InvalidOperationException("No USB interface containing both a bulk IN and bulk OUT endpoint was found.");
            }

            // Detach an active kernel driver automatically where supported.
            device.SetAutoDetachKernelDriver(true);

            if (!device.ClaimInterface(interfaceNumber.Value))
            {
                throw new InvalidOperationException($"USB interface {interfaceNumber.Value} could not be claimed.");
            }

            byte bulkIndAddress = bulkInEndpoint.EndpointAddress;
            byte bulkOutAddress = bulkOutEndpoint.EndpointAddress;

            UsbEndpointReader reader = device.OpenEndpointReader(
                (ReadEndpointID)bulkIndAddress, DefaultReadBufferSize, EndpointType.Bulk);
            UsbEndpointWriter writer = device.OpenEndpointWriter(
                (WriteEndpointID)bulkOutAddress, EndpointType.Bulk);

            // Transfer ownership to the transport only after setup succeeded.
            _context = context;
            _device = device;
            _reader = reader;
            _writer = writer;
            _claimedInterface = interfaceNumber;

            context = null;
            device = null;

            _log?.LogInformation("USB bulk transport connected. VID=0x{Vid:X4}, PID=0x{Pid:X4}, " +
                "bus={Bus}, address={Address}, interface={Interface}, bulkIn=0x{BulkIn:X2}, bulkOut={BulkOut:X2}.",
                _vid, _pid, _busNumber, _deviceAddress, interfaceNumber.Value, bulkIndAddress, bulkOutAddress);

            return Task.CompletedTask;
        }
        catch
        {
            // Clean up resources that were created before the connection failed.
            try
            {
                if (device is { IsOpen: true })
                {
                    device.Close();
                }
            }
            catch
            {

            }

            device?.Dispose();
            context?.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Releases the interface and disposes all USB resources.
    /// </summary>
    public Task Disconnect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        UsbDevice? device = _device;
        UsbContext? context = _context;
        int? claimedInterface = _claimedInterface;

        // Clear the public connection state before releasing native resources.
        _reader = null;
        _writer = null;
        _device = null;
        _context = null;
        _claimedInterface = null;

        if (device is not null)
        {
            if (claimedInterface is not null)
            {
                try
                {
                    device.ReleaseInterface(claimedInterface.Value);
                }
                catch (Exception ex)
                {
                    _log?.LogWarning(ex, "Releasing USB interface {Interface} failed.", claimedInterface.Value);
                }
            }

            try
            {
                if (device.IsOpen)
                {
                    device.Close();
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Closing USB device failed.");
            }

            try
            {
                device.Dispose();
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Disposing USB device failed.");
            }
        }

        try
        {
            context?.Dispose();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Disposing USB context failed.");
        }

        _log?.LogInformation("USB bulk transport disconnected.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes one complete payload to the bulk OUT endpoint.
    /// </summary>
    public Task Write(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        byte[] buffer = payload.ToArray();
        Error error = _writer!.Write(buffer, 0, buffer.Length, DefaultWriteTimeoutMs, out int transferred);

        if (error != Error.Success)
        {
            throw new IOException($"USB bulk write failed with {error}. Transferred {transferred}/{buffer.Length} bytes.");
        }

        if (transferred != buffer.Length)
        {
            throw new IOException($"Incomplete USB bulk write. Transferred {transferred}/{buffer.Length} bytes.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads one packet from the bulk IN endpoint.
    /// </summary>
    public Task<byte[]> Read(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureConnected();

        byte[] buffer = new byte[DefaultReadBufferSize];
        Error error = _reader!.Read(buffer, 0, buffer.Length, DefaultReadTimeotMs, out int transferred);

        if (error == Error.Timeout)
        {
            throw new TimeoutException($"USB bulk read exceeded the timeout of {DefaultReadTimeotMs} ms.");
        }

        if (error != Error.Success)
        {
            throw new IOException($"USB bulk read failed with {error}. Transferred {transferred} bytes.");
        }

        return Task.FromResult(buffer[..transferred]);
    }

    /// <summary>
    /// Flushes pending input data when supported by the transport.
    /// </summary>
    public void FlushInputBuffer()
    {
        if (!IsConnected)
        {
            return;
        }
    }

    /// <summary>
    /// Reads and discards up to the requested number of packets.
    /// </summary>
    public async Task<int> Drain(int expectedPackets, int perPacketTimeoutMs = 100, CancellationToken ct = default)
    {
        if (expectedPackets <= 0 || !IsConnected)
        {
            return 0;
        }

        int drained = 0;

        for (int i = 0; i < expectedPackets; i++)
        {
            ct.ThrowIfCancellationRequested();

            byte[] buffer = new byte[DefaultReadBufferSize];
            Error error = _reader!.Read(buffer, 0, buffer.Length, perPacketTimeoutMs, out int transferred);

            if (error == Error.Timeout)
            {
                break;
            }

            if (error != Error.Success)
            {
                _log?.LogWarning("USB drain stopped with {Error} after {Count} packet(s).", error, drained);

                break;
            }

            if (transferred <= 0)
            {
                break;
            }

            drained++;
            await Task.Yield();
        }

        return drained;
    }

    /// <summary>
    /// Disconnects the transport and releases its resources.
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

    /// <summary>
    /// Checks VID, PID and physical USB location.
    /// </summary>
    private bool IsMatchingDevice(UsbDevice device)
    {
        if (device.VendorId != _vid || device.ProductId != _pid)
        {
            return false;
        }

        if (device.BusNumber != _busNumber)
        {
            return false;
        }

        if (_portNumbers.Count > 0)
        {
            return device.PortNumbers.SequenceEqual(_portNumbers);
        }

        return device.Address == _deviceAddress;
    }

    /// <summary>
    /// Throws when an endpoint operation is attempted while disconnected.
    /// </summary>
    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("USB bulk transport is not connected. Call Connect() first.");
        }
    }

    /// <summary>
    /// Creates a stable key for a physical USB location.
    /// </summary>
    internal static string CreateLocationKey(byte busNumber, byte address, IReadOnlyList<byte> portNumbers)
    {
        return $"usb:{busNumber}:{address}:{FormatPortPath(portNumbers)}";
    }

    /// <summary>
    /// Formats a USB port chain such as 1.3.2.
    /// </summary>
    private static string FormatPortPath(IEnumerable<byte> portNumbers)
    {
        string path = string.Join(".", portNumbers);
        return string.IsNullOrEmpty(path) ? "-" : path;
    }
}