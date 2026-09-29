
using App.Resources.Strings;
using App.ViewModels;
using App.ViewModels.GoDirect;

namespace App.Views.GoDirect;

public partial class SpectroVisCalibrationDialog : ContentPage
{
    private SpectroVisCalibrationViewModel ViewModel => (SpectroVisCalibrationViewModel)BindingContext;

    public SpectroVisCalibrationDialog(SpectroVisCalibrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void SkipWarmupClicked(object? sender, EventArgs e)
    {
        ViewModel.SkipWarmupSelected = true;
    }

    private async void CancelClicked(object? sender, EventArgs e)
    {
        ViewModel.Complete();
        await Navigation.PopModalAsync();
    }

    private async void OkClicked(object? sender, EventArgs e)
    {
        ViewModel.Complete();
        await Navigation.PopModalAsync();
    }

    private async void HelpClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync(AppResources.App_Help, "Hier kommt später die Hilfeseite für die Kalibrierung hin.", AppResources.Dialog_Ok);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        ViewModel.Dispose();
    }
}