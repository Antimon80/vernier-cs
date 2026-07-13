using System.Collections.ObjectModel;
using App.Models;
using App.Resources.Strings;
using Backend.Devices.GoDirect;
using Backend.Measurements;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.ViewModels.GoDirect;


/// <summary>
/// Provides the device-specific presentation and interaction logic for a SpectroVis measurement view.
///
/// The view model translates the current spectrometer session state into chart, table and status-bar data. 
/// It also coordinates operating-mode changes, integration-time updates, calibration dialogs and 
/// acquisition-mode-specific table handling.
/// </summary>
public sealed partial class SpectroVisMeasurementViewModel : ObservableObject, IDisposable, IMeasurementWorkflow
{
    /// <summary>
    /// Spectrometer controlled by this view model.
    /// </summary>
    private readonly ISpectrometer _spectrometer;

    /// <summary>
    /// Provides the current recording state maintained by the generic measurement view model.
    /// </summary>
    private readonly Func<bool> _isMeasurementRunningProvider;

    /// <summary>
    /// Minimum interval between two updates of spectrum-dependent UI elements.
    /// </summary>
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Protects spectrum data shared between the acquisition callback and the scheduled UI update.
    /// </summary>
    private readonly Lock _uiUpdateLock = new();

    /// <summary>
    /// Most recent spectrum waiting to be transferred to the UI thread.
    /// Older pending spectra are replaced so the UI always displays the newest available data.
    /// </summary>
    private Spectrum? _latestSpectrumForUi;

    /// <summary>
    /// Indicates whether an update has already been scheduled on the UI thread.
    /// </summary>
    private bool _uiUpdateScheduled;

    /// <summary>
    /// Timestamp of the most recent spectrum update applied to the UI.
    /// </summary>
    private DateTimeOffset _latestUiUpdateAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Start time used for calculating elapsed values in time-resolved mode.
    /// </summary>
    private DateTimeOffset? _acquisitionStartedAt;

    /// <summary>
    /// Maximum number of completed measurement series retained in the table.
    /// </summary>
    private const int MaxArchivedSeries = 10;

    /// <summary>
    /// Repeating color palette used for live and archived table series.
    /// </summary>
    private static readonly Color[] SeriesColors = [Colors.Green, Colors.Red, Colors.Blue, Colors.DarkOrange, Colors.Purple, Colors.Teal];

    /// <summary>
    /// Header of the current live series' x-value column.
    /// </summary>
    private string _liveXHeader = "";

    /// <summary>
    /// Header of the current live series' y-value column.
    /// </summary>
    private string _liveYHeader = "";

    /// <summary>
    /// Number of rows currently occupied by the live measurement series.
    /// </summary>
    private int _liveRowCount;

    /// <summary>
    /// Indicates whether event subscriptions have already been removed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes the view model from the current spectrometer session and subscribes to 
    /// spectrum and session-state updates.
    /// </summary>
    /// <param name="spectrometer">
    /// Spectrometer whose measurement state is presented and controlled.
    /// </param>
    /// <param name="isMeasurementRunningProvider">
    /// Callback that returns whether incoming spectra should currently be transferred to the display.
    /// </param>
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

    /// <summary>
    /// Gets the mutable measurement session maintained by the spectrometer.
    /// </summary>
    public SpectrometerSession Session => _spectrometer.Session;

    /// <summary>
    /// Gets the static hardware capabilities and wavelength range of the connected spectrometer.
    /// </summary>
    public SpectrometerModel Model => _spectrometer.Model;

    /// <summary>
    /// Gets the column definitions displayed by the wide measurement table.
    /// </summary>
    public ObservableCollection<TableColumn> Columns { get; } = [];

    /// <summary>
    /// Gets the rows containing the live series and all retained archived measurement series.
    /// </summary>
    public ObservableCollection<WideTableRow> WideRows { get; } = [];

    /// <summary>
    /// Gets previously completed measurement series retained for comparison.
    /// </summary>
    public ObservableCollection<MeasurementSeries> ArchivedSeries { get; } = [];

    /// <summary>
    /// Gets the operating modes presented by the operating-mode dialog, 
    /// including their support and selection state.
    /// </summary>
    public ObservableCollection<SpectroVisOperatingModeOption> OperatingModeOptions { get; } = [];

    /// <summary>
    /// Indicates that SpectroVis devices expose selectable operating modes.
    /// </summary>
    public bool HasOperatingModeSelection => true;

