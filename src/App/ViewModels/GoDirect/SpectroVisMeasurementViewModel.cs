using System.Collections.ObjectModel;
using App.Models;
using Backend.Devices.GoDirect;
using Backend.Measurements;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.ApplicationModel;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisMeasurementViewModel :
    ObservableObject,
    IDisposable,
    IMeasurementWorkflow
{
    private readonly ISpectrometer _spectrometer;
    private readonly Func<bool> _isMeasurementRunningProvider;
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

    public ObservableCollection<SpectroVisTableRow> TableRows { get; } = [];

    public ObservableCollection<SpectroVisOperatingModeOption> OperatingModeOptions { get; } = [];

    public bool HasOperatingModeSelection => true;

    public bool HasZeroCommand => false;

    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task>? OperatingModeDialogRequested;

    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task>? AcquisitionModeDialogRequested;

    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task<CalibrationDialogResult?>>? CalibrationDialogRequested;

    [ObservableProperty]
    public partial AcquisitionMode AcquisitionMode { get; set; } = AcquisitionMode.FullSpectrum;

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
            RebuildTable(DisplayedSpectrum);
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

        int clamped = Math.Clamp(integrationTimeMs, 1, 1000);

        await _spectrometer.SetIntegrationTime(clamped, ct).ConfigureAwait(false);

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
            RebuildTable(DisplayedSpectrum);
        }
    }

    private void OnCurrentSpectrumChanged(Spectrum _)
    {
        if (!_isMeasurementRunningProvider())
        {
            return;
        }

        Spectrum? spectrum = _spectrometer.Session.CurrentSpectrum;

        if (spectrum is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            DisplayedSpectrum = spectrum;
            RebuildTable(spectrum);
        });
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
        ChartTitle = "Vollspektrum";

        XAxisTitle = "Wellenlänge [nm]";
        XMinimum = _spectrometer.Model.WavelengthMinNm;
        XMaximum = _spectrometer.Model.WavelengthMaxNm;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = true;
    }

    private void ConfigureTimeBasedChart()
    {
        ChartTitle = "Zeitaufgelöste Messung";

        XAxisTitle = "Zeit [s]";
        XMinimum = 0;
        XMaximum = 60;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = false;
    }

    private void ConfigureEventBasedChart()
    {
        ChartTitle = "Ereignisgesteuerte Messung";

        XAxisTitle = "Messung";
        XMinimum = 0;
        XMaximum = 10;

        YAxisTitle = GetYAxisTitle(Session.Mode);
        (YMinimum, YMaximum) = GetYAxisRange(Session.Mode);

        ShowSpectrumStrip = false;
    }

    private void RefreshTableHeaders()
    {
        XColumnHeader = AcquisitionMode switch
        {
            AcquisitionMode.FullSpectrum => "λ\n[nm]",
            AcquisitionMode.TimeResolved => "t\n[s]",
            AcquisitionMode.EventTriggered => "Messung",
            _ => "x"
        };

        YColumnHeader = Session.Mode switch
        {
            OperatingMode.RawCounts => "ADC\n[counts]",
            OperatingMode.Intensity => "Intensität\n[rel.]",
            OperatingMode.Transmission => "T\n[%]",
            OperatingMode.Absorbance => "A",
            OperatingMode.Fluorescence405 => "Intensität\n[rel.]",
            OperatingMode.Fluorescence500 => "Intensität\n[rel.]",
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

        AddOperatingModeOption(
            OperatingMode.RawCounts,
            "Unkalibrierte Messwerte",
            isSupported: true);

        AddOperatingModeOption(
            OperatingMode.Intensity,
            "Intensität",
            isSupported: true);

        AddOperatingModeOption(
            OperatingMode.Transmission,
            "Transmission",
            isSupported: _spectrometer.Model.HasWhiteLamp);

        AddOperatingModeOption(
            OperatingMode.Absorbance,
            "Absorbanz",
            isSupported: _spectrometer.Model.HasWhiteLamp);

        AddOperatingModeOption(
            OperatingMode.Fluorescence405,
            "Fluoreszenz 405 nm",
            isSupported: _spectrometer.Model.HasLed405);

        AddOperatingModeOption(
            OperatingMode.Fluorescence500,
            "Fluoreszenz 500 nm",
            isSupported: _spectrometer.Model.HasLed500);
    }

    private void AddOperatingModeOption(
        OperatingMode mode,
        string displayName,
        bool isSupported)
    {
        OperatingModeOptions.Add(new SpectroVisOperatingModeOption(
            mode,
            displayName,
            isSupported,
            IsSelected: Session.Mode == mode));
    }

    private void RebuildTable(Spectrum spectrum)
    {
        TableRows.Clear();

        if (AcquisitionMode != AcquisitionMode.FullSpectrum)
        {
            return;
        }

        int count = Math.Min(spectrum.WavelengthNm.Length, spectrum.YAxis.Length);

        for (int i = 0; i < count; i++)
        {
            TableRows.Add(new SpectroVisTableRow(
                FormatXValue(spectrum.WavelengthNm[i]),
                FormatYValue(spectrum.YAxis[i], spectrum.Mode)));
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
            OperatingMode.RawCounts => "ADC counts [counts]",
            OperatingMode.Intensity => "Intensität [rel.]",
            OperatingMode.Transmission => "Transmission [%]",
            OperatingMode.Absorbance => "Absorbanz",
            OperatingMode.Fluorescence405 => "Intensität [rel.]",
            OperatingMode.Fluorescence500 => "Intensität [rel.]",
            _ => "Messwert"
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

public sealed record SpectroVisTableRow(
    string XValue,
    string YValue);