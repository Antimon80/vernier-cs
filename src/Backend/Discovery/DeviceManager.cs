using Backend.Devices;
using Backend.Devices.GoDirect;
using Backend.Transport;
using HidSharp;
using Microsoft.Extensions.Logging;

namespace Backend.Discovery;

/// <summary>
/// Discovers supported spectrometer devices on the local machine and manages
/// a single active connection at a time.
///
/// Responsibilities:
/// - Enumerate connected HID spectrometers using the catalog (VID + known PIDs).
/// - Create and wire up the transport + spectrometer instances.
/// - Ensure only one device is connected at once (disconnect previous on connect).
/// - Provide thread-safe connect/disconnect operations via a semaphore gate.
///
/// Notes:
/// - Device discovery is based on HID device paths (HidSharp).
/// - The manager keeps a cached device list that is refreshed on <see cref="ListDevices"/>.
/// - Consumers may either connect explicitly by index or require exactly one device.
/// </summary>
/// <remarks>
/// Creates a new device manager.
/// </remarks>
/// <param name="loggerFactory">
/// Optional logger factory. If provided, component instances (HidTransport, Spectrometer)
/// will receive their own typed loggers.
/// </param>
public sealed class DeviceManager(ILoggerFactory? loggerFactory = null) : IDeviceManager
{
    private readonly ILoggerFactory? _loggerFactory = loggerFactory;
    private readonly ILogger<DeviceManager>? _log = loggerFactory?.CreateLogger<DeviceManager>();

    /// <summary>
    /// Gate to serialize connect/disconnect operations and prevent concurrent state changes.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Cached last discovery result (device descriptors).
    /// </summary>
    private List<DeviceDescriptor> _devices = new();

    /// <summary>
    /// Currently connected device instance (also implements <see cref="IDisposable"/>).
    /// Null if no device is connected.
    /// </summary>
    public IDevice? CurrentDevice { get; private set; }

    /// <summary>
    /// Currently connected spectrometer facade (typed view of <see cref="CurrentDevice"/>).
    /// Null if no spectrometer is connected.
    /// </summary>
    public ISpectrometer? CurrentSpectrometer { get; private set; }

    /// <summary>
    /// Enumerates all supported spectrometer devices currently connected to the machine.
    /// The result is also cached internally for subsequent connect-by-index calls.
    /// </summary>
    /// <returns>A read-only list of device descriptors (VID/PID/name/device path).</returns>
    public IReadOnlyList<DeviceDescriptor> ListDevices()
    {
        ushort vid = DeviceCatalog.VernierVid;

        _devices = [.. DeviceCatalog.SpectrometerModels.SelectMany(kv =>
            DeviceList.Local.GetHidDevices(vid, kv.Key).Select(hid =>
            new DeviceDescriptor(Vid: vid, Pid: kv.Key, Name: kv.Value.Name, DevicePath: hid.DevicePath)))];

        return _devices;
    }

    /// <summary>
    /// Convenience helper: if exactly one supported device is connected, connect to it.
    /// Throws if zero or more than one device is found.
    /// </summary>
    public async Task ConnectSingleOrThrow(CancellationToken ct = default)
    {
        var devices = ListDevices();

        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No spectrometer device found.");
        }

        if (devices.Count > 1)
        {
            throw new InvalidOperationException($"Multiple spectrometers found ({devices.Count}). Select one explicitly.");
        }

        await Connect(0, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to a device by its index in the last discovery list.
    /// If no discovery has been performed yet, <see cref="ListDevices"/> is called implicitly.
    /// Any previously connected device will be disconnected and disposed first.
    /// </summary>
    /// <param name="deviceIndex">Index into the cached discovery list.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task Connect(int deviceIndex, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_devices.Count == 0)
            {
                ListDevices();
            }

            if ((uint)deviceIndex >= (uint)_devices.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(deviceIndex),
                $"deviceIndex={deviceIndex} out of range (0..{_devices.Count - 1}). Call ListDevices() first.");
            }

            // Ensure only one active device at a time.
            await DisconnectPrevious(ct).ConfigureAwait(false);

            var dd = _devices[deviceIndex];

            // Resolve the static model from the catalog for this PID.
            if (!DeviceCatalog.SpectrometerModels.TryGetValue(dd.Pid, out var model))
            {
                throw new InvalidOperationException($"PID=0x{dd.Pid:X4} not found in SpectrometerCatalog.Models.");
            }

            // Build transport and spectrometer instances and connect.
            ILogger<HidTransport>? tlog = _loggerFactory?.CreateLogger<HidTransport>();
            var transport = new HidTransport(dd.DevicePath, dd.Vid, dd.Pid, tlog);

            ILogger<Spectrometer>? slog = _loggerFactory?.CreateLogger<Spectrometer>();
            ISpectrometer spec = new Spectrometer(transport, model, slog);

            await spec.Connect(ct).ConfigureAwait(false);

            CurrentSpectrometer = spec;
            CurrentDevice = spec;

            _log?.LogInformation("Connected to {Name} (VID=0x{Vid:X4}, PID=0x{Pid:X4}).", dd.Name, dd.Vid, dd.Pid);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disconnects and disposes the currently connected device (if any).
    /// Thread-safe (serialized through the gate).
    /// </summary>
    public async Task Disconnect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DisconnectPrevious(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disconnects and disposes the previously connected device instance, if present.
    /// Resets <see cref="CurrentDevice"/> and <see cref="CurrentSpectrometer"/> to null.
    /// </summary>
    private async Task DisconnectPrevious(CancellationToken ct)
    {
        var dev = CurrentDevice;

        CurrentDevice = null;
        CurrentSpectrometer = null;

        if (dev is null)
        {
            return;
        }

        try
        {
            await dev.Disconnect(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Disconnect failed.");
        }

        try
        {
            dev.Dispose();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Dispose failed.");
        }
    }

    /// <summary>
    /// Disposes the manager by disconnecting any active device and releasing the semaphore gate.
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
        finally
        {
            _gate.Dispose();
        }
    }
}