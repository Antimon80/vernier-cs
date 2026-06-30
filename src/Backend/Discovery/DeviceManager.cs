using Backend.Devices;
using Backend.Devices.GoDirect;
using Backend.Devices.LabQuest;
using Backend.Transport;
using HidSharp;
using LibUsbDotNet.LibUsb;
using Microsoft.Extensions.Logging;

namespace Backend.Discovery;

/// <summary>
/// Discovers supported Vernier HID devices and manages one active device
/// connection at a time.
///
/// The manager is responsible only for:
/// - HID device discovery,
/// - resolving a discovered device to a concrete device implementation,
/// - connecting and disconnecting that device,
/// - manager-level diagnostics.
///
/// Device-specific initialization and hardware diagnostics are the
/// responsibility of the concrete device implementation.
/// </summary>
public sealed class DeviceManager(ILoggerFactory? loggerFactory = null) : IDeviceManager
{
    private readonly ILoggerFactory? _loggerFactory = loggerFactory;
    private readonly ILogger<DeviceManager>? _log = loggerFactory?.CreateLogger<DeviceManager>();

    private const ushort LabQuestInterfacePid = 0x0008;

    /// <summary>
    /// Gate to serialize connect/disconnect operations and prevent concurrent state changes.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Protects the diagnostic collection.
    /// </summary>
    private readonly Lock _diagnosticLock = new();

    /// <summary>
    /// Cached last discovery result (device descriptors).
    /// </summary>
    private List<DeviceDescriptor> _devices = [];

    /// <summary>
    /// Manager-level diagnostics.
    /// </summary>
    private readonly List<DiagnosticEntry> _diagnostics = [];

    private bool _disposed;

    /// <summary>
    /// Currently connected device instance (also implements <see cref="IDisposable"/>).
    /// Null if no device is connected.
    /// </summary>
    public IDevice? CurrentDevice { get; private set; }

    /// <summary>
    /// Typed view of <see cref="CurrentDevice"/> if the current device
    /// is a spectrometer.
    /// </summary>
    public ISpectrometer? CurrentSpectrometer => CurrentDevice as ISpectrometer;

    /// <summary>
    /// Snapshot of all manager-level diagnostics.
    /// </summary>
    public IReadOnlyList<DiagnosticEntry> Diagnostics
    {
        get
        {
            lock (_diagnosticLock)
            {
                return [.. _diagnostics];
            }
        }
    }

