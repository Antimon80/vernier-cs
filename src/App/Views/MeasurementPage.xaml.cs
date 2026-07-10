using App.Resources.Strings;
using App.ViewModels;
using App.ViewModels.GoDirect;
using App.Views.GoDirect;

namespace App.Views;

public partial class MeasurementPage : ContentPage
{
    private readonly MeasurementViewModel _viewModel;
    private SpectroVisMeasurementViewModel? _spectroVisViewModel;

    public MeasurementPage(MeasurementViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;

        _viewModel.DiagnosticsRequested += ShowDiagnosticsDialog;

        RegisterDeviceDialogs();
        LoadDeviceContent();
    }

    private void RegisterDeviceDialogs()
    {
        if (_viewModel.DeviceViewModel is not SpectroVisMeasurementViewModel spectroVisViewModel)
        {
            return;
        }

        _spectroVisViewModel = spectroVisViewModel;
        _spectroVisViewModel.OperatingModeDialogRequested += ShowOperatingModeDialog;
        _spectroVisViewModel.AcquisitionModeDialogRequested += ShowAcquisitionModeDialog;
        _spectroVisViewModel.CalibrationDialogRequested += ShowCalibrationDialog;
    }

    private void LoadDeviceContent()
    {
        switch (_viewModel.DeviceViewModel)
        {
            case SpectroVisMeasurementViewModel spectroVisViewModel:
                DeviceContentHost.Content = new SpectroVisMeasurementView
                {
                    BindingContext = spectroVisViewModel
                };
                break;

            default:
                throw new InvalidOperationException(
                    $"No measurement view is registered for view model type '{_viewModel.DeviceViewModel.GetType().Name}'.");
        }
    }

    private async Task ShowOperatingModeDialog(SpectroVisMeasurementViewModel measurementViewModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisOperatingModeViewModel dialogViewModel = new(measurementViewModel);
        SpectroVisOperatingModeDialog dialog = new(dialogViewModel);

        await Navigation.PushModalAsync(dialog);
    }

    private Task ShowAcquisitionModeDialog(SpectroVisMeasurementViewModel measurmentViewModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return DisplayAlertAsync(AppResources.Device_AcquisitionMode, "not implemented yet", AppResources.Dialog_Ok);
    }

    private async Task<CalibrationDialogResult?> ShowCalibrationDialog(SpectroVisMeasurementViewModel measurmentViewModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await DisplayAlertAsync(AppResources.Device_Calibrate, "not implemented yet", AppResources.Dialog_Ok);

        return null;
    }

    private async Task ShowDiagnosticsDialog(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _viewModel.RefreshDiagnostics();
        DiagnosticsDialog dialog = new(_viewModel.Diagnostics);

        await Navigation.PushModalAsync(dialog);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _viewModel.RefreshDeviceState();
        _viewModel.RefreshDiagnostics();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
    }
}