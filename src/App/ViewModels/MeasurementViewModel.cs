using App.Resources.Strings;
using App.ViewModels.GoDirect;
using Backend.Discovery;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.ViewModels;

public sealed partial class MeasurementViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceManager _deviceManager;
    private bool _disposed;

    [ObservableProperty]
    public partial object? DeviceViewModel {get; set;}
    [ObservableProperty]
    public partial string PageTitle {get; set;} = AppResources.App_AppName;

    public MeasurementViewModel(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));

        if(_deviceManager.CurrentSpectrometer is not null)
        {
            DeviceViewModel = new SpectroVisMeasurementViewModel(_deviceManager.CurrentSpectrometer);
            return;
        }

        throw new InvalidOperationException("No supported measurement device is currently selected.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if(DeviceViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}