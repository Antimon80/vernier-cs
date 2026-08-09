using System.Collections.ObjectModel;
using App.Models;
using App.Resources.Strings;
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
            SpectroVisMeasurementViewModel spectroVisViewModel = new(_deviceManager.CurrentSpectrometer, isMeasurementRunningProvider: () => IsMeasurementRunning);
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

    public ObservableCollection<UiDiagnostics> Diagnostics { get; } = [];
    public bool HasDiagnostics => Diagnostics.Count > 0;

    public event Func<CancellationToken, Task>? DiagnosticsRequested;

    [ObservableProperty]
    public partial string PageTitle { get; set; } = AppResources.App_AppName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOperatingModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenAcquisitionModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CalibrateCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoscaleCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowCrosshairsCommand))]
    public partial bool IsMeasurementRunning { get; set; }

    [ObservableProperty]
    public partial string RecordingIcon { get; set; } = StartIcon;


    [ObservableProperty]
    public partial bool HasOperatingModeSelection { get; set; }

    [ObservableProperty]
    public partial bool IsCalibrationEnabled { get; set; }

    [ObservableProperty]
    public partial bool HasKeepDataPointCommand { get; set; }

    public void RefreshDeviceState()
    {
        HasOperatingModeSelection = MeasurementSettings.HasOperatingModeSelection;
        HasKeepDataPointCommand = MeasurementSettings.CanKeepDataPoint;
        IsCalibrationEnabled = CurrentDevice.CanCalibrate;

        if (DeviceViewModel is SpectroVisMeasurementViewModel spectroVisViewModel)
        {
            spectroVisViewModel.RefreshAll();
        }

        RefreshDiagnostics();

        ToggleMeasurementCommand.NotifyCanExecuteChanged();
        KeepDataPointCommand.NotifyCanExecuteChanged();
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
        return ShowNotImplementedAsync(AppResources.App_Settings);
    }

    [RelayCommand]
    private Task OpenAbout()
    {
        return ShowNotImplementedAsync(AppResources.App_About);
    }

    [RelayCommand]
    private Task OpenHelp()
    {
        return ShowNotImplementedAsync(AppResources.App_Help);
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

        await CurrentDevice.Calibrate(result.SkipWarmup);
        RefreshDeviceState();
    }

    [RelayCommand(CanExecute = nameof(CanToggleMeasurement))]
    private void ToggleMeasurement()
    {
        IsMeasurementRunning = !IsMeasurementRunning;
        RecordingIcon = IsMeasurementRunning ? StopIcon : StartIcon;

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
    private void Autoscale()
    {
        MeasurementSettings.Autoscale();
    }

    [RelayCommand(CanExecute = nameof(CanUseChartTools))]
    private Task ShowCrosshairs()
    {
        return ShowNotImplementedAsync(AppResources.App_CrossHairs);
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

    private static Task ShowNotImplementedAsync(string feature)
    {
        Page? page = GetCurrentPage();

        if (page is null)
        {
            return Task.CompletedTask;
        }

        return page.DisplayAlertAsync(feature, "not implemented yet", AppResources.Dialog_Ok);
    }

    /// <summary>
    /// Resolves the topmost currently presented page (modal dialog if one is pushed, otherwise the
    /// root page). Application.Current.Windows[0].Page alone always returns the root page, so an
    /// alert raised while a modal dialog is open would be requested on a page that isn't the one
    /// actually visible to the user.
    /// </summary>
    private static Page? GetCurrentPage()
    {
        Page? root = Application.Current?.Windows.FirstOrDefault()?.Page;

        if (root is null)
        {
            return null;
        }

        IReadOnlyList<Page>? modalStack = root.Navigation?.ModalStack;

        return modalStack is { Count: > 0 } ? modalStack[^1] : root;
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

        public void Autoscale()
        {

        }
    }
}