using App.ViewModels;

namespace App.Views;

public interface IDeviceUiProvider
{
    View CreateMeasurementView();
    Task ShowOperatingModeDialog(INavigation navigation, CancellationToken ct);
    Task ShowAcquisitionModeDialog(INavigation navigation, CancellationToken ct);
    Task<CalibrationDialogResult?> ShowCalibrationDialog(INavigation navigation, CancellationToken ct);
    Task ShowKeepDataPointDialog(INavigation navigation, CancellationToken ct);
}