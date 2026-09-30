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

        DeviceViewModel = DeviceViewModelFactory.Create(_deviceManager, Table, Chart, Data);
        DeviceViewModel.AutoStopRequested += OnAutoStopRequested;

        RefreshDeviceState();
    }

    /// <summary>
    /// Gets the currently selected device.
    /// </summary>
    public IDevice CurrentDevice { get; }

    /// <summary>
    /// Device-specific view model used by the content area.
    /// The generic page does not inspect this object directly.
    /// </summary>
    public IDeviceMeasurementViewModel DeviceViewModel { get; }

    /// <summary>
    /// Measurement data shared between the generic measurement page and the device-specific view model.
    /// </summary>
    public WideMeasurementTable Table { get; } = new();

    public ChartModel Chart { get; } = new();

    public MeasurementDataSet Data { get; } = new();

    public ObservableCollection<UiDiagnostics> Diagnostics { get; } = [];
    public bool HasDiagnostics => Diagnostics.Count > 0;

    public event Func<CancellationToken, Task>? DiagnosticsRequested;
    public event Func<CancellationToken, Task>? OperatingModeDialogRequested;
    public event Func<CancellationToken, Task>? AcquisitionModeDialogRequested;
    public event Func<CancellationToken, Task>? KeepDataPointDialogRequested;
    public event Func<CancellationToken, Task>? CalibrationDialogRequested;

    [ObservableProperty]
    public partial string PageTitle { get; set; } = AppResources.App_AppName;

    /// <summary>
    /// Indicates whether measurement is currently active in the measurement UI.
    /// </summary>
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

    /// <summary>
    /// Refreshes the device-specific state and the UI state derived from it.
    /// </summary>
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

    /// <summary>
    /// Refreshes the diagnostics displayed by the measurement UI.
    /// </summary>
    public void RefreshDiagnostics()
    {
        Diagnostics.Clear();

        UiDiagnostics.AddDiagnostics(Diagnostics, _deviceManager.Diagnostics);
        UiDiagnostics.AddDiagnostics(Diagnostics, CurrentDevice.Diagnostics);

        OnPropertyChanged(nameof(HasDiagnostics));
    }

    /// <summary>
    /// Releases event subscriptions and disposes the device-specific view model.
    /// </summary>
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

    /// <summary>
    /// Opens the operating-mode dialog when measurement is not running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeMeasurementConfiguration))]
    private async Task OpenOperatingMode(CancellationToken ct)
    {
        if (!DeviceViewModel.HasOperatingModeSelection)
        {
            return;
        }

        if (OperatingModeDialogRequested is null)
        {
            throw new InvalidOperationException("No operating mode dialog is registered.");
        }

        await OperatingModeDialogRequested(ct);
        RefreshDeviceState();
    }

    /// <summary>
    /// Opens the acquisition-mode dialog when measurement is not running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeMeasurementConfiguration))]
    private async Task OpenAcquisitionMode(CancellationToken ct)
    {
        if (AcquisitionModeDialogRequested is null)
        {
            throw new InvalidOperationException("No acquisition mode dialog is registered.");
        }

        await AcquisitionModeDialogRequested(ct);
        RefreshDeviceState();
    }

    /// <summary>
    /// Opens the calibration dialog when calibration is supported and measurement is not running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCalibrate))]
    private async Task Calibrate(CancellationToken ct)
    {
        if (!CurrentDevice.CanCalibrate)
        {
            await Shell.Current.DisplayAlertAsync(AppResources.Device_Calibrate, AppResources.Dialog_CannotCalibrate, AppResources.Dialog_Ok);

            return;
        }

        if (CalibrationDialogRequested is null)
        {
            throw new InvalidOperationException("No calibration dialog is registered.");
        }

        await CalibrationDialogRequested(ct);

        RefreshDeviceState();
    }

    /// <summary>
    /// Toggles the measurement state shown by the UI.
    /// Stopping the measurement also notifies the device-specific view model.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleMeasurement))]
    private void ToggleMeasurement()
    {
        IsMeasurementRunning = !IsMeasurementRunning;
        RecordingIcon = IsMeasurementRunning ? StopIcon : StartIcon;

        if (IsMeasurementRunning)
        {
            DeviceViewModel.OnMeasurementStarted();
        }
        else
        {
            DeviceViewModel.OnMeasurementStopped();
        }

        RefreshDeviceState();
    }

    /// <summary>
    /// Opens the dialog for capturing the current value as a data point.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanKeepDataPoint))]
    private async Task KeepDataPoint(CancellationToken ct)
    {
        if (!DeviceViewModel.CanKeepDataPoint)
        {
            await Shell.Current.DisplayAlertAsync(AppResources.Device_KeepDataPoint, AppResources.Dialog_CannotKeepDataPoint, AppResources.Dialog_Ok);
            return;
        }

        if (KeepDataPointDialogRequested is null)
        {
            throw new InvalidOperationException("No keep data point dialog registered.");
        }

        await KeepDataPointDialogRequested(ct);

        RefreshDeviceState();
    }

    /// <summary>
    /// Automatically adjusts the Y-axis to the current measurement data.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private void AutoscaleYAxis()
    {
        Chart.AutoscaleYAxis();
    }

    /// <summary>
    /// Automatically adjusts the chart axes to show the complete measurement data.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private void AutoscaleFull()
    {
        Chart.AutoscaleFull();
    }

    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private Task ShowCrosshairs()
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

    /// <summary>
    /// Refreshes the diagnostics and opens the diagnostics dialog.
    /// </summary>
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

    /// <summary>
    /// Determines whether measurement configuration can currently be changed.
    /// </summary>
    private bool CanChangeMeasurementConfiguration()
    {
        return !IsMeasurementRunning;
    }

    /// <summary>
    /// Determines whether chart tools can currently be used.
    /// </summary>
    private bool CanUseChartTools()
    {
        return IsMeasurementRunning;
    }

    /// <summary>
    /// Determines whether calibration can currently be started.
    /// </summary>
    private bool CanCalibrate()
    {
        return !IsMeasurementRunning && CurrentDevice.CanCalibrate;
    }

    /// <summary>
    /// Determines whether measurement can currently be started or stopped.
    /// </summary>
    private bool CanToggleMeasurement()
    {
        return DeviceViewModel.CanStartMeasurement;
    }

    /// <summary>
    /// Determines whether a data point can currently be captured.
    /// </summary>
    private bool CanKeepDataPoint()
    {
        return IsMeasurementRunning;
    }

    /// <summary>
    /// Stops the measurement when the device-specific view model requests an automatic stop.
    /// </summary>
    private void OnAutoStopRequested()
    {
        if (IsMeasurementRunning)
        {
            ToggleMeasurement();
        }
    }
}