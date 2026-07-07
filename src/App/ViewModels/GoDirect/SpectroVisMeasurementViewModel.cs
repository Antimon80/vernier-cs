using App.Models;
using App.Resources.Strings;
using Backend.Devices.GoDirect;
using Backend.Measurements;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisMeasurementViewModel : ObservableObject, IDisposable
{
    private readonly ISpectrometer _spectrometer;
    private bool _dispoded;

    [ObservableProperty]
    public partial AcquisitionMode AcquisitionMode { get; set; } = AcquisitionMode.FullSpectrum;
    [ObservableProperty]
    public partial string ChartTitle { get; set; } = AppResources.Spectrometer_FullSpectrum;
    [ObservableProperty]
    public partial string XAxisTitle { get; set; } = AppResources.Spectrometer_Wavelength;
    [ObservableProperty]
    public partial string YAxisTitle { get; set; } = AppResources.Spectrometer_RawCounts;
    [ObservableProperty]
    public partial double XMinimum { get; set; }
    [ObservableProperty]
    public partial double XMaximum { get; set; }
    [ObservableProperty]
    public partial double YMinimum { get; set; }
    [ObservableProperty]
    public partial double YMaximum { get; set; }
    [ObservableProperty]
    public partial bool ShowSpectrumStrip { get; set; }
    [ObservableProperty]
    public partial bool IsRecording { get; set; }
    [ObservableProperty]
    public partial string RecordingIcon { get; set; } = "start_stop.png";
    [ObservableProperty]
    public partial Color InitializationStatusColor { get; set; } = Colors.Gray;
    [ObservableProperty]
    public partial Color WhiteLampStatusColor { get; set; } = Colors.Gray;
    [ObservableProperty]
    public partial Color CalibrationStatusColor { get; set; } = Colors.Gray;

    public SpectrometerSession Session => _spectrometer.Session;

    public SpectroVisMeasurementViewModel(ISpectrometer spectrometer)
    {
        _spectrometer = spectrometer ?? throw new ArgumentNullException(nameof(spectrometer));

        Session.CurrentSpectrumChanged += OnCurrentSpectrumChanged;

        UpdateChartConfiguration();
        UpdateStatusIndicators();

        if (Session.IsInitialized)
        {
            _spectrometer.StartStreaming();
        }
    }

    partial void OnAcquisitionModeChanged(AcquisitionMode value){
        UpdateChartConfiguration();
    }

    [RelayCommand]
    private Task OpenFileAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_OpenFile);
    }

    [RelayCommand]
    private Task SaveAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_SaveFile);
    }

    [RelayCommand]
    private Task SaveAsAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_SaveFileAs);
    }

    [RelayCommand]
    private Task PrintAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_Print);
    }

    [RelayCommand]
    private Task ExportAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_ExportData);
    }

    [RelayCommand]
    private Task ImportAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_ImportData);
    }

    [RelayCommand]
    private Task OperatingModeAsync()
    {
        return ShowNotImplementedAsync(AppResources.Spectrometer_OperatingMode);
    }

    [RelayCommand]
    private Task AcquisitionModeAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_AcquisitionMode);
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        if (!RequiresCalibration(Session.Mode))
        {
            await Shell.Current.DisplayAlertAsync(AppResources.Spectrometer_Calibrate, "calibration is required for mode 'absorbance' and 'transmittance'", AppResources.Dialog_Ok);

            return;
        }

        await Shell.Current.DisplayAlertAsync(AppResources.Spectrometer_Calibrate, "not implemented yet", AppResources.Dialog_Ok);
    }

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (!IsRecording)
        {
            if(RequiresCalibration(Session.Mode) && !Session.IsCalibrated)
            {
                await CalibrateAsync();
                return;
            }

            IsRecording = true;
            RecordingIcon = "stop.png";
            UpdateStatusIndicators();
            return;
        }

        IsRecording = false;
        RecordingIcon = "start_stop.png";

        // ToDo
    }

    [RelayCommand]
    private Task OpenCursorAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_CrossHairs);
    }

    [RelayCommand]
    private Task OpenDataManagerAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_DataManagement);
    }

    [RelayCommand]
    private Task OpenAnalysisAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_DataAnalysis);
    }

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_Settings);
    }

    [RelayCommand]
    private Task OpenHelpAsync()
    {
        return ShowNotImplementedAsync(AppResources.App_Help);
    }

    private void OnCurrentSpectrumChanged(Spectrum spectrum)
    {
        if (!IsRecording)
        {
            return;
        }

        // ToDo
    }

    private void UpdateChartConfiguration()
    {
        switch (AcquisitionMode)
        {
            case AcquisitionMode.FullSpectrum:
                ConfigureFullSpectrumChart();
                break;
            case AcquisitionMode.TimeResolved:
                ConfigureTimeResolvedChart();
                break;
            case AcquisitionMode.EventTriggered:
                ConfigureEventTriggeredChart();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(AcquisitionMode), AcquisitionMode, "Unknown acquisition mode.");
        }
    }

    private void ConfigureFullSpectrumChart()
    {
        ChartTitle = AppResources.Spectrometer_FullSpectrum;

        XAxisTitle = AppResources.Spectrometer_Wavelength;
        XMinimum = _spectrometer.Model.WavelengthMinNm;
        XMaximum = _spectrometer.Model.WavelengthMaxNm;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = true;
    }

    private void ConfigureTimeResolvedChart()
    {
        ChartTitle = AppResources.App_TimeResolved;

        XAxisTitle = AppResources.App_TimeAxis;
        XMinimum = 0;
        XMaximum = 60;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = false;
    }

    private void ConfigureEventTriggeredChart()
    {
        ChartTitle = AppResources.App_EventTriggered;

        XAxisTitle = AppResources.Spectrometer_ConcentrationAxis;
        XMinimum = 0;
        XMaximum = 10;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = false;
    }

    private void UpdateStatusIndicators()
    {
        InitializationStatusColor = Session.IsInitialized ? Colors.LimeGreen : Colors.Red;

        UpdateWhiteLampStatus();
        UpdateCalibrationStatus();
    }

    private void UpdateWhiteLampStatus()
    {
        if (Session.WhiteLampCheckPassed == false)
        {
            WhiteLampStatusColor = Colors.Red;
            return;
        }

        if (Session.WhiteLampCheckPassed == true && Session.IsWhiteLampWarmedUp)
        {
            WhiteLampStatusColor = Colors.LimeGreen;
            return;
        }

        if (Session.WhiteLampCheckPassed == true && Session.WhiteLampIsOn)
        {
            WhiteLampStatusColor = Colors.Orange;
            return;
        }

        WhiteLampStatusColor = Colors.Gray;
    }

    private void UpdateCalibrationStatus()
    {
        if (!RequiresCalibration(Session.Mode))
        {
            CalibrationStatusColor = Colors.Gray;
            return;
        }

        CalibrationStatusColor = Session.IsCalibrated ? Colors.LimeGreen : Colors.Red;
    }

    private static string GetYAxisTitle(OperatingMode mode)
    {
        return mode switch
        {
            OperatingMode.RawCounts => AppResources.Spectrometer_RawCounts,
            OperatingMode.Intensity => AppResources.Spectrometer_Intensity,
            OperatingMode.Transmission => AppResources.Spectrometer_Transmittance,
            OperatingMode.Absorbance => AppResources.Spectrometer_Absorbance,
            OperatingMode.Fluorescence405 => AppResources.Spectrometer_Intensity,
            OperatingMode.Fluorescence500 => AppResources.Spectrometer_Intensity,
            _ => AppResources.Spectrometer_RawCounts
        };
    }

    private static (double Minimum, double Maximum) GetYAxisRange(OperatingMode mode)
    {
        return mode switch
        {
            OperatingMode.RawCounts => (0, 65535),
            OperatingMode.Intensity => (0, 1),
            OperatingMode.Transmission => (0, 100),
            OperatingMode.Absorbance => (0, 3),
            OperatingMode.Fluorescence405 => (0, 1),
            OperatingMode.Fluorescence500 => (0, 1),
            _ => (0, 65535)
        };
    }

    private static bool RequiresCalibration(OperatingMode mode)
    {
        return mode is OperatingMode.Absorbance or OperatingMode.Transmission;
    }

    private static Task ShowNotImplementedAsync(string feature)
    {
        return Shell.Current.DisplayAlertAsync(feature, "not implemented yet", AppResources.Dialog_Ok);
    }

    public void Dispose()
    {
        if (_dispoded)
        {
            return;
        }

        _dispoded = true;
        Session.CurrentSpectrumChanged -= OnCurrentSpectrumChanged;
    }


}