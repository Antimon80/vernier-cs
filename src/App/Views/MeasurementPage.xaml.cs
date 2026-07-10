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

    private async Task ShowOperatingModeDialog(
        SpectroVisMeasurementViewModel measurementViewModel,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisOperatingModeViewModel dialogViewModel = new(measurementViewModel);
        SpectroVisOperatingModeDialog dialog = new(dialogViewModel);

        await Navigation.PushModalAsync(dialog);
    }
}