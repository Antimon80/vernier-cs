using System.Collections.ObjectModel;
using App.Models;
using App.Resources.Strings;
using App.Util;
using App.ViewModels.GoDirect;
using Backend.Devices;
using Backend.Devices.GoDirect;
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
            SpectroVisMeasurementViewModel spectroVisViewModel = new(_deviceManager.CurrentSpectrometer, isMeasurementRunningProvider: () => IsMeasurementRunning, Table);
            DeviceViewModel = spectroVisViewModel;
            MeasurementSettings = spectroVisViewModel as IMeasurementSettings ?? new NoOpMeasurementWorkflow();
            MeasurementSettings.AutoStopRequested += OnAutoStopRequested;

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
    public object DeviceViewModel { get; }

    /// <summary>
    /// Device-specific dialog/workflow adapter used by generic toolbar commands.
    /// </summary>
    public IMeasurementSettings MeasurementSettings { get; }

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
        HasOperatingModeSelection = MeasurementSettings.HasOperatingModeSelection;
        HasKeepDataPointCommand = MeasurementSettings.CanKeepDataPoint;

        if (DeviceViewModel is SpectroVisMeasurementViewModel spectroVisViewModel)
        {
            spectroVisViewModel.RefreshAll();
        }

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

        MeasurementSettings.AutoStopRequested -= OnAutoStopRequested;

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
        if (!MeasurementSettings.HasOperatingModeSelection)
        {
            return;
        }

        await MeasurementSettings.RequestOperatingModeDialog();
        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanChangeMeasurementConfiguration))]
    private async Task OpenAcquisitionMode()
    {
        await MeasurementSettings.RequestAcquisitionModeDialog();
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

        CalibrationDialogResult? result = await MeasurementSettings.RequestCalibrationDialog();

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
            MeasurementSettings.OnMeasurementStopped();
        }

        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanKeepDataPoint))]
    private async Task KeepDataPoint()
    {
        if (!MeasurementSettings.CanKeepDataPoint)
        {
            await Shell.Current.DisplayAlertAsync(AppResources.Device_KeepDataPoint, AppResources.Dialog_CannotKeepDataPoint, AppResources.Dialog_Ok);
            return;
        }

        await MeasurementSettings.RequestKeepDataPointDialog();

        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private void AutoscaleYAxis()
    {
        MeasurementSettings.AutoscaleYAxis();
    }

    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private void AutoscaleFull()
    {
        MeasurementSettings.AutoscaleFull();
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
        if (_deviceManager.CurrentSpectrometer is not null
            && (_deviceManager.CurrentSpectrometer.Session.Mode is OperatingMode.Absorbance or OperatingMode.Transmission))
        {
            return _deviceManager.CurrentSpectrometer.IsCalibrated;
        }
        else
        {
            return true;
        }
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

    private sealed class NoOpMeasurementWorkflow : IMeasurementSettings
    {
        public bool HasOperatingModeSelection => false;
        public bool HasZeroCommand => false;
        public bool CanKeepDataPoint => false;

        public event Action? AutoStopRequested;

        public Task RequestOperatingModeDialog(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task RequestAcquisitionModeDialog(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task<CalibrationDialogResult?> RequestCalibrationDialog(CancellationToken ct = default)
        {
            return Task.FromResult<CalibrationDialogResult?>(new CalibrationDialogResult(SkipWarmup: null));
        }

        public Task SetToZero(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task RequestKeepDataPointDialog(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public void AutoscaleYAxis()
        {

        }

        public void AutoscaleFull()
        {
            
        }

        public void OnMeasurementStopped()
        {

        }
    }
}