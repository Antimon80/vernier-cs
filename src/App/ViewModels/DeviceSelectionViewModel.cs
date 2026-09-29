using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using App.Models;
using App.Resources.Strings;
using Backend.Discovery;
using App.Services;
using Backend.Devices;
using Backend.Util;

namespace App.ViewModels;

public sealed partial class DeviceSelectionViewModel : ObservableObject, IDisposable
{
    private readonly DeviceManager _deviceManager;
    private bool _disposed;
    private readonly LocalizationService _localization;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = AppResources.DeviceSelection_Searching;

    public ObservableCollection<DeviceSelectionItem> Devices { get; } = [];

    public ObservableCollection<UiDiagnostics> Diagnostics { get; } = [];

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public DeviceSelectionViewModel(DeviceManager deviceManager, LocalizationService localization)
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
                : string.Format(AppResources.DeviceSelection_DiscoveryFailedWithDetails, ex.GetType().Name, ex.Message);
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
            IDevice? device = _deviceManager.CurrentDevice;

            if (device is null)
            {
                RefreshDiagnostics();
                StatusText = AppResources.DeviceSelection_ConnectionFailed;
                return;
            }

            await device.Initialize();

            if (!device.IsInitialized)
            {
                RefreshDiagnostics();
                StatusText = Diagnostics.Count > 0
                    ? Diagnostics[^1].Message
                    : string.Format(AppResources.DeviceSelection_InitializationFailed, _deviceManager.CurrentDevice?.DeviceName);
                return;
            }

            device.StartMeasurement();

            RefreshDiagnostics();

            StatusText = _deviceManager.CurrentDevice is null
                ? AppResources.DeviceSelection_Connected
                : string.Format(AppResources.DeviceSelection_ConnectedToDevice, _deviceManager.CurrentDevice.DeviceName);
        }
        catch (Exception ex)
        {
            RefreshDiagnostics();

            StatusText = Diagnostics.Count > 0
                ? Diagnostics[^1].Message
                : string.Format(AppResources.DeviceSelection_ConnectionFailedWithDetails, ex.GetType().Name, ex.Message);

            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Updates the device list and status when the available devices change.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the device selection items from the current device snapshot.
    /// </summary>
    private void ApplyDevices(IReadOnlyList<DeviceDescriptor> devices)
    {
        Devices.Clear();

        for (int i = 0; i < devices.Count; i++)
        {
            DeviceDescriptor device = devices[i];

            Devices.Add(new DeviceSelectionItem(Index: i, DisplayName: device.Name));
        }
    }

    /// <summary>
    /// Updates the UI diagnostics from the diagnostics reported by the device manager.
    /// </summary>
    private void RefreshDiagnostics()
    {
        Diagnostics.Clear();

        foreach (DiagnosticEntry diagnostic in _deviceManager.Diagnostics)
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

    /// <summary>
    /// Updates the status text based on the number of discovered devices.
    /// </summary>
    private void UpdateStatusText(int deviceCount)
    {
        StatusText = deviceCount switch
        {
            0 => AppResources.DeviceSelection_NoDevicesFound,
            1 => AppResources.DeviceSelection_DeviceFoundSingular,
            _ => string.Format(AppResources.DeviceSelection_DeviceFoundPlural, deviceCount)
        };
    }

    /// <summary>
    /// Updates localized status text when the application language changes.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!IsBusy)
        {
            StatusText = AppResources.DeviceSelection_Searching;
        }
    }

    /// <summary>
    /// Unsubscribes from device and localization events when the view model is no longer used.
    /// </summary>
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