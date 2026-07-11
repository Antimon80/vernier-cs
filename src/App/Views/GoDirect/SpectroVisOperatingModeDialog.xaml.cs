using App.Resources.Strings;
using App.ViewModels.GoDirect;

namespace App.Views.GoDirect;

public partial class SpectroVisOperatingModeDialog : ContentPage
{
    private SpectroVisOperatingModeViewModel ViewModel => (SpectroVisOperatingModeViewModel)BindingContext;

    public SpectroVisOperatingModeDialog(SpectroVisOperatingModeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void CancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private async void OkClicked(object? sender, EventArgs e)
    {
        try
        {
            await ViewModel.IntegrationTimeChangedCommand.ExecuteAsync(null);
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppResources.Dialog_ErrorTitle, ex.Message, AppResources.Dialog_Ok);
        }
    }

    private async void HelpClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync(AppResources.App_Help, "Hier kommt später die Hilfeseite für die Betriebsmodi hin.", AppResources.Dialog_Ok);
    }
}