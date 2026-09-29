using App.Resources.Strings;
using App.Util;
using Backend.Devices.GoDirect;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisCalibrationViewModel : ObservableObject, IDisposable
{
    private readonly SpectroVisMeasurementViewModel _measurementViewModel;
    private readonly TaskCompletionSource<CalibrationDialogResult?> _resultTcs = new();
    private bool _disposed;

    public SpectroVisCalibrationViewModel(SpectroVisMeasurementViewModel measurementViewModel)
    {
        _measurementViewModel = measurementViewModel ?? throw new ArgumentNullException(nameof(measurementViewModel));

        _measurementViewModel.Session.StateChanged += OnSessionStateChanged;
        RefreshWarmupState();

        SpectrometerType = _measurementViewModel.Model.Name;
        MeasurementRangeText = $"{_measurementViewModel.Model.WavelengthMinNm:F1} - {_measurementViewModel.Model.WavelengthMaxNm:F1} nm";
    }

    /// <summary>
    /// Completes when the calibration dialog has produced a result.
    /// </summary>
    public Task<CalibrationDialogResult?> ResultTask => _resultTcs.Task;

    /// <summary>
    /// Remaining white-lamp warmup time in seconds.
    /// </summary>
    [ObservableProperty]
    public partial int WarmupRemainingSeconds { get; set; }

    /// <summary>
    /// Indicates whether the white lamp has completed its required warmup period.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FinishCalibrationCommand))]
    [NotifyPropertyChangedFor(nameof(CanSkipWarmup))]
    public partial bool IsWarmedUp { get; set; }

    /// <summary>
    /// Indicates whether the user has chosen to skip the white-lamp warmup.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FinishCalibrationCommand))]
    public partial bool SkipWarmupSelected { get; set; }

    /// <summary>
    /// True while the "Skip warmup" button should still be clickable. Once warmup has
    /// completed there is nothing left to skip, so the button is greyed out.
    /// </summary>
    public bool CanSkipWarmup => !IsWarmedUp;

    [ObservableProperty]
    public partial string SpectrometerType { get; set; } = "";

    [ObservableProperty]
    public partial string MeasurementRangeText { get; set; } = "";

    /// <summary>
    /// True while the actual Calibrate() call triggered by "finish calibration" is running.
    /// Used to disable the button against double-clicks while the device is busy.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FinishCalibrationCommand))]
    public partial bool IsCalibrating { get; set; }

    /// <summary>
    /// True once the in-dialog calibration has completed successfully.
    /// The OK button binds to this so the dialog can only be confirmed after
    /// calibration has actually happened, not just because warmup is done.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FinishCalibrationCommand))]
    public partial bool IsCalibrationComplete { get; set; }

    public void SetResult(CalibrationDialogResult? result) => _resultTcs.TrySetResult(result);

    /// <summary>
    /// Updates the warmup state when the spectrometer session changes.
    /// </summary>
    private void OnSessionStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(RefreshWarmupState);
    }

    /// <summary>
    /// Refreshes the warmup information from the current spectrometer session.
    /// </summary>
    private void RefreshWarmupState()
    {
        SpectrometerSession session = _measurementViewModel.Session;

        WarmupRemainingSeconds = (int)Math.Ceiling(session.WhiteLampWarmupRemaining.TotalSeconds);
        IsWarmedUp = session.IsWhiteLampWarmedUp;
    }

    /// <summary>
    /// Performs the spectrometer calibration once the warmup requirements have been met
    /// or the user has explicitly chosen to skip the warmup.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFinishCalibration))]
    private async Task OnFinishCalibration()
    {
        IsCalibrating = true;

        try
        {
            await _measurementViewModel.Spectrometer.Calibrate(SkipWarmupSelected);

            IsCalibrationComplete = true;
            _measurementViewModel.Refresh();
        }
        catch (Exception ex)
        {
            await ViewModelHelpers.ShowErrorAsync(ex);
        }
        finally
        {
            IsCalibrating = false;
        }
    }

    /// <summary>
    /// Opens the calibration help dialog.
    /// </summary>
    [RelayCommand]
    private Task OpenHelp()
    {
        return ViewModelHelpers.ShowNotImplementedAsync(AppResources.App_Help);
    }

    /// <summary>
    /// Determines whether calibration can currently be started.
    /// </summary>
    private bool CanFinishCalibration()
    {
        return (IsWarmedUp || SkipWarmupSelected) && !IsCalibrating && !IsCalibrationComplete;
    }

    /// <summary>
    /// Unsubscribes from session events when the view model is no longer used.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _measurementViewModel.Session.StateChanged -= OnSessionStateChanged;
    }
}