using System.Collections.ObjectModel;
using App.Models;
using App.Resources.Strings;
using App.Util;
using Backend.Devices;
using Backend.Discovery;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels;

public sealed partial class MeasurementViewModel : ObservableObject, IDisposable
{
    private const string StartIcon = "start.png";
    private const string StopIcon = "stop.png";
    private readonly DeviceManager _deviceManager;
    private bool _disposed;

    public MeasurementViewModel(DeviceManager deviceManager)
    {
        _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));

        CurrentDevice = _deviceManager.CurrentDevice ?? throw new InvalidOperationException("No current device is selected.");

        if (_deviceManager.CurrentSpectrometer is not null)
        {
            DeviceViewModel = DeviceModelFactory.Create(_deviceManager, () => IsMeasurementRunning, Table);
            DeviceViewModel.AutoStopRequested += OnAutoStopRequested;

            RefreshDeviceState();

            return;
        }

        throw new InvalidOperationException($"The selected device type '{CurrentDevice.DeviceName}' is not supported by the measurement UI yet.");

    }

    public IDevice CurrentDevice { get; }

    /// <summary>
    /// Device-specific view model used by the content area.
    /// The generic page does not inspect this object directly.
    /// </summary>
    public IDeviceMeasurementViewModel DeviceViewModel { get; }

    public WideMeasurementTable Table { get; } = new();

    public ObservableCollection<UiDiagnostics> Diagnostics { get; } = [];
    public bool HasDiagnostics => Diagnostics.Count > 0;

    public event Func<CancellationToken, Task>? DiagnosticsRequested;

    [ObservableProperty]
    public partial string PageTitle { get; set; } = AppResources.App_AppName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOperatingModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenAcquisitionModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CalibrateCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoscaleYAxisCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoscaleFullCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowCrosshairsCommand))]
    public partial bool IsMeasurementRunning { get; set; }

    [ObservableProperty]
    public partial string RecordingIcon { get; set; } = StartIcon;


    [ObservableProperty]
    public partial bool HasOperatingModeSelection { get; set; }

    [ObservableProperty]
    public partial bool HasKeepDataPointCommand { get; set; }

    public void RefreshDeviceState()
    {
        HasOperatingModeSelection = DeviceViewModel.HasOperatingModeSelection;
        HasKeepDataPointCommand = DeviceViewModel.CanKeepDataPoint;

        DeviceViewModel.Refresh();

        RefreshDiagnostics();

        ToggleMeasurementCommand.NotifyCanExecuteChanged();
        KeepDataPointCommand.NotifyCanExecuteChanged();
        CalibrateCommand.NotifyCanExecuteChanged();
    }

    public void RefreshDiagnostics()
    {
        Diagnostics.Clear();

        UiDiagnostics.AddDiagnostics(Diagnostics, _deviceManager.Diagnostics);
        UiDiagnostics.AddDiagnostics(Diagnostics, CurrentDevice.Diagnostics);

        OnPropertyChanged(nameof(HasDiagnostics));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DeviceViewModel.AutoStopRequested -= OnAutoStopRequested;

        if (DeviceViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [RelayCommand]
    private Task OpenFile()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_OpenFile);
    }

    [RelayCommand]
    private Task SaveFile()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_SaveFile);
    }

    [RelayCommand]
    private Task SaveFileAs()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_SaveFileAs);
    }

    [RelayCommand]
    private Task ExportData()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_ExportData);
    }

    [RelayCommand]
    private Task ImportData()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_ImportData);
    }

    [RelayCommand]
    private Task OpenCursor()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_CrossHairs);
    }

    [RelayCommand]
    private Task OpenDataManager()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_DataManagement);
    }

    [RelayCommand]
    private Task OpenAnalysis()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_DataAnalysis);
    }

    [RelayCommand]
    private async Task OpenDiagnostics(CancellationToken ct)
    {
        RefreshDiagnostics();

        if (DiagnosticsRequested is null)
        {
            throw new InvalidOperationException("No diagnostics dialog is registered.");
        }

        await DiagnosticsRequested(ct);
    }

    [RelayCommand]
    private Task OpenSettings()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_Settings);
    }

    [RelayCommand]
    private Task OpenAbout()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_About);
    }

    [RelayCommand]
    private Task OpenHelp()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_Help);
    }

    [RelayCommand(CanExecute = nameof(CanChangeMeasurementConfiguration))]
    private async Task OpenOperatingMode()
    {
        if (!DeviceViewModel.HasOperatingModeSelection)
        {
            return;
        }

        await DeviceViewModel.RequestOperatingModeDialog();
        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanChangeMeasurementConfiguration))]
    private async Task OpenAcquisitionMode()
    {
        await DeviceViewModel.RequestAcquisitionModeDialog();
        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task Calibrate()
    {
        if (!CurrentDevice.CanCalibrate)
        {
            await Shell.Current.DisplayAlertAsync(AppResources.Device_Calibrate, AppResources.Dialog_CannotCalibrate, AppResources.Dialog_Ok);

            return;
        }

        CalibrationDialogResult? result = await DeviceViewModel.RequestCalibrationDialog();

        if (result is null)
        {
            return;
        }

        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanToggleMeasurement))]
    private void ToggleMeasurement()
    {
        IsMeasurementRunning = !IsMeasurementRunning;
        RecordingIcon = IsMeasurementRunning ? StopIcon : StartIcon;

        if (!IsMeasurementRunning)
        {
            DeviceViewModel.OnMeasurementStopped();
        }

        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanKeepDataPoint))]
    private async Task KeepDataPoint()
    {
        if (!DeviceViewModel.CanKeepDataPoint)
        {
            await Shell.Current.DisplayAlertAsync(AppResources.Device_KeepDataPoint, AppResources.Dialog_CannotKeepDataPoint, AppResources.Dialog_Ok);
            return;
        }

        await DeviceViewModel.RequestKeepDataPointDialog();

        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private void AutoscaleYAxis()
    {
        DeviceViewModel.AutoscaleYAxis();
    }

    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private void AutoscaleFull()
    {
        DeviceViewModel.AutoscaleFull();
    }

    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private Task ShowCrosshairs()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_CrossHairs);
    }

    private bool CanChangeMeasurementConfiguration()
    {
        return !IsMeasurementRunning;
    }

    private bool CanUseChartTools()
    {
        return IsMeasurementRunning;
    }

    private bool CanCalibrate()
    {
        return !IsMeasurementRunning && CurrentDevice.CanCalibrate;
    }

    private bool CanToggleMeasurement()
    {
        return DeviceViewModel.CanStartMeasurement;
    }

    private bool CanKeepDataPoint()
    {
        return IsMeasurementRunning;
    }

    private void OnAutoStopRequested()
    {
        if (IsMeasurementRunning)
        {
            ToggleMeasurement();
        }
    }
}