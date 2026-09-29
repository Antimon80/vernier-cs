using App.ViewModels;

namespace App.Views;

/// <summary>
/// Provides the device-specific measurement UI and dialogs.
/// </summary>
public interface IDeviceUiProvider
{
    /// <summary>
    /// Creates the device-specific measurement view.
    /// </summary>
    View CreateMeasurementView();

    /// <summary>
    /// Shows the operating-mode selection dialog.
    /// </summary>
    Task ShowOperatingModeDialog(INavigation navigation, CancellationToken ct);

    /// <summary>
    /// Shows the acquisition-mode selection dialog.
    /// </summary>
    Task ShowAcquisitionModeDialog(INavigation navigation, CancellationToken ct);

    /// <summary>
    /// Shows the calibration dialog and returns the result.
    /// </summary>
    Task ShowCalibrationDialog(INavigation navigation, CancellationToken ct);

    /// <summary>
    /// Shows the dialog for capturing a measurement data point.
    /// </summary>
    Task ShowKeepDataPointDialog(INavigation navigation, CancellationToken ct);
}