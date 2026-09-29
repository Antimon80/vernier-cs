
namespace App.ViewModels;

/// <summary>
/// Provides device-specific UI workflows used by the generic measurement page.
///
/// The generic toolbar can call these methods without knowing whether the
/// current device is a spectrometer, a sensor interface or something else.
/// </summary>
public interface IDeviceMeasurementViewModel
{
    /// <summary>
    /// True if the current device exposes a selectable operating mode.
    /// Example: spectrometer absorbance/transmission/intensity/raw counts.
    /// </summary>
    bool HasOperatingModeSelection { get; }

    /// <summary>
    /// True if the current device exposes a separate zero/tare action.
    /// Example: force sensor zeroing. Not used by the current toolbar yet.
    /// </summary>
    bool HasZeroCommand { get; }

    bool CanKeepDataPoint { get; }

    bool CanStartMeasurement {get;}

    event Action? AutoStopRequested;

    /// <summary>
    /// Performs or opens a device-specific zero/tare workflow.
    /// </summary>
    Task SetToZero(CancellationToken ct = default);

    void OnMeasurementStopped();

    void Refresh();
}