    /// <summary>
    /// Indicates that the spectrometer does not expose a generic zero command.
    /// </summary>
    public bool HasZeroCommand => false;

    /// <summary>
    /// Raised when the platform view should display the SpectroVis operating-mode dialog.
    /// </summary>
    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task>? OperatingModeDialogRequested;

    /// <summary>
    /// Raised when the platform view should display the acquisition-mode dialog.
    /// </summary>
    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task>? AcquisitionModeDialogRequested;

    /// <summary>
    /// Raised when the platform view should display the SpectroVis calibration
    /// dialog and return the selected calibration action.
    /// </summary>
    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task<CalibrationDialogResult?>>? CalibrationDialogRequested;

    /// <summary>
    /// Gets or sets how incoming full spectra are interpreted and accumulated by the user interface.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the wavelength sampled for time-resolved and event-triggered measurements.
    /// </summary>
    [ObservableProperty]
    public partial double SelectedWavelengthNm { get; set; } = 500.0;

    /// <summary>
    /// Gets or sets whether the wavelength color strip should be displayed below the chart.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the most recent spectrum accepted for display.
    /// </summary>
    [ObservableProperty]
    public partial Spectrum? DisplayedSpectrum { get; set; }

    /// <summary>
    /// Gets the current recording state from the generic measurement workflow.
    /// </summary>
    private bool IsMeasurementRunning => _isMeasurementRunningProvider();

    /// <summary>
    /// Archives the current live series and rebuilds chart and table metadata
    /// whenever the acquisition mode changes.
    /// </summary>
    /// <param name="value">New acquisition mode.</param>
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

    /// <summary>
    /// Requests display of the device-specific operating-mode dialog.
    /// </summary>
    /// <param name="ct">Cancellation token for the dialog operation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no dialog handler has been registered by the view.
    /// </exception>
    public async Task OpenOperatingMode(CancellationToken ct = default)
    {
        if (OperatingModeDialogRequested is null)
        {
            throw new InvalidOperationException("No SpectroVis operating mode dialog is registered.");
        }

        await OperatingModeDialogRequested(this, ct);
    }

    /// <summary>
    /// Requests display of the acquisition-mode dialog.
    /// </summary>
    /// <param name="ct">Cancellation token for the dialog operation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no dialog handler has been registered by the view.
    /// </exception>
    public async Task OpenAcquisitionMode(CancellationToken ct = default)
    {
        if (AcquisitionModeDialogRequested is null)
        {
            throw new InvalidOperationException("No SpectroVis acquisition mode dialog is registered.");
        }

        await AcquisitionModeDialogRequested(this, ct);
    }

    /// <summary>
    /// Requests display of the device-specific calibration dialog.
    /// </summary>
    /// <param name="ct">Cancellation token for the dialog operation.</param>
    /// <returns>
    /// The selected calibration action, or <see langword="null"/> if the dialog was dismissed without a result.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no dialog handler has been registered by the view.
    /// </exception>
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

    /// <summary>
    /// Changes the spectrometer operating mode after verifying that the connected model supports it.
    ///
    /// The current live series is archived before the backend mode is changed.
    /// The resulting session state and option selection are then refreshed on the UI thread.
    /// </summary>
    /// <param name="mode">Operating mode to activate.</param>
    /// <param name="ct">Cancellation token for the backend operation.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the requested mode is not present in the available mode collection.
    /// </exception>
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

    /// <summary>
    /// Applies a new integration time when editing is allowed for the current operating and recording state.
    /// </summary>
    /// <param name="integrationTimeMs">
    /// Requested integration time in milliseconds.
    /// </param>
    /// <param name="ct">Cancellation token for the backend operation.</param>
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

    /// <summary>
    /// Captures one event-triggered measurement point at the selected wavelength and appends 
    /// it to the live table series.
    /// </summary>
    /// <param name="concentration">
    /// Concentration value associated with the current spectrum.
    /// </param>
    public void CaptureEventPoint(double concentration)
    {
        if (DisplayedSpectrum is null)
        {
            return;
        }

        double y = GetYValueAtSelectedWavelength(DisplayedSpectrum);

        AppendLiveRow(FormatConcentration(concentration), FormatYValue(y, DisplayedSpectrum.Mode));
    }

    /// <summary>
    /// Refreshes all chart metadata, table headers, operating-mode text and status indicators from 
    /// the current spectrometer session.
    /// </summary>
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

