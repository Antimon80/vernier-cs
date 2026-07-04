using App.Views;

namespace App;

public partial class App : Application
{
    private readonly DeviceSelectionPage _startPage;

    public App(DeviceSelectionPage startPage)
    {
        InitializeComponent();
        _startPage = startPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new NavigationPage(_startPage));
    }
}