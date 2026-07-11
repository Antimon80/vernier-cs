using System.Collections.ObjectModel;
using App.Models;
using App.Resources.Strings;
using Backend.Devices.GoDirect;
using Backend.Measurements;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisMeasurementViewModel : ObservableObject, IDisposable, IMeasurementWorkflow
{
    private readonly ISpectrometer _spectrometer;
    private readonly Func<bool> _isMeasurementRunningProvider;

    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(100);
    private readonly Lock _uiUpdateLock = new();
    private Spectrum? _latestSpectrumForUi;
    private bool _uiUpdateScheduled;
    private DateTimeOffset _latestUiUpdateAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _acquisitionStartedAt;
    private const int MaxArchivedSeries = 10;
    private static readonly Color[] SeriesColors = [Colors.Green, Colors.Red, Colors.Blue, Colors.DarkOrange, Colors.Purple, Colors.Teal];
    private string _liveXHeader = "";
    private string _liveYHeader = "";
    private bool _disposed;

    public SpectroVisMeasurementViewModel(ISpectrometer spectrometer, Func<bool> isMeasurementRunningProvider)
    {
        _spectrometer = spectrometer ?? throw new ArgumentNullException(nameof(spectrometer));
        _isMeasurementRunningProvider = isMeasurementRunningProvider ?? throw new ArgumentNullException(nameof(isMeasurementRunningProvider));

        IntegrationTimeMs = _spectrometer.Session.IntegrationTime;

        _spectrometer.Session.CurrentSpectrumChanged += OnCurrentSpectrumChanged;
        _spectrometer.Session.StateChanged += OnSessionStateChanged;

        BuildOperatingModeOptions();
        RefreshAll();
    }

    public SpectrometerSession Session => _spectrometer.Session;
    public SpectrometerModel Model => _spectrometer.Model;

    public ObservableCollection<TableColumn> Columns { get; } = [];
    public ObservableCollection<WideTableRow> WideRows { get; } = [];
    public ObservableCollection<MeasurementSeries> ArchivedSeries { get; } = [];
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
    public partial double SelectedWavelengthNm { get; set; } = 500.0;

    [ObservableProperty]
    public partial bool ShowSpectrumStrip { get; set; }

    [ObservableProperty]
    public partial Color InitializationStatusColor { get; set; } = Colors.Gray;

    [ObservableProperty]
    public partial Color WhiteLampStatusColor { get; set; } = Colors.Gray;

    [ObservableProperty]
    public partial Color CalibrationStatusColor { get; set; } = Colors.Gray;

    [ObservableProperty]
    public partial string CurrentOperatingMode { get; set; } = "";

    [ObservableProperty]
    public partial int IntegrationTimeMs { get; set; }

    [ObservableProperty]
    public partial bool CanEditIntegrationTime { get; set; }

    [ObservableProperty]
    public partial Spectrum? DisplayedSpectrum { get; set; }
    private bool IsMeasurementRunning => _isMeasurementRunningProvider();

    partial void OnAcquisitionModeChanged(AcquisitionMode value)
    {
        ArchiveLiveSeries();

        RefreshChartConfiguration();
        RefreshTableHeaders();

        if (DisplayedSpectrum is not null && value == AcquisitionMode.FullSpectrum)
        {
            UpdateTable(DisplayedSpectrum);
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
            OperatingModeOptions.FirstOrDefault(item => item.Mode == mode) ?? throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown operating mode.");

        if (!option.IsSupported)
        {
            return;
        }

        ArchiveLiveSeries();
        await _spectrometer.SetOperatingMode(mode, ct).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            RefreshOperatingModeSelection();
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

    public void CaptureEventPoint(double concentration)
    {
        if (DisplayedSpectrum is null)
        {
            return;
        }

        double y = GetYValueAtSelectedWavelength(DisplayedSpectrum);

        AppendLiveRow(FormatConcentration(concentration), FormatYValue(y, DisplayedSpectrum.Mode));
    }

    public void RefreshAll()
    {
        IntegrationTimeMs = _spectrometer.Session.IntegrationTime;
        CanEditIntegrationTime = CanEditIntegrationTimeForCurrentMode();

        RefreshChartConfiguration();
        RefreshTableHeaders();
        RefreshCurrentOperatingMode();
        RefreshStatusIndicators();

        if (DisplayedSpectrum is not null && AcquisitionMode == AcquisitionMode.FullSpectrum)
        {
            UpdateFullSpectrumTable(DisplayedSpectrum);
        }
    }

    public void ArchiveLiveSeries()
    {
        if (WideRows.Count == 0)
        {
            return;
        }

        List<TableRow> rows = new(WideRows.Count);

        foreach (WideTableRow row in WideRows)
        {
            rows.Add(new TableRow(row.Cells[0].Value ?? "", row.Cells[1].Value ?? ""));
        }

        ArchivedSeries.Add(new MeasurementSeries(
            Guid.NewGuid(), Session.Mode, AcquisitionMode, DateTimeOffset.Now,
            _liveXHeader, _liveYHeader, rows
        ));

        if (ArchivedSeries.Count > MaxArchivedSeries)
        {
            ArchivedSeries.RemoveAt(0);
        }

        WideRows.Clear();
        _acquisitionStartedAt = null;
        RebuildColumns();
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
            UpdateTable(spectrum);
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

    private void RebuildColumns()
    {
        Columns.Clear();
        Columns.Add(new TableColumn(_liveXHeader, Colors.Black));
        Columns.Add(new TableColumn(_liveYHeader, SeriesColors[0]));

        for (int i = 0; i < ArchivedSeries.Count; i++)
        {
            MeasurementSeries series = ArchivedSeries[i];
            Color color = SeriesColors[(i + 1) % SeriesColors.Length];

            Columns.Add(new TableColumn(series.XColumnHeader, Colors.Black));
            Columns.Add(new TableColumn(series.YColumnHeader, color));
        }

        int columnCount = Columns.Count;

        foreach (WideTableRow row in WideRows)
        {
            while (row.Cells.Count < columnCount)
            {
                row.Cells.Add(new TableCell { Color = Columns[row.Cells.Count].Color });
            }

            while (row.Cells.Count > columnCount)
            {
                row.Cells.RemoveAt(row.Cells.Count - 1);
            }
        }

        WriteArchivedCells();
    }

    private void EnsureRowCount(int minimumCount)
    {
        while (WideRows.Count < minimumCount)
        {
            WideTableRow row = new();

            for (int i = 0; i < Columns.Count; i++)
            {
                row.Cells.Add(new TableCell { Color = Columns[i].Color });
            }

            WideRows.Add(row);
        }
    }

    private void WriteArchivedCells()
    {
        for (int s = 0; s < ArchivedSeries.Count; s++)
        {
            MeasurementSeries series = ArchivedSeries[s];
            int xColumn = 2 + s * 2;
            int yColumn = xColumn + 1;

            EnsureRowCount(series.Rows.Count);

            for (int r = 0; r < series.Rows.Count; r++)
            {
                WideRows[r].Cells[xColumn].Value = series.Rows[r].XValue;
                WideRows[r].Cells[yColumn].Value = series.Rows[r].YValue;
            }
        }
    }

    private void RefreshTableHeaders()
    {
        switch (AcquisitionMode)
        {
            case AcquisitionMode.FullSpectrum:
                _liveXHeader = "λ [nm]";
                break;
            case AcquisitionMode.TimeResolved:
                _liveXHeader = "t [s]";
                break;
            case AcquisitionMode.EventTriggered:
                _liveXHeader = ConcentrationUnits switch
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
                _liveXHeader = "x";
                break;
        }

        _liveYHeader = Session.Mode switch
        {
            OperatingMode.RawCounts => "ADC [counts]",
            OperatingMode.Intensity => "I [rel.]",
            OperatingMode.Transmission => "T [%]",
            OperatingMode.Absorbance => "A",
            OperatingMode.Fluorescence405 => "I [rel.]",
            OperatingMode.Fluorescence500 => "I [rel.]",
            _ => "y"
        };

        RebuildColumns();
    }

    private void UpdateTable(Spectrum spectrum)
    {
        switch (AcquisitionMode)
        {
            case AcquisitionMode.FullSpectrum:
                UpdateFullSpectrumTable(spectrum);
                break;
            case AcquisitionMode.TimeResolved:
                AppendTimeResolvedTableRow(spectrum);
                break;
            case AcquisitionMode.EventTriggered:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(AcquisitionMode), AcquisitionMode, "Unknown acquisition mode.");
        }
    }

    private void UpdateFullSpectrumTable(Spectrum spectrum)
    {
        int count = Math.Min(spectrum.WavelengthNm.Length, spectrum.YAxis.Length);
        EnsureRowCount(count);

        for (int i = 0; i < count; i++)
        {
            WideRows[i].Cells[0].Value = FormatXValue(spectrum.WavelengthNm[i]);
            WideRows[i].Cells[1].Value = FormatYValue(spectrum.YAxis[i], spectrum.Mode);
        }
    }

    private void AppendLiveRow(string xValue, string yValue)
    {
        int rowIndex = WideRows.Count;
        EnsureRowCount(rowIndex + 1);

        WideRows[rowIndex].Cells[0].Value = xValue;
        WideRows[rowIndex].Cells[1].Value = yValue;
    }

    private void AppendTimeResolvedTableRow(Spectrum spectrum)
    {
        _acquisitionStartedAt ??= DateTimeOffset.UtcNow;
        double elapsedSeconds = (DateTimeOffset.UtcNow - _acquisitionStartedAt.Value).TotalSeconds;
        double y = GetYValueAtSelectedWavelength(spectrum);

        AppendLiveRow(elapsedSeconds.ToString("F2"), FormatYValue(y, spectrum.Mode));
    }

    private double GetYValueAtSelectedWavelength(Spectrum spectrum)
    {
        int count = Math.Min(spectrum.WavelengthNm.Length, spectrum.YAxis.Length);

        if (count == 0)
        {
            return double.NaN;
        }

        int bestIndex = 0;
        double bestDistance = Math.Abs(spectrum.WavelengthNm[0] - SelectedWavelengthNm);

        for (int i = 0; i < count; i++)
        {
            double distance = Math.Abs(spectrum.WavelengthNm[i] - SelectedWavelengthNm);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return spectrum.YAxis[bestIndex];
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

        if (Session.WhiteLampCheckPassed != true)
        {
            WhiteLampStatusColor = Colors.Red;
            return;
        }

        if (!Session.WhiteLampIsOn)
        {
            WhiteLampStatusColor = Colors.Orange;
            return;
        }

        WhiteLampStatusColor = Session.IsWhiteLampWarmedUp ? Colors.LimeGreen : Colors.Orange;
    }

    private void RefreshCurrentOperatingMode()
    {
        SpectroVisOperatingModeOption? option = OperatingModeOptions.FirstOrDefault(item => item.Mode == Session.Mode);

        CurrentOperatingMode = option?.DisplayName ?? Session.Mode.ToString();
    }

    private void BuildOperatingModeOptions()
    {

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
            isSelected: Session.Mode == mode));
    }

    private void RefreshOperatingModeSelection()
    {
        foreach (SpectroVisOperatingModeOption option in OperatingModeOptions)
        {
            option.IsSelected = option.Mode == Session.Mode;
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

    private string FormatConcentration(double value)
    {
        return ConcentrationUnits switch
        {
            ConcentrationUnits.MolPerLiter => value.ToString("G4"),
            ConcentrationUnits.MilliMolPerLiter => value.ToString("G4"),
            ConcentrationUnits.MicroMolPerLiter => value.ToString("G4"),
            ConcentrationUnits.GramsPerLiter => value.ToString("G4"),
            ConcentrationUnits.MilliGramsPerLiter => value.ToString("G4"),
            ConcentrationUnits.MilliGramsPerMilliLiter => value.ToString("G4"),
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