    /// <summary>
    /// Converts the current live table values into an archived measurement series and resets the live-series state.
    ///
    /// Empty live series are ignored. When the archive limit is exceeded, the oldest retained series is removed.
    /// </summary>
    public void ArchiveLiveSeries()
    {
        DisplayedSpectrum = null;

        if (_liveRowCount == 0)
        {
            return;
        }

        List<TableRow> rows = new(_liveRowCount);

        for (int i = 0; i < _liveRowCount; i++)
        {
            WideTableRow row = WideRows[i];
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
        _liveRowCount = 0;
        _acquisitionStartedAt = null;
        RebuildColumns();
    }

    /// <summary>
    /// Receives processed spectra from the backend session.
    ///
    /// Incoming spectra are ignored while recording is stopped. While an update is already scheduled, 
    /// the pending spectrum is replaced with the newest one instead of scheduling additional UI work.
    /// </summary>
    /// <param name="spectrum">Newest processed spectrum.</param>
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

    /// <summary>
    /// Transfers the newest pending spectrum to UI-bound properties while limiting updates to the configured interval.
    /// </summary>
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

    /// <summary>
    /// Schedules a complete UI refresh after the backend session reports a state change.
    /// </summary>
    private void OnSessionStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(RefreshAll);
    }

    /// <summary>
    /// Selects the chart configuration associated with the current acquisition mode.
    /// </summary>
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

    /// <summary>
    /// Configures a wavelength-based chart covering the model-specific spectral range.
    /// </summary>
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

    /// <summary>
    /// Configures a time-resolved chart with elapsed time on the x-axis and the selected 
    /// wavelength's value on the y-axis.
    /// </summary>
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

    /// <summary>
    /// Configures an event-triggered chart with concentration on the x-axis.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the wide-table column structure for the current live series and all archived series.
    ///
    /// Existing rows are expanded or reduced to match the resulting column count before archived 
    /// values are written.
    /// </summary>
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

    /// <summary>
    /// Ensures that the wide table contains at least the requested number of rows and initializes 
    /// each new row with one cell per current column.
    /// </summary>
    /// <param name="minimumCount">Required minimum row count.</param>
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

    /// <summary>
    /// Writes all retained archived series into their corresponding pairs of wide-table columns.
    /// </summary>
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

