namespace Backend.Device;

/// <summary>
/// High-level device classification.
/// </summary>
public enum DeviceFamily
{
    GoDirect,
    LabQuest
}

/// <summary>
/// Concrete GoDirect model
/// </summary>
public enum GoDirectDevice
{
    Unknown,
    SpectroVis,
    UvVis,
    Emission
}

/// <summary>
/// Runtime state of a device instance.
/// </summary>
public enum DeviceState
{
    Closed,
    Open,
    Initialized,
    Calibrated,
    Idle,
    Sampling,
    Faulted
}

/// <summary>
/// Stable identity of a physical device.
/// </summary>
public readonly record struct DeviceId(
    ushort VendorId,
    ushort ProductId,
    string SerialNumber
)
{
    public override string ToString()
    => $"USB:{VendorId:X4}:{ProductId:X4}:{SerialNumber}";
}