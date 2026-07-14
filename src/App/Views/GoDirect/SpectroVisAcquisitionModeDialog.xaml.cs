using App.Resources.Strings;
using App.ViewModels.GoDirect;

namespace App.Views.GoDirect;

public partial class SpectroVisAcquisitionModeDialog : ContentPage
{
    private SpectroVisAcquisitionModeViewModel ViewModel => (SpectroVisAcquisitionModeViewModel)BindingContext;
    public SpectroVisAcquisitionModeDialog(SpectroVisAcquisitionModeViewModel viewModel)
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
            ViewModel.TimeResolvedSettingsChangedCommand.Execute(null);
            await ViewModel.EventTriggeredSettingsChangedCommand.ExecuteAsync(null);

            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppResources.Dialog_ErrorTitle, ex.Message, AppResources.Dialog_Ok);
        }
    }

    private async void HelpClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync(AppResources.App_Help, "Hier kommt später die Hilfeseite für Datenerfassungsmodi hin.", AppResources.Dialog_Ok);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        ViewModel.Dispose();
    }
}