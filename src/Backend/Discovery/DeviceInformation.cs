namespace Backend.Discovery;

public enum TransportType
{
    Hid,
    UsbBulk
}

public sealed record DeviceDescriptor(
    ushort Vid,
    ushort Pid,
    string Name,
    string DevicePath,
    TransportType TransportType,
    byte? UsbBusNumber = null,
    byte? UsbDeviceAddress = null,
    IReadOnlyList<byte>? UsbPortNumbers = null
);