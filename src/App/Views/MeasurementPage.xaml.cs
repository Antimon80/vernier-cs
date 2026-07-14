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
        _spectroVisViewModel.OperatingModeDialogRequested += PresentOperatingModeDialog;
        _spectroVisViewModel.AcquisitionModeDialogRequested += PresentAcquisitionModeDialog;
        _spectroVisViewModel.CalibrationDialogRequested += PresentCalibrationDialog;
        _spectroVisViewModel.KeepDataPointDialogRequested += PresentKeepDataPointDialog;
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

    /// <summary>
    /// Handles <see cref="SpectroVisMeasurementViewModel.OperatingModeDialogRequested"/> by building
    /// and pushing the operating-mode dialog. This is the one place that actually puts the dialog on
    /// screen; the view model only requests it.
    /// </summary>
    private async Task PresentOperatingModeDialog(SpectroVisMeasurementViewModel measurementViewModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisOperatingModeViewModel dialogViewModel = new(measurementViewModel);
        SpectroVisOperatingModeDialog dialog = new(dialogViewModel);

        await Navigation.PushModalAsync(dialog);
    }

    /// <summary>
    /// Handles <see cref="SpectroVisMeasurementViewModel.AcquisitionModeDialogRequested"/>.
    /// </summary>
    private async Task PresentAcquisitionModeDialog(SpectroVisMeasurementViewModel measurementViewModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisAcquisitionModeViewModel dialogViewModel = new(measurementViewModel);
        SpectroVisAcquisitionModeDialog dialog = new(dialogViewModel);

        await Navigation.PushModalAsync(dialog);
    }

    /// <summary>
    /// Handles <see cref="SpectroVisMeasurementViewModel.CalibrationDialogRequested"/>.
    /// </summary>
    private async Task<CalibrationDialogResult?> PresentCalibrationDialog(SpectroVisMeasurementViewModel measurmentViewModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await DisplayAlertAsync(AppResources.Device_Calibrate, "not implemented yet", AppResources.Dialog_Ok);

        return null;
    }

    /// <summary>
    /// Handles <see cref="SpectroVisMeasurementViewModel.KeepDataPointDialogRequested"/>.
    /// </summary>
    private async Task PresentKeepDataPointDialog(SpectroVisMeasurementViewModel measurementViewModel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisKeepDataPointViewModel dialogViewModel = new(measurementViewModel);
        SpectroVisKeepDataPointDialog dialog = new(dialogViewModel);

        await Navigation.PushModalAsync(dialog);
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