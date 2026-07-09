using System.Collections.ObjectModel;
using App.Models;
using App.Resources.Strings;
using Backend.Devices.GoDirect;
using Backend.Measurements;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisMeasurementViewModel :
    ObservableObject,
    IDisposable,
    IMeasurementWorkflow
{
    private readonly ISpectrometer _spectrometer;
    private readonly Func<bool> _isMeasurementRunningProvider;

    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(100);
    private readonly Lock _uiUpdateLock = new();
    private Spectrum? _latestSpectrumForUi;
    private bool _uiUpdateScheduled;
    private DateTimeOffset _latestUiUpdateAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public SpectroVisMeasurementViewModel(ISpectrometer spectrometer, Func<bool> isMeasurementRunningProvider)
    {
        _spectrometer = spectrometer ?? throw new ArgumentNullException(nameof(spectrometer));
        _isMeasurementRunningProvider = isMeasurementRunningProvider ?? throw new ArgumentNullException(nameof(isMeasurementRunningProvider));

        IntegrationTimeMs = _spectrometer.Session.IntegrationTime;

        _spectrometer.Session.CurrentSpectrumChanged += OnCurrentSpectrumChanged;
        _spectrometer.Session.StateChanged += OnSessionStateChanged;

        RebuildOperatingModeOptions();
        RefreshAll();
    }

    public SpectrometerSession Session => _spectrometer.Session;

    public ObservableCollection<TableRow> TableRows { get; } = [];

    public ObservableCollection<SpectroVisOperatingModeOption> OperatingModeOptions { get; } = [];

    public bool HasOperatingModeSelection => true;

    public bool HasZeroCommand => false;

    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task>? OperatingModeDialogRequested;

    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task>? AcquisitionModeDialogRequested;

    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task<CalibrationDialogResult?>>? CalibrationDialogRequested;

    [ObservableProperty]
    public partial AcquisitionMode AcquisitionMode { get; set; } = AcquisitionMode.FullSpectrum;

    [ObservableProperty]
    public partial ConcentrationUnits ConcentrationUnits { get; set; } = ConcentrationUnits.MolPerLiter;

    [ObservableProperty]
    public partial string ChartTitle { get; set; } = "";

    [ObservableProperty]
    public partial string XAxisTitle { get; set; } = "";

    [ObservableProperty]
    public partial string YAxisTitle { get; set; } = "";

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
    public partial Color InitializationStatusColor { get; set; } = Colors.Gray;

    [ObservableProperty]
    public partial Color WhiteLampStatusColor { get; set; } = Colors.Gray;

    [ObservableProperty]
    public partial Color CalibrationStatusColor { get; set; } = Colors.Gray;

    [ObservableProperty]
    public partial int IntegrationTimeMs { get; set; }

    [ObservableProperty]
    public partial bool CanEditIntegrationTime { get; set; }

    [ObservableProperty]
    public partial string XColumnHeader { get; set; } = "λ\n[nm]";

    [ObservableProperty]
    public partial string YColumnHeader { get; set; } = "ADC\n[counts]";

    [ObservableProperty]
    public partial Spectrum? DisplayedSpectrum { get; set; }
    private bool IsMeasurementRunning => _isMeasurementRunningProvider();

    partial void OnAcquisitionModeChanged(AcquisitionMode value)
    {
        RefreshChartConfiguration();
        RefreshTableHeaders();

        if (DisplayedSpectrum is not null)
        {
            UpdateFullSpectrumTable(DisplayedSpectrum);
        }
    }

    public async Task OpenOperatingMode(CancellationToken ct = default)
    {
        if (OperatingModeDialogRequested is null)
        {
            throw new InvalidOperationException("No SpectroVis operating mode dialog is registered.");
        }

        await OperatingModeDialogRequested(this, ct);
    }

    public async Task OpenAcquisitionMode(CancellationToken ct = default)
    {
        if (AcquisitionModeDialogRequested is null)
        {
            throw new InvalidOperationException("No SpectroVis acquisition mode dialog is registered.");
        }

        await AcquisitionModeDialogRequested(this, ct);
    }

    public async Task<CalibrationDialogResult?> ShowCalibrationDialog(CancellationToken ct = default)
    {
        if (CalibrationDialogRequested is null)
        {
            throw new InvalidOperationException("No SpectroVis calibration dialog is registered.");
        }

        return await CalibrationDialogRequested(this, ct);
    }

    public Task SetToZero(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public async Task SelectOperatingModeAsync(OperatingMode mode, CancellationToken ct = default)
    {
        SpectroVisOperatingModeOption? option =
            OperatingModeOptions.FirstOrDefault(item => item.Mode == mode);

        if (option is null)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown operating mode.");
        }

        if (!option.IsSupported)
        {
            return;
        }

        await _spectrometer.SetOperatingMode(mode, ct).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            RebuildOperatingModeOptions();
            RefreshAll();
        });
    }

    public void SelectAcquisitionMode(AcquisitionMode mode)
    {
        AcquisitionMode = mode;
    }

    public async Task ApplyIntegrationTimeAsync(int integrationTimeMs, CancellationToken ct = default)
    {
        if (!CanEditIntegrationTime)
        {
            return;
        }

        await _spectrometer.SetIntegrationTime(integrationTimeMs, ct).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IntegrationTimeMs = _spectrometer.Session.IntegrationTime;
            RefreshAll();
        });
    }

    public void RefreshAll()
    {
        IntegrationTimeMs = _spectrometer.Session.IntegrationTime;
        CanEditIntegrationTime = CanEditIntegrationTimeForCurrentMode();

        RefreshChartConfiguration();
        RefreshTableHeaders();
        RefreshStatusIndicators();

        if (DisplayedSpectrum is not null)
        {
            UpdateFullSpectrumTable(DisplayedSpectrum);
        }
    }

    private void OnCurrentSpectrumChanged(Spectrum spectrum)
    {
        if (!_isMeasurementRunningProvider())
        {
            return;
        }

        lock (_uiUpdateLock)
        {
            _latestSpectrumForUi = spectrum;

            if (_uiUpdateScheduled)
            {
                return;
            }

            _uiUpdateScheduled = true;
        }

        MainThread.BeginInvokeOnMainThread(UpdateSpectrumOnUiThread);
    }

    private async void UpdateSpectrumOnUiThread()
    {
        try
        {
            TimeSpan sinceLastUpdate = DateTimeOffset.UtcNow - _latestUiUpdateAt;

            if (sinceLastUpdate < UiUpdateInterval)
            {
                await Task.Delay(UiUpdateInterval - sinceLastUpdate);
            }

            Spectrum? spectrum;

            lock (_uiUpdateLock)
            {
                spectrum = _latestSpectrumForUi;
                _latestSpectrumForUi = null;
                _uiUpdateScheduled = false;
            }

            if (spectrum is null || !_isMeasurementRunningProvider())
            {
                return;
            }

            _latestUiUpdateAt = DateTimeOffset.UtcNow;

            DisplayedSpectrum = spectrum;
            UpdateFullSpectrumTable(spectrum);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void OnSessionStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(RefreshAll);
    }

    private void RefreshChartConfiguration()
    {
        switch (AcquisitionMode)
        {
            case AcquisitionMode.FullSpectrum:
                ConfigureFullSpectrumChart();
                break;

            case AcquisitionMode.TimeResolved:
                ConfigureTimeBasedChart();
                break;

            case AcquisitionMode.EventTriggered:
                ConfigureEventBasedChart();
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

    private void ConfigureTimeBasedChart()
    {
        ChartTitle = AppResources.Device_TimeResolved;

        XAxisTitle = AppResources.App_TimeAxis;
        XMinimum = 0;
        XMaximum = 60;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = false;
    }

    private void ConfigureEventBasedChart()
    {
        ChartTitle = AppResources.Device_EventTriggered;

        XAxisTitle = AppResources.Spectrometer_ConcentrationAxis;
        XMinimum = 0;
        XMaximum = 10;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = false;
    }

    private void RefreshTableHeaders()
    {
        switch (AcquisitionMode)
        {
            case AcquisitionMode.FullSpectrum:
                XColumnHeader = "λ [nm]";
                break;
            case AcquisitionMode.TimeResolved:
                XColumnHeader = "t [s]";
                break;
            case AcquisitionMode.EventTriggered:
                XColumnHeader = ConcentrationUnits switch
                {
                    ConcentrationUnits.MolPerLiter => "c [mol/l]",
                    ConcentrationUnits.MilliMolPerLiter => "c [mmol/l]",
                    ConcentrationUnits.MicroMolPerLiter => "c [µmol/l]",
                    ConcentrationUnits.GramsPerLiter => "c [g/l]",
                    ConcentrationUnits.MilliGramsPerLiter => "c [mg/l]",
                    ConcentrationUnits.MilliGramsPerMilliLiter => "c [mg/ml]",
                    _ => "c [mol]",
                };
                break;
            default:
                XColumnHeader = "x";
                break;
        }

        YColumnHeader = Session.Mode switch
        {
            OperatingMode.RawCounts => "ADC [counts]",
            OperatingMode.Intensity => "I [rel.]",
            OperatingMode.Transmission => "T [%]",
            OperatingMode.Absorbance => "A",
            OperatingMode.Fluorescence405 => "I [rel.]",
            OperatingMode.Fluorescence500 => "I [rel.]",
            _ => "y"
        };
    }

    private void RefreshStatusIndicators()
    {
        InitializationStatusColor = _spectrometer.IsInitialized ? Colors.LimeGreen : Colors.Red;

        RefreshWhiteLampStatus();

        CalibrationStatusColor = _spectrometer.IsCalibrated ? Colors.LimeGreen : Colors.Red;
    }

    private void RefreshWhiteLampStatus()
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

        WhiteLampStatusColor = Colors.Red;
    }

    private void RebuildOperatingModeOptions()
    {
        OperatingModeOptions.Clear();

        AddOperatingModeOption(OperatingMode.RawCounts, AppResources.OperatingMode_RawCounts, isSupported: true);

        AddOperatingModeOption(OperatingMode.Intensity, AppResources.OperatingMode_Emission, isSupported: true);

        AddOperatingModeOption(OperatingMode.Transmission, AppResources.OperatingMode_Transmittance, isSupported: _spectrometer.Model.HasWhiteLamp);

        AddOperatingModeOption(OperatingMode.Absorbance, AppResources.OperatingMode_Absorbance, isSupported: _spectrometer.Model.HasWhiteLamp);

        AddOperatingModeOption(OperatingMode.Fluorescence405, AppResources.OperatingMode_Fluorescence405, isSupported: _spectrometer.Model.HasLed405);

        AddOperatingModeOption(OperatingMode.Fluorescence500, AppResources.OperatingMode_Fluorescence500, isSupported: _spectrometer.Model.HasLed500);
    }

    private void AddOperatingModeOption(OperatingMode mode, string displayName, bool isSupported)
    {
        OperatingModeOptions.Add(new SpectroVisOperatingModeOption(
            mode,
            displayName,
            isSupported,
            IsSelected: Session.Mode == mode));
    }

    private void UpdateFullSpectrumTable(Spectrum spectrum)
    {
        int count = Math.Min(spectrum.WavelengthNm.Length, spectrum.YAxis.Length);

        if (TableRows.Count != count)
        {
            TableRows.Clear();

            for (int i = 0; i < count; i++)
            {
                TableRows.Add(new TableRow(FormatXValue(spectrum.WavelengthNm[i]), FormatYValue(spectrum.YAxis[i], spectrum.Mode)));
            }

            return;
        }

        for (int i = 0; i < count; i++)
        {
            TableRows[i].XValue = FormatXValue(spectrum.WavelengthNm[i]);
            TableRows[i].YValue = FormatYValue(spectrum.YAxis[i], spectrum.Mode);
        }
    }

    private bool CanEditIntegrationTimeForCurrentMode()
    {
        return !IsMeasurementRunning && Session.Mode is not OperatingMode.Absorbance
            and not OperatingMode.Transmission;
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
            _ => (0, 1)
        };
    }

    private static string FormatXValue(double value)
    {
        return value.ToString("F1");
    }

    private static string FormatYValue(double value, OperatingMode mode)
    {
        return mode switch
        {
            OperatingMode.RawCounts => value.ToString("F0"),
            OperatingMode.Transmission => value.ToString("F1"),
            OperatingMode.Absorbance => value.ToString("F3"),
            OperatingMode.Intensity => value.ToString("F4"),
            OperatingMode.Fluorescence405 => value.ToString("F4"),
            OperatingMode.Fluorescence500 => value.ToString("F4"),
            _ => value.ToString("G4")
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _spectrometer.Session.CurrentSpectrumChanged -= OnCurrentSpectrumChanged;
        _spectrometer.Session.StateChanged -= OnSessionStateChanged;
    }
}

public sealed record SpectroVisOperatingModeOption(
    OperatingMode Mode,
    string DisplayName,
    bool IsSupported,
    bool IsSelected);