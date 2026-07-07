using App.ViewModels;

namespace App.Views;

public partial class MeasurementPage : ContentPage
{
    public MeasurementPage(MeasurementViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        if (BindingContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnDisappearing();
    }
}