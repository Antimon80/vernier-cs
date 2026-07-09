using App.Resources.Strings;
using App.ViewModels.GoDirect;
using Backend.Devices;
using Backend.Discovery;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels;

public sealed partial class MeasurementViewModel : ObservableObject, IDisposable
{
    private const string StartIcon = "start.png";
    private const string StopIcon = "stop.png";
    private readonly IDeviceManager _deviceManager;
    private bool _disposed;

    public IDevice CurrentDevice { get; }

    /// <summary>
    /// Device-specific view model used by the content area.
    /// The generic page does not inspect this object directly.
    /// </summary>
    public object DeviceViewModel { get; }

    /// <summary>
    /// Device-specific dialog/workflow adapter used by generic toolbar commands.
    /// </summary>
    public IMeasurementWorkflow Workflow { get; }

    [ObservableProperty]
    public partial string PageTitle { get; set; } = AppResources.App_AppName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOperatingModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenAcquisitionModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CalibrateCommand))]
    public partial bool IsMeasurementRunning { get; set; }

    [ObservableProperty]
    public partial string RecordingIcon { get; set; } = StartIcon;


    [ObservableProperty]
    public partial bool HasOperatingModeSelection { get; set; }

    [ObservableProperty]
    public partial bool IsCalibrationCommandEnabled { get; set; }

    public MeasurementViewModel(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));

        CurrentDevice = _deviceManager.CurrentDevice ?? throw new InvalidOperationException("No current device is selected.");

        if (_deviceManager.CurrentSpectrometer is not null)
        {
            SpectroVisMeasurementViewModel spectroVisViewModel = new SpectroVisMeasurementViewModel(_deviceManager.CurrentSpectrometer, isMeasurementRunningProvider: () => IsMeasurementRunning);
            DeviceViewModel = spectroVisViewModel;
            Workflow = spectroVisViewModel as IMeasurementWorkflow ?? new NoOpMeasurementWorkflow();

            RefreshDeviceState();

            return;
        }

        throw new InvalidOperationException($"The selected device type '{CurrentDevice.DeviceName}' is not supported by the measurement UI yet.");

    }

    public void RefreshDeviceState()
    {
        HasOperatingModeSelection = Workflow.HasOperatingModeSelection;
        IsCalibrationCommandEnabled = CurrentDevice.CanCalibrate;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (DeviceViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [RelayCommand]
    private Task OpenFile()
    {
        return ShowNotImplementedAsync(AppResources.App_OpenFile);
    }

    [RelayCommand]
    private Task SaveFile()
    {
        return ShowNotImplementedAsync(AppResources.App_SaveFile);
    }

    [RelayCommand]
    private Task SaveFileAs()
    {
        return ShowNotImplementedAsync(AppResources.App_SaveFileAs);
    }

    [RelayCommand]
    private Task Print()
    {
        return ShowNotImplementedAsync(AppResources.App_Print);
    }

    [RelayCommand]
    private Task ExportData()
    {
        return ShowNotImplementedAsync(AppResources.App_ExportData);
    }

    [RelayCommand]
    private Task ImportData()
    {
        return ShowNotImplementedAsync(AppResources.App_ImportData);
    }

    [RelayCommand]
    private Task OpenCursor()
    {
        return ShowNotImplementedAsync(AppResources.App_CrossHairs);
    }

    [RelayCommand]
    private Task OpenDataManager()
    {
        return ShowNotImplementedAsync(AppResources.App_DataManagement);
    }

    [RelayCommand]
    private Task OpenAnalysis()
    {
        return ShowNotImplementedAsync(AppResources.App_DataAnalysis);
    }

    [RelayCommand]
    private Task OpenSettings()
    {
        return ShowNotImplementedAsync(AppResources.App_Settings);
    }

    [RelayCommand]
    private Task OpenHelp()
    {
        return ShowNotImplementedAsync(AppResources.App_Help);
    }

    [RelayCommand(CanExecute = nameof(CanChangeMeasurementConfiguration))]
    private async Task OpenOperatingMode()
    {
        if (!Workflow.HasOperatingModeSelection)
        {
            return;
        }

        await Workflow.OpenOperatingMode();
        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanChangeMeasurementConfiguration))]
    private async Task OpenAcquisitionMode()
    {
        await Workflow.OpenAcquisitionMode();
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

        CalibrationDialogResult? result = await Workflow.ShowCalibrationDialog();

        if (result is null)
        {
            return;
        }

        await CurrentDevice.Calibrate(result.SkipWarmup);
        RefreshDeviceState();
    }

    [RelayCommand]
    private void ToggleMeasurement()
    {
        IsMeasurementRunning = !IsMeasurementRunning;

        RecordingIcon = IsMeasurementRunning ? StopIcon : StartIcon;

        RefreshDeviceState();
    }

    private bool CanChangeMeasurementConfiguration()
    {
        return !IsMeasurementRunning;
    }

    private bool CanCalibrate()
    {
        return !IsMeasurementRunning && CurrentDevice.CanCalibrate;
    }

    private static Task ShowNotImplementedAsync(string feature)
    {
        return Shell.Current.DisplayAlertAsync(feature, "not implemented yet", AppResources.Dialog_Ok);
    }

    private sealed class NoOpMeasurementWorkflow : IMeasurementWorkflow
    {
        public bool HasOperatingModeSelection => false;
        public bool HasZeroCommand => false;

        public Task OpenOperatingMode(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task OpenAcquisitionMode(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task<CalibrationDialogResult?> ShowCalibrationDialog(CancellationToken ct = default)
        {
            return Task.FromResult<CalibrationDialogResult?>(new CalibrationDialogResult(SkipWarmup: null));
        }

        public Task SetToZero(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}