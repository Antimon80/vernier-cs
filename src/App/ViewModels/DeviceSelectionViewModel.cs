using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using App.Models;
using App.Resources.Strings;
using Backend.Discovery;
using App.Services;

namespace App.ViewModels;

public sealed partial class DeviceSelectionViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceManager _deviceManager;
    private bool _disposed;
    private readonly LocalizationService _localization;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = AppResources.DeviceSelection_Searching;

    public ObservableCollection<DeviceSelectionItem> Devices { get; } = [];

    public ObservableCollection<UiDiagnostics> Diagnostics { get; } = [];

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public DeviceSelectionViewModel(IDeviceManager deviceManager, LocalizationService localization)
    {
        _deviceManager = deviceManager;
        _localization = localization;

        _deviceManager.DevicesChanged += OnDevicesChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Performs the initial device discovery when the start page is opened.
    /// Further updates should come from native hotplug notifications through
    /// DeviceManager.DevicesChanged.
    /// </summary>
    public async Task DiscoverDevicesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = AppResources.DeviceSelection_Searching;

            IReadOnlyList<DeviceDescriptor> found =
                await Task.Run(() => _deviceManager.ListDevices());

            ApplyDevices(found);
            RefreshDiagnostics();
            UpdateStatusText(found.Count);
        }
        catch (Exception ex)
        {
            RefreshDiagnostics();

            StatusText = Diagnostics.Count > 0
                ? Diagnostics[^1].Message
                : string.Format(AppResources.DeviceSelection_DiscoveryFailed, ex.GetType().Name, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Connects the selected device by index from the current DeviceManager snapshot.
    /// </summary>
    public async Task ConnectDeviceAsync(int deviceIndex)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = AppResources.DeviceSelection_Connecting;

            await _deviceManager.Connect(deviceIndex);

            RefreshDiagnostics();

            StatusText = _deviceManager.CurrentDevice is null
                ? AppResources.DeviceSelection_Conntected
                : string.Format(AppResources.DeviceSelection_ConnectedToDevice, _deviceManager.CurrentDevice.DeviceName);
        }
        catch (Exception ex)
        {
            RefreshDiagnostics();

            StatusText = Diagnostics.Count > 0
                ? Diagnostics[^1].Message
                : string.Format(AppResources.DeviceSelection_ConnectionFailed, ex.GetType().Name, ex.Message);

            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnDevicesChanged(IReadOnlyList<DeviceDescriptor> devices)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            ApplyDevices(devices);
            RefreshDiagnostics();
            UpdateStatusText(devices.Count);
        });
    }

    private void ApplyDevices(IReadOnlyList<DeviceDescriptor> devices)
    {
        Devices.Clear();

        for (int i = 0; i < devices.Count; i++)
        {
            DeviceDescriptor device = devices[i];

            Devices.Add(new DeviceSelectionItem(Index: i, DisplayName: device.Name));
        }
    }

    private void RefreshDiagnostics()
    {
        Diagnostics.Clear();

        foreach (var diagnostic in _deviceManager.Diagnostics)
        {
            Diagnostics.Add(new UiDiagnostics(
                Severity: diagnostic.Severity.ToString(),
                Category: diagnostic.Category.ToString(),
                Code: diagnostic.Code,
                Message: diagnostic.Message,
                TechnicalDetails: diagnostic.TechnicalDetails ?? string.Empty));
        }

        OnPropertyChanged(nameof(HasDiagnostics));
    }

    private void UpdateStatusText(int deviceCount)
    {
        StatusText = deviceCount switch
        {
            0 => AppResources.DeviceSelection_NoDevicesFound,
            1 => AppResources.DeviceSelection_DeviceFoundSingular,
            _ => string.Format(AppResources.DeviceSelection_DeviceFoundPlural, deviceCount)
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!IsBusy)
        {
            StatusText = AppResources.DeviceSelection_Searching;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _deviceManager.DevicesChanged -= OnDevicesChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _disposed = true;
    }
}

public sealed record DeviceSelectionItem(
    int Index,
    string DisplayName);