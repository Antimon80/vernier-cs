using App.ViewModels;
using App.ViewModels.GoDirect;
using App.Views.GoDirect;

namespace App.Views;

public partial class MeasurementPage : ContentPage
{
    private readonly MeasurementViewModel _viewModel;
    private readonly IDeviceUiProvider _uiProvider;

    public MeasurementPage(MeasurementViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;

        _viewModel.DiagnosticsRequested += ShowDiagnosticsDialog;

        _uiProvider = DeviceUiProviderFactory.Create(_viewModel.DeviceViewModel);

        RegisterDeviceDialogs();
        LoadDeviceContent();
    }

    /// <summary>
    /// Registers the device-specific dialogs with the generic measurement view model.
    /// </summary>
    private void RegisterDeviceDialogs()
    {
        _viewModel.OperatingModeDialogRequested += ct => _uiProvider.ShowOperatingModeDialog(Navigation, ct);
        _viewModel.AcquisitionModeDialogRequested += ct => _uiProvider.ShowAcquisitionModeDialog(Navigation, ct);
        _viewModel.CalibrationDialogRequested += ct => _uiProvider.ShowCalibrationDialog(Navigation, ct);
        _viewModel.KeepDataPointDialogRequested += ct => _uiProvider.ShowKeepDataPointDialog(Navigation, ct);
    }

    /// <summary>
    /// Loads the device-specific measurement view into the page.
    /// </summary>
    private void LoadDeviceContent()
    {
        DeviceContentHost.Content = _uiProvider.CreateMeasurementView();
    }

    /// <summary>
    /// Refreshes the diagnostics and displays them in a modal dialog.
    /// </summary>
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