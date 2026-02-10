namespace Backend.Devices;

public interface IDevice : IDisposable
{
    ushort Vid {get;}
    ushort Pid {get;}

    string DeviceName {get;}
    bool IsConnected {get;}

    Task Connect(CancellationToken ct = default);
    Task Disconnect(CancellationToken ct = default);
}