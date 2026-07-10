using System.Collections.ObjectModel;
using App.Models;

namespace App.Views;

public partial class DiagnosticsDialog : ContentPage
{
    public DiagnosticsDialog(ObservableCollection<UiDiagnostics> diagnostics)
    {
        InitializeComponent();

        BindingContext = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private async void CloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}