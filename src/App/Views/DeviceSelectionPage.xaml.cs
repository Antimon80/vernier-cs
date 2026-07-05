using App.ViewModels;
using App.Views.LabQuest;
using App.Views.SpectroVis;
using App.Resources.Strings;
using Backend.Discovery;

namespace App.Views;

public partial class DeviceSelectionPage : ContentPage
{
    private readonly DeviceSelectionViewModel _viewModel;
    private readonly IDeviceManager _deviceManager;
    private bool _hasSearched;

    public DeviceSelectionPage(DeviceSelectionViewModel viewModel, IDeviceManager deviceManager)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _deviceManager = deviceManager;

        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasSearched)
        {
            return;
        }

        _hasSearched = true;
        await _viewModel.DiscoverDevicesAsync();
    }

    private async void OnDeviceButtonClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button ||
            button.BindingContext is not DeviceSelectionItem item)
        {
            return;
        }

        try
        {
            await _viewModel.ConnectDeviceAsync(item.Index);

            IServiceProvider services = Handler!.MauiContext!.Services;

            Page nextPage = _deviceManager.CurrentSpectrometer is not null
                ? services.GetRequiredService<SpectrometerPage>()
                : services.GetRequiredService<LabQuestPlaceholderPage>();

            NavigationPage.SetHasBackButton(nextPage, false);
            await Navigation.PushAsync(nextPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppResources.DeviceSelection_ConnectionFailed, ex.Message, AppResources.Dialog_Ok);
        }
    }
}