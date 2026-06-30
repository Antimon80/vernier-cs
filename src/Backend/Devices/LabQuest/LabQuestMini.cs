using Backend.Transport;
using Microsoft.Extensions.Logging;

namespace Backend.Devices.LabQuest;

/// <summary>
/// Represents a Vernier LabQuest Mini USB interface.
///
/// Sensor discovery, channel configuration and measurement logic will be
/// added later. At this stage the class only owns the USB transport lifecycle.
/// </summary>
public sealed class LabQuestMini(ITransport transport, ILoggerFactory? loggerFactory = null) : IDevice
{
    /// <summary>
    /// Underlying USB transport used for communication.
    /// </summary>
    private readonly ITransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly ILogger<LabQuestMini>? _log = loggerFactory?.CreateLogger<LabQuestMini>();

    
    public ushort Vid => DeviceCatalog.VernierVid;
    public ushort Pid => DeviceCatalog.LabQuestMiniPid;

    /// <summary>
    /// Human-readable device name.
    /// </summary>
    public string DeviceName => "LabQuest Mini";

    /// <summary>
    /// Indicates wether the underlying transport is connected.
    /// </summary>
    public bool IsConnected => _transport.IsConnected;

    /// <summary>
    /// Opens the USB transport connection.
    /// </summary>
    public async Task Connect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (IsConnected)
        {
            return;
        }

        await _transport.Connect(ct).ConfigureAwait(false);

        _log?.LogInformation("LabQuest Mini connected.");
    }

    /// <summary>
    /// Closes the USB transport connection.
    /// </summary>
    public async Task Disconnect(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!IsConnected)
        {
            return;
        }

        await _transport.Disconnect(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnects the device and disposes the transport.
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
            _transport.Dispose();
        }
    }
}