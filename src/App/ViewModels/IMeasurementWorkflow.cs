
namespace App.ViewModels;

/// <summary>
/// Provides device-specific UI workflows used by the generic measurement page.
///
/// The generic toolbar can call these methods without knowing whether the
/// current device is a spectrometer, a sensor interface or something else.
/// </summary>
public interface IMeasurementWorkflow
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

    /// <summary>
    /// Opens the device-specific operating mode dialog.
    /// </summary>
    Task OpenOperatingMode(CancellationToken ct = default);

    /// <summary>
    /// Opens the device-specific acquisition mode dialog.
    /// </summary>
    Task OpenAcquisitionMode(CancellationToken ct = default);

    /// <summary>
    /// Opens the device-specific calibration dialog.
    ///
    /// Returns null if the user cancels the dialog.
    /// </summary>
    Task<CalibrationDialogResult?> ShowCalibrationDialog(CancellationToken ct = default);

    /// <summary>
    /// Performs or opens a device-specific zero/tare workflow.
    /// </summary>
    Task SetToZero(CancellationToken ct = default);
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