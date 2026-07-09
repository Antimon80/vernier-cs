using App.ViewModels;
using App.ViewModels.GoDirect;
using App.Views.GoDirect;

namespace App.Views;

public partial class MeasurementPage : ContentPage
{
    private readonly MeasurementViewModel _viewModel;

    public MeasurementPage(MeasurementViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;

        LoadDeviceContent();
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

    protected override void OnDisappearing()
    {
        _viewModel.Dispose();
        base.OnDisappearing();
    }
}