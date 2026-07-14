using App.Resources.Strings;
using App.ViewModels.GoDirect;

namespace App.Views.GoDirect;

public partial class SpectroVisKeepDataPointDialog : ContentPage
{
    private SpectroVisKeepDataPointViewModel ViewModel => (SpectroVisKeepDataPointViewModel)BindingContext;

    public SpectroVisKeepDataPointDialog(SpectroVisKeepDataPointViewModel viewModel)
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
            await ViewModel.ValueSetCommand.ExecuteAsync(null);
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppResources.Dialog_ErrorTitle, ex.Message, AppResources.Dialog_Ok);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        ViewModel.Dispose();
    }


}