    /// <summary>
    /// Enumerates all currently connected Vernier HID devices known to the catalog.
    ///
    /// Spectrometers are identified directly by their PID. The LabQuest interface
    /// is discovered separately under PID 0x0008, although its attached sensor
    /// cannot yet be identified by this manager.
    /// </summary>
    public IReadOnlyList<DeviceDescriptor> ListDevices()
    {
        ThrowIfDisposed();
        DiagnosticEntry.ClearDiagnostics(_diagnostics, DiagnosticCategory.Connection);
        List<DeviceDescriptor> discovered = [];

        foreach ((ushort pid, SpectrometerModel model) in DeviceCatalog.SpectrometerModels)
        {
            try
            {
                IEnumerable<HidDevice> hidDevices = DeviceList.Local.GetHidDevices(DeviceCatalog.VernierVid, pid);

                foreach (HidDevice hidDevice in hidDevices)
                {
                    discovered.Add(
                        new DeviceDescriptor(
                            Vid: DeviceCatalog.VernierVid,
                            Pid: pid,
                            Name: model.Name,
                            DevicePath: hidDevice.DevicePath,
                            TransportType: TransportType.Hid
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                DiagnosticEntry.AddDiagnostic(_diagnostics,
                    code: "DEVICE.DISCOVERY.SPECTROMETER_ENUMERATION_FAILED",
                    severity: DiagnosticSeverity.Warning,
                    category: DiagnosticCategory.Discovery,
                    message: $"Spectrometer of type '{model.Name}' could not be enumerated.",
                    technicalDetails: $"VID=0x{DeviceCatalog.VernierVid:X4}; " +
                        $"PID=0x{pid:X4}; " + $"Exception={ex}",
                    operation: nameof(ListDevices),
                    source: nameof(DeviceManager)
                );

                _log?.LogWarning(ex, "Enumeration failed for {Name} " +
                    "(VID=0x{Vid:X4}, PID=0x{Pid:X4}).", model.Name, DeviceCatalog.VernierVid, pid);
            }
        }

        DiscoverLabQuestInterfaces(discovered);
        _devices = discovered;

        _log?.LogInformation("Device discovery completed. Found {Count} supported device(s).", _devices.Count);

        return [.. _devices];
    }

    /// <summary>
    /// Convenience helper: if exactly one supported device is connected, connect to it.
    /// Throws if zero or more than one device is found.
    /// </summary>
    public async Task ConnectSingle(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<DeviceDescriptor> devices = ListDevices();

        const string message = "No supported Vernier device was found";

        if (devices.Count == 0)
        {
            DiagnosticEntry.AddDiagnostic(_diagnostics,
                code: "DEVICE.CONNECTION.NO_DEVICE_FOUND",
                severity: DiagnosticSeverity.Error,
                category: DiagnosticCategory.Connection,
                message: message,
                operation: nameof(ConnectSingle),
                source: nameof(DeviceManager)
            );

            throw new InvalidOperationException(message);
        }

        if (devices.Count > 1)
        {
            string message2 = $"Multiple supported Vernier devices wer found " +
                $"({devices.Count}). Select one explicitily.";
            string details = string.Join(Environment.NewLine, devices.Select((device, index) =>
                $"[{index}] {device.Name}; " + $"VID=0x{device.Vid:X4}; " +
                $"PID=0x{device.Pid:X4}; " + $"Path={device.DevicePath}"));

            DiagnosticEntry.AddDiagnostic(_diagnostics,
                code: "DEVICE.CONNECTION.MULTIPLE_DEVICES_FOUND",
                severity: DiagnosticSeverity.Error,
                category: DiagnosticCategory.Connection,
                message: message2,
                technicalDetails: details,
                operation: nameof(ConnectSingle),
                source: nameof(DeviceManager)
            );

            throw new InvalidOperationException(message2);
        }

        await Connect(0, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects the device at the specified index in the current discovery list.
    ///
    /// Any previously connected device is disconnected and disposed first.
    /// A concrete device instance is created according to VID and PID.
    ///
    /// Device-specific initialization errors do not prevent the connected device
    /// from becoming <see cref="CurrentDevice"/> if its Connect operation returns
    /// normally.
    /// </summary>
    public async Task Connect(int deviceIndex, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DiagnosticEntry.ClearDiagnostics(_diagnostics, DiagnosticCategory.Connection);

            if (_devices.Count == 0)
            {
                ListDevices();
            }

            if ((uint)deviceIndex >= (uint)_devices.Count)
            {
                string message = _devices.Count == 0 ? "No discovered device is available."
                    : $"Device index {deviceIndex} is outside the valid range " + $"0..{_devices.Count - 1}.";

                DiagnosticEntry.AddDiagnostic(_diagnostics,
                    code: "DEVICE.CONNECTION.INVALID_DEVICE_INDEX",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Connection,
                    message: message,
                    technicalDetails: $"DeviceIndex={deviceIndex}; " + $"DeviceCount={_devices.Count}",
                    operation: nameof(Connect),
                    source: nameof(DeviceManager)
                );

                throw new ArgumentOutOfRangeException(nameof(deviceIndex), deviceIndex, message);
            }

            DeviceDescriptor descriptor = _devices[deviceIndex];

            await DisconnectCurrentDevice(createDiagnostics: true, CancellationToken.None).ConfigureAwait(false);

            IDevice? candidate = null;

            try
            {
                candidate = CreateDevice(descriptor);
                await candidate.Connect(ct).ConfigureAwait(false);
                CurrentDevice = candidate;
                candidate = null;

                _log?.LogInformation("Connected to {Name} " + "(VID=0x{Vid:X4}, PID=0x{Pid:X4}).",
                    descriptor.Name, descriptor.Vid, descriptor.Pid);
            }
            catch (OperationCanceledException)
            {
                await CleanupCandidate(candidate).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await CleanupCandidate(candidate).ConfigureAwait(false);

                DiagnosticEntry.AddDiagnostic(_diagnostics,
                    code: "DEVICE.CONNECTION.FAILED",
                    severity: DiagnosticSeverity.Error,
                    category: DiagnosticCategory.Connection,
                    message: $"The device '{descriptor.Name}' could not be connected.",
                    technicalDetails: $"VID=0x{descriptor.Vid:X4}; " + $"PID=0x{descriptor.Pid:X4}; " +
                        $"Path={descriptor.DevicePath}; " + $"Exception={ex}",
                    operation: nameof(Connect),
                    source: nameof(DeviceManager)
                );

                _log?.LogError(ex, "Connection failed for {Name} " + "(VID=0x{Vid:X4}, PID=0x{Pid:X4}).",
                    descriptor.Name, descriptor.Vid, descriptor.Pid);

                throw;
            }
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
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DiagnosticEntry.ClearDiagnostics(_diagnostics, DiagnosticCategory.Connection);

            await DisconnectCurrentDevice(createDiagnostics: true, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Creates the concrete device implementation for a discovered descriptor.
    ///
    /// New device types can be added here without changing the discovery,
    /// connection or cleanup logic.
    /// </summary>
    private IDevice CreateDevice(DeviceDescriptor descriptor)
    {
        if (descriptor.Vid != DeviceCatalog.VernierVid)
        {
            throw new NotSupportedException(
                $"Unsupported vendor ID 0x{descriptor.Vid:X4}.");
        }

        if (descriptor.TransportType == TransportType.Hid)
        {
            ILogger<HidTransport>? transportLogger =
                _loggerFactory?.CreateLogger<HidTransport>();

            HidTransport transport = new(
                descriptor.DevicePath,
                descriptor.Vid,
                descriptor.Pid,
                transportLogger);

            if (DeviceCatalog.SpectrometerModels.TryGetValue(
                    descriptor.Pid,
                    out SpectrometerModel? model))
            {
                return new Spectrometer(
                    transport,
                    model,
                    _loggerFactory);
            }

            transport.Dispose();

            throw new NotSupportedException(
                $"No HID device implementation is registered for " +
                $"VID=0x{descriptor.Vid:X4}, PID=0x{descriptor.Pid:X4}.");
        }

        if (descriptor.TransportType == TransportType.UsbBulk)
        {
            if (descriptor.Pid != DeviceCatalog.LabQuestMiniPid)
            {
                throw new NotSupportedException(
                    $"No USB bulk device implementation is registered for " +
                    $"VID=0x{descriptor.Vid:X4}, PID=0x{descriptor.Pid:X4}.");
            }

            if (descriptor.UsbBusNumber is null ||
                descriptor.UsbDeviceAddress is null ||
                descriptor.UsbPortNumbers is null)
            {
                throw new InvalidOperationException(
                    "The USB bulk device descriptor contains no USB location information.");
            }

            ILogger<UsbBulkTransport>? transportLogger =
                _loggerFactory?.CreateLogger<UsbBulkTransport>();

            UsbBulkTransport transport = new(
                descriptor.Vid,
                descriptor.Pid,
                descriptor.UsbBusNumber.Value,
                descriptor.UsbDeviceAddress.Value,
                descriptor.UsbPortNumbers,
                transportLogger);

            return new LabQuestMini(
                transport,
                _loggerFactory);
        }

        throw new ArgumentOutOfRangeException(
            nameof(descriptor),
            descriptor.TransportType,
            "Unknown transport kind.");
    }

    /// <summary>
    /// Adds connected LabQuest HID interfaces to the discovery result.
    ///
    /// PID 0x0008 identifies the LabQuest interface itself, not necessarily
    /// the sensor attached to that interface.
    /// </summary>
    private void DiscoverLabQuestInterfaces(ICollection<DeviceDescriptor> discovered)
    {
        try
        {
            using UsbContext context = new();

            foreach (UsbDevice device in context.List())
            {
                if (device.VendorId != DeviceCatalog.VernierVid || device.ProductId != DeviceCatalog.LabQuestMiniPid)
                {
                    continue;
                }

                byte[] portNumbers = [.. device.PortNumbers];
                string locationKey = UsbBulkTransport.CreateLocationKey(device.BusNumber, device.Address, portNumbers);
                discovered.Add(new DeviceDescriptor(
                    Vid: device.VendorId,
                    Pid: device.ProductId,
                    Name: "LabQuest Mini",
                    DevicePath: locationKey,
                    TransportType: TransportType.UsbBulk,
                    UsbBusNumber: device.BusNumber,
                    UsbDeviceAddress: device.Address,
                    UsbPortNumbers: portNumbers
                ));

                _log?.LogInformation("Found LabQuest Mini. Bus={Bus}, Address={Address}, Ports={Ports}.",
                    device.BusNumber, device.Address, string.Join(".", portNumbers));
            }
        }
        catch (Exception ex)
        {
            DiagnosticEntry.AddDiagnostic(_diagnostics,
                code: "DEVICE.DISCOVERY.LABQUEST_ENUMERATION_FAILED",
                severity: DiagnosticSeverity.Warning,
                category: DiagnosticCategory.Discovery,
                message: "LabQuest interfaces could not be enumerated.",
                technicalDetails: $"VID=0x{DeviceCatalog.VernierVid:X4}; " +
                    $"PID=0x{LabQuestInterfacePid:X4}; " + $"Exception={ex}",
                operation: nameof(ListDevices),
                source: nameof(DeviceManager),
                exception: ex,
                logger: _log
            );

            _log?.LogWarning(ex, "LabQuest Mini enumeration failed.");
        }
    }

    /// <summary>
    /// Disconnects and disposes the current device.
    /// </summary>
    private async Task DisconnectCurrentDevice(bool createDiagnostics, CancellationToken ct)
    {
        IDevice? device = CurrentDevice;
        CurrentDevice = null;

        if (device is null)
        {
            return;
        }

        OperationCanceledException? cancellation = null;

        try
        {
            await device.Disconnect(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            cancellation = ex;
        }
        catch (Exception ex)
        {
            if (createDiagnostics)
            {
                DiagnosticEntry.AddDiagnostic(_diagnostics,
                    code: "DEVICE.CONNECTION.DISCONNECT_FAILED",
                    severity: DiagnosticSeverity.Warning,
                    category: DiagnosticCategory.Connection,
                    message: $"The device '{device.DeviceName}' could not be disconnected cleanly.",
                    technicalDetails: $"Exception={ex}",
                    operation: nameof(Disconnect),
                    source: nameof(DeviceManager)
                );
            }

            _log?.LogWarning(ex, "Disconnect failed for {DeviceName}.", device.DeviceName);
        }

        try
        {
            device.Dispose();
        }
        catch (Exception ex)
        {
            if (createDiagnostics)
            {
                DiagnosticEntry.AddDiagnostic(_diagnostics,
                    code: "DEVICE.CONNECTION.DISPOSE_FAILED",
                    severity: DiagnosticSeverity.Warning,
                    category: DiagnosticCategory.Connection,
                    message: $"Resources belonging to '{device.DeviceName}' could not be released cleanly.",
                    technicalDetails: $"Exception={ex}",
                    operation: nameof(IDisposable.Dispose),
                    source: nameof(DeviceManager)
                );
            }

            _log?.LogWarning(ex, "Dispose failed for {DeviceName}.", device.DeviceName);
        }

        if (cancellation is not null)
        {
            throw cancellation;
        }

    }

    /// <summary>
    /// Cleans up a device whose connection attempt did not complete.
    /// </summary>
    private async Task CleanupCandidate(IDevice? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        try
        {
            await candidate.Disconnect(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Disconnect failed while cleaning up an unsuccesful device connection attempt.");
        }

        try
        {
            candidate.Dispose();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Dispose failed while cleaning up an unsuccesful device connection attempt.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Disposes the manager by disconnecting any active device and releasing the semaphore gate.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Wait();

        try
        {
            DisconnectCurrentDevice(createDiagnostics: false, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Device Manager disposal failed.");
        }
        finally
        {
            _disposed = true;
            _gate.Release();
            _gate.Dispose();
        }
    }
}