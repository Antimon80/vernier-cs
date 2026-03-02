namespace Backend.Discovery;

public sealed record DeviceDescriptor(
    ushort Vid,
    ushort Pid,
    string Name,
    string DevicePath
);