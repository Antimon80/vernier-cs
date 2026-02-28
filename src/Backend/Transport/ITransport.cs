namespace Backend.Transport;

public interface ITransport : IDisposable
{
    bool IsConnected{get;}

    Task Connect(CancellationToken ct = default);
    Task Disconnect(CancellationToken ct = default);

    Task Write(ReadOnlyMemory<byte> payload, CancellationToken ct = default);
    Task<byte[]> Read(CancellationToken ct = default);

    void FlushInputBuffer();
    Task<int> Drain(int expectedPackets, int perPacketTimeoutMs = 100, CancellationToken ct = default);
}