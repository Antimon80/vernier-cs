
namespace App.ViewModels;

/// <summary>
/// Provides device-specific UI workflows used by the generic measurement page.
///
/// The generic toolbar can call these methods without knowing whether the
/// current device is a spectrometer, a sensor interface or something else.
/// </summary>
public interface IMeasurementSettings
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

    event Action? AutoStopRequested;

    /// <summary>
    /// Requests that the device-specific operating mode dialog be presented.
    /// Raises the device view model's own "requested" event; the page that hosts
    /// the toolbar is the one that actually builds and shows the dialog.
    /// </summary>
    Task RequestOperatingModeDialog(CancellationToken ct = default);

    /// <summary>
    /// Requests that the device-specific acquisition mode dialog be presented.
    /// </summary>
    Task RequestAcquisitionModeDialog(CancellationToken ct = default);

    /// <summary>
    /// Requests that the device-specific "keep data point" dialog be presented.
    /// </summary>
    Task RequestKeepDataPointDialog(CancellationToken ct = default);

    /// <summary>
    /// Requests that the device-specific calibration dialog be presented.
    ///
    /// Returns null if the user cancels the dialog.
    /// </summary>
    Task<CalibrationDialogResult?> RequestCalibrationDialog(CancellationToken ct = default);

    /// <summary>
    /// Performs or opens a device-specific zero/tare workflow.
    /// </summary>
    Task SetToZero(CancellationToken ct = default);

    /// <summary>
    /// Scale the chart axes so that the currently displayed data fills the chart without being cut off.
    /// </summary>
    void Autoscale();
}

/// <summary>
/// Result returned by a device-specific calibration dialog.
/// </summary>
/// <param name="SkipWarmup">
/// null  = no explicit UI choice; backend/device decides.
/// true  = explicitly skip warmup.
/// false = explicitly wait for required warmup.
/// </param>
public sealed record CalibrationDialogResult(bool? SkipWarmup);