using System.Collections.ObjectModel;
using App.Models;
using Backend.Discovery;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.ViewModels;

public sealed partial class DeviceSelectionViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceManager _deviceManager;
    private bool _disposed;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Suche angeschlossene Geräte ...";

    public ObservableCollection<DeviceSelectionItem> Devices { get; } = [];

    public ObservableCollection<UiDiagnostics> Diagnostics { get; } = [];

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public DeviceSelectionViewModel(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
        _deviceManager.DevicesChanged += OnDevicesChanged;
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
            StatusText = "Suche angeschlossene Geräte ...";

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
                : $"Gerätesuche fehlgeschlagen: {ex.GetType().Name}: {ex.Message}";
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
            StatusText = "Verbinde Gerät ...";

            await _deviceManager.Connect(deviceIndex);

            RefreshDiagnostics();

            StatusText = _deviceManager.CurrentDevice is null
                ? "Gerät verbunden."
                : $"Verbunden mit {_deviceManager.CurrentDevice.DeviceName}.";
        }
        catch (Exception ex)
        {
            RefreshDiagnostics();

            StatusText = Diagnostics.Count > 0
                ? Diagnostics[^1].Message
                : $"Verbindung fehlgeschlagen: {ex.GetType().Name}: {ex.Message}";

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

        Diagnostics.Add(new UiDiagnostics(
            Severity: "Test",
            Category: "Test",
            Code: "DEBUG",
            Message: "Dieser Eintrag kommt aus dem DeviceSelectionViewModel",
            TechnicalDetails: "Test"
        ));

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
            0 => "Kein unterstütztes Gerät gefunden.",
            1 => "1 unterstütztes Gerät gefunden.",
            _ => $"{deviceCount} unterstützte Geräte gefunden."
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _deviceManager.DevicesChanged -= OnDevicesChanged;
        _disposed = true;
    }
}

public sealed record DeviceSelectionItem(
    int Index,
    string DisplayName);