    /// <summary>
    /// Updates the live table headers from the current acquisition mode, concentration unit and spectrometer operating mode.
    /// </summary>
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
            OperatingMode.Fluorescence405 => "F405 [rel.]",
            OperatingMode.Fluorescence500 => "F500 [rel.]",
            _ => "y"
        };

        RebuildColumns();
    }

    /// <summary>
    /// Applies an incoming spectrum to the table according to the current acquisition mode.
    /// </summary>
    /// <param name="spectrum">Spectrum to process for display.</param>
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

    /// <summary>
    /// Writes the complete wavelength and y-value arrays of a spectrum into the live table columns.
    /// </summary>
    /// <param name="spectrum">Spectrum whose points should be displayed.</param>
    private void UpdateFullSpectrumTable(Spectrum spectrum)
    {
        int count = Math.Min(spectrum.WavelengthNm.Length, spectrum.YAxis.Length);
        EnsureRowCount(count);
        _liveRowCount = count;

        for (int i = 0; i < count; i++)
        {
            WideRows[i].Cells[0].Value = FormatXValue(spectrum.WavelengthNm[i]);
            WideRows[i].Cells[1].Value = FormatYValue(spectrum.YAxis[i], spectrum.Mode);
        }
    }

    /// <summary>
    /// Appends one formatted x/y pair to the live table series.
    /// </summary>
    /// <param name="xValue">Formatted x-axis value.</param>
    /// <param name="yValue">Formatted y-axis value.</param>
    private void AppendLiveRow(string xValue, string yValue)
    {
        int rowIndex = WideRows.Count;
        EnsureRowCount(rowIndex + 1);

        WideRows[rowIndex].Cells[0].Value = xValue;
        WideRows[rowIndex].Cells[1].Value = yValue;
        _liveRowCount++;
    }

    /// <summary>
    /// Samples the incoming spectrum at the selected wavelength and appends the
    /// value together with the elapsed acquisition time.
    /// </summary>
    /// <param name="spectrum">Current processed spectrum.</param>
    private void AppendTimeResolvedTableRow(Spectrum spectrum)
    {
        _acquisitionStartedAt ??= DateTimeOffset.UtcNow;
        double elapsedSeconds = (DateTimeOffset.UtcNow - _acquisitionStartedAt.Value).TotalSeconds;
        double y = GetYValueAtSelectedWavelength(spectrum);

        AppendLiveRow(elapsedSeconds.ToString("F2"), FormatYValue(y, spectrum.Mode));
    }

    /// <summary>
    /// Returns the spectrum value whose wavelength is closest to <see cref="SelectedWavelengthNm"/>.
    /// </summary>
    /// <param name="spectrum">Spectrum to sample.</param>
    /// <returns>
    /// The y-value at the nearest available wavelength, or
    ///  <see cref="double.NaN"/> if the spectrum contains no usable points.
    /// </returns>
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

    /// <summary>
    /// Updates initialization, white-lamp and calibration status colors from the current backend state.
    /// </summary>
    private void RefreshStatusIndicators()
    {
        InitializationStatusColor = _spectrometer.IsInitialized ? Colors.LimeGreen : Colors.Red;

        RefreshWhiteLampStatus();

        CalibrationStatusColor = _spectrometer.IsCalibrated ? Colors.LimeGreen : Colors.Red;
    }

    /// <summary>
    /// Calculates the white-lamp indicator color from the initialization check, 
    /// current lamp state and accumulated warm-up state.
    /// </summary>
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

    /// <summary>
    /// Resolves the localized display name of the active backend operating mode.
    /// </summary>
    private void RefreshCurrentOperatingMode()
    {
        SpectroVisOperatingModeOption? option = OperatingModeOptions.FirstOrDefault(item => item.Mode == Session.Mode);

        CurrentOperatingMode = option?.DisplayName ?? Session.Mode.ToString();
    }

    /// <summary>
    /// Builds the operating-mode selection from the capabilities of the connected spectrometer model.
    /// </summary>
    private void BuildOperatingModeOptions()
    {

        AddOperatingModeOption(OperatingMode.RawCounts, AppResources.OperatingMode_RawCounts, isSupported: true);
        AddOperatingModeOption(OperatingMode.Intensity, AppResources.OperatingMode_Emission, isSupported: true);
        AddOperatingModeOption(OperatingMode.Transmission, AppResources.OperatingMode_Transmittance, isSupported: _spectrometer.Model.HasWhiteLamp);
        AddOperatingModeOption(OperatingMode.Absorbance, AppResources.OperatingMode_Absorbance, isSupported: _spectrometer.Model.HasWhiteLamp);
        AddOperatingModeOption(OperatingMode.Fluorescence405, AppResources.OperatingMode_Fluorescence405, isSupported: _spectrometer.Model.HasLed405);
        AddOperatingModeOption(OperatingMode.Fluorescence500, AppResources.OperatingMode_Fluorescence500, isSupported: _spectrometer.Model.HasLed500);
    }

    /// <summary>
    /// Adds one operating-mode option with its localized label, hardware support state and initial selection state.
    /// </summary>
    private void AddOperatingModeOption(OperatingMode mode, string displayName, bool isSupported)
    {
        OperatingModeOptions.Add(new SpectroVisOperatingModeOption(
            mode,
            displayName,
            isSupported,
            isSelected: Session.Mode == mode));
    }

    /// <summary>
    /// Synchronizes the selected option with the operating mode currently stored in the spectrometer session.
    /// </summary>
    private void RefreshOperatingModeSelection()
    {
        foreach (SpectroVisOperatingModeOption option in OperatingModeOptions)
        {
            option.IsSelected = option.Mode == Session.Mode;
        }
    }

    /// <summary>
    /// Determines whether the integration time may be edited in the current
    /// operating and recording state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when recording is stopped and the active mode does not require the fixed 
    /// calibration-dependent integration time used by absorbance or transmission.
    /// </returns>
    private bool CanEditIntegrationTimeForCurrentMode()
    {
        return !IsMeasurementRunning && Session.Mode is not OperatingMode.Absorbance
            and not OperatingMode.Transmission;
    }

    /// <summary>
    /// Resolves the localized y-axis title for a spectrometer operating mode.
    /// </summary>
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

    /// <summary>
    /// Returns the default display range used for the selected operating mode.
    /// </summary>
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

    /// <summary>
    /// Formats a wavelength value with one decimal place.
    /// </summary>
    private static string FormatXValue(double value)
    {
        return value.ToString("F1");
    }

    /// <summary>
    /// Formats a measured y-value using the precision appropriate for its operating mode.
    /// </summary>
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

    /// <summary>
    /// Removes the session event subscriptions owned by this view model.
    /// </summary>
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