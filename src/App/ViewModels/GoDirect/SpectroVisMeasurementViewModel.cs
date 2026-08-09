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
public sealed partial class SpectroVisMeasurementViewModel : ObservableObject, IDisposable, IMeasurementSettings
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
    /// Owns the wide-table column/row/archive layout. Device-agnostic; this view model only
    /// decides what the live headers should say and when to archive.
    /// </summary>
    private readonly WideMeasurementTable _table = new();

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
        BuildAcquisitionModeOptions();
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
    public ObservableCollection<TableColumn> Columns => _table.Columns;

    /// <summary>
    /// Gets the rows containing the live series and all retained archived measurement series.
    /// </summary>
    public ObservableCollection<WideTableRow> WideRows => _table.WideRows;

    /// <summary>
    /// Gets previously completed measurement series retained for comparison.
    /// </summary>
    public ObservableCollection<MeasurementSeries> ArchivedSeries => _table.ArchivedSeries;

    /// <summary>
    /// Gets the operating modes presented by the operating-mode dialog, 
    /// including their support and selection state.
    /// </summary>
    public ObservableCollection<SpectroVisOperatingModeOption> OperatingModeOptions { get; } = [];

    public ObservableCollection<AcquisitionModeOption> AcquisitionModeOptions { get; } = [];

    /// <summary>
    /// Indicates that SpectroVis devices expose selectable operating modes.
    /// </summary>
    public bool HasOperatingModeSelection => true;

    public bool CanKeepDataPoint => AcquisitionMode == AcquisitionMode.EventTriggered;

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
    /// Raised when the platform view should display the SpectroVis calibration dialog and return the selected calibration action.
    /// </summary>
    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task<CalibrationDialogResult?>>? CalibrationDialogRequested;

    public event Func<SpectroVisMeasurementViewModel, CancellationToken, Task>? KeepDataPointDialogRequested;

    /// <summary>
    /// The event is triggered to stop data recording when a fixed time interval has elapsed in time-resolved mode.
    /// </summary>
    public event Action? AutoStopRequested;

    /// <summary>
    /// Gets or sets how incoming full spectra are interpreted and accumulated by the user interface.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the lowest value the user may enter for the x-axis range fields.
    /// <see langword="null"/> leaves the x-axis unbounded on that side.
    /// </summary>
    [ObservableProperty]
    public partial double? XAxisLowerLimit { get; set; }

    /// <summary>
    /// Gets or sets the highest value the user may enter for the x-axis range fields.
    /// See <see cref="XAxisLowerLimit"/>.
    /// </summary>
    [ObservableProperty]
    public partial double? XAxisUpperLimit { get; set; }

    /// <summary>
    /// Gets or sets the lowest value the user may enter for the y-axis range fields.
    /// See <see cref="XAxisLowerLimit"/>.
    /// </summary>
    [ObservableProperty]
    public partial double? YAxisLowerLimit { get; set; }

    /// <summary>
    /// Gets or sets the highest value the user may enter for the y-axis range fields.
    /// See <see cref="XAxisLowerLimit"/>.
    /// </summary>
    [ObservableProperty]
    public partial double? YAxisUpperLimit { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double> XValues { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<double> YValues { get; set; } = [];

    /// <summary>
    /// Gets or sets the wavelength sampled for time-resolved and event-triggered measurements.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedWavelengthNm { get; set; } = 500;

    [ObservableProperty]
    public partial int TimeResolvedDuration { get; set; } = 10;

    [ObservableProperty]
    public partial double DataPointValue { get; set; } = 0.0;

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

    [ObservableProperty]
    public partial bool CanEditTimeResolvedSettings { get; set; }

    [ObservableProperty]
    public partial bool CanEditEventTriggeredSettings { get; set; }

    /// <summary>
    /// Gets or sets whether the sampled wavelength may be edited.
    /// Relevant for both time-resolved and event-triggered acquisition, but not full-spectrum.
    /// </summary>
    [ObservableProperty]
    public partial bool CanEditWavelength { get; set; }

    /// <summary>
    /// Gets or sets whether time-resolved sampling records continuously instead of for a fixed duration.
    /// </summary>
    [ObservableProperty]
    public partial bool ContinuousDataCollection { get; set; }

    /// <summary>
    /// Gets or sets the full descriptive name for the x-axis of event-triggered measurement series
    /// (e.g. for legends/exports). The table header itself uses <see cref="ColumnNameShort"/>.
    /// </summary>
    [ObservableProperty]
    public partial string ColumnNameLong { get; set; } = AppResources.AcquisitionMode_Concentration;

    /// <summary>
    /// Gets or sets the abbreviated column name used in the (narrow) event-triggered table header.
    /// </summary>
    [ObservableProperty]
    public partial string ColumnNameShort { get; set; } = "c";

    /// <summary>
    /// Gets or sets the unit shown alongside <see cref="ColumnNameShort"/> in the event-triggered table header.
    /// </summary>
    [ObservableProperty]
    public partial string Unit { get; set; } = "mol/l";

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
    /// Requests display of the device-specific operating-mode dialog.
    /// </summary>
    /// <param name="ct">Cancellation token for the dialog operation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no dialog handler has been registered by the view.
    /// </exception>
    public async Task RequestOperatingModeDialog(CancellationToken ct = default)
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
    public async Task RequestAcquisitionModeDialog(CancellationToken ct = default)
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
    public async Task<CalibrationDialogResult?> RequestCalibrationDialog(CancellationToken ct = default)
    {
        if (CalibrationDialogRequested is null)
        {
            throw new InvalidOperationException("No SpectroVis calibration dialog is registered.");
        }

        return await CalibrationDialogRequested(this, ct);
    }

    /// <summary>
    /// Requests display of the device-specific "keep data point" dialog.
    /// </summary>
    /// <param name="ct">Cancellation token for the dialog operation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no dialog handler has been registered by the view.
    /// </exception>
    public async Task RequestKeepDataPointDialog(CancellationToken ct = default)
    {
        if (KeepDataPointDialogRequested is null)
        {
            throw new InvalidOperationException("No keep data point dialog is registered.");
        }

        await KeepDataPointDialogRequested(this, ct);
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

    /// <summary>
    /// Archives the current live series and rebuilds chart and table metadata
    /// whenever the acquisition mode changes.
    /// </summary>
    /// <param name="value">New acquisition mode.</param>
    public void SelectAcquisitionMode(AcquisitionMode mode)
    {
        AcquisitionMode = mode;

        ArchiveLiveSeries();

        RefreshChartConfiguration();
        RefreshTableHeaders();
        RefreshAcquisitionModeEditFlags();

        if (IsMeasurementRunning && DisplayedSpectrum is not null && mode == AcquisitionMode.FullSpectrum)
        {
            UpdateTable(DisplayedSpectrum);
        }
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
    /// <param name="value">
    /// Concentration value associated with the current spectrum.
    /// </param>
    public async Task CaptureEventPoint(double value, CancellationToken ct = default)
    {
        if (DisplayedSpectrum is null)
        {
            return;
        }

        double y = GetYValueAtSelectedWavelength(DisplayedSpectrum);
        _table.AppendLiveRow(FormatXValue(value), FormatYValue(y, DisplayedSpectrum.Mode));


    }

    /// <summary>
    /// Refreshes all chart metadata, table headers, operating-mode text and status indicators from 
    /// the current spectrometer session.
    /// </summary>
    public void RefreshAll()
    {
        IntegrationTimeMs = _spectrometer.Session.IntegrationTime;
        CanEditIntegrationTime = CanEditIntegrationTimeForCurrentMode();
        RefreshAcquisitionModeEditFlags();

        RefreshChartConfiguration();
        RefreshTableHeaders();
        RefreshCurrentOperatingMode();
        RefreshStatusIndicators();

        // Same reasoning as in OnAcquisitionModeChanged: DisplayedSpectrum is now always live,
        // independent of recording state, so writing into the table must stay gated separately.
        if (IsMeasurementRunning && DisplayedSpectrum is not null && AcquisitionMode == AcquisitionMode.FullSpectrum)
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
        _acquisitionStartedAt = null;

        _table.ArchiveLiveSeries();
    }

    public void Autoscale()
    {
        if (XValues.Count == 0 || YValues.Count == 0)
        {
            return;
        }

        (XMinimum, XMaximum) = GetRangeWithPadding(XValues);
        (YMinimum, YMaximum) = GetRangeWithPadding(YValues);
    }

    /// <summary>
    /// Receives processed spectra from the backend session.
    ///
    /// The device measures continuously regardless of recording state, so incoming spectra are always
    /// transferred to the live preview (<see cref="DisplayedSpectrum"/>). Whether they are also captured
    /// into the recorded table/series data is decided separately in <see cref="UpdateSpectrumOnUiThread"/>.
    /// While an update is already scheduled, the pending spectrum is replaced with the newest one instead
    /// of scheduling additional UI work.
    /// </summary>
    /// <param name="spectrum">Newest processed spectrum.</param>
    private void OnCurrentSpectrumChanged(Spectrum spectrum)
    {
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

            if (spectrum is null)
            {
                return;
            }

            _latestUiUpdateAt = DateTimeOffset.UtcNow;

            // The live preview always reflects the newest spectrum, independent of recording state.
            DisplayedSpectrum = spectrum;

            // Only capture into the recorded table/series while recording is actually running.
            if (_isMeasurementRunningProvider())
            {
                UpdateTable(spectrum);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// Pushes the current spectrum to the UI if data recording is running.
    /// </summary>
    /// <param name="value"></param>
    partial void OnDisplayedSpectrumChanged(Spectrum? value)
    {
        if (AcquisitionMode != AcquisitionMode.FullSpectrum)
        {
            return;
        }

        if (!IsMeasurementRunning)
        {
            XValues = [];
            YValues = [];
            return;
        }

        XValues = value?.WavelengthNm ?? [];
        YValues = value?.YAxis ?? [];
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

        // The wavelength axis cannot be scaled beyond what the sensor actually covers.
        XAxisLowerLimit = _spectrometer.Model.WavelengthMinNm;
        XAxisUpperLimit = _spectrometer.Model.WavelengthMaxNm;

        ApplyYAxisRange();

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

        // Elapsed time cannot be negative, but there is no natural upper bound.
        XAxisLowerLimit = 0;
        XAxisUpperLimit = null;

        ApplyYAxisRange();

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

        // Concentration cannot be negative, but there is no natural upper bound
        // (it depends on the user-chosen unit).
        XAxisLowerLimit = 0;
        XAxisUpperLimit = null;

        ApplyYAxisRange();

        ShowSpectrumStrip = false;
    }

    /// <summary>
    /// Applies the y-axis title, default display range and axis-entry clamp limits for the current
    /// spectrometer operating mode. The y-axis meaning depends only on <see cref="OperatingMode"/>,
    /// not on the acquisition mode, so all three chart configurations share this logic.
    /// </summary>
    private void ApplyYAxisRange()
    {
        YAxisTitle = GetYAxisTitle(Session.Mode);
        (double yMinimum, double yMaximum) = GetYAxisRange(Session.Mode);

        YMinimum = yMinimum;
        YMaximum = yMaximum;
        YAxisLowerLimit = yMinimum;
        YAxisUpperLimit = yMaximum;
    }

    /// <summary>
    /// Updates the live table headers from the current acquisition mode, concentration unit and spectrometer operating mode.
    /// </summary>
    private void RefreshTableHeaders()
    {
        string xHeader = AcquisitionMode switch
        {
            AcquisitionMode.FullSpectrum => "λ [nm]",
            AcquisitionMode.TimeResolved => "t [s]",
            AcquisitionMode.EventTriggered => $"{ColumnNameShort} [{Unit}]",
            _ => "x"
        };

        string yHeader = Session.Mode switch
        {
            OperatingMode.RawCounts => "ADC [counts]",
            OperatingMode.Intensity => "I [rel.]",
            OperatingMode.Transmission => "T [%]",
            OperatingMode.Absorbance => "A",
            OperatingMode.Fluorescence405 => "F405 [rel.]",
            OperatingMode.Fluorescence500 => "F500 [rel.]",
            _ => "y"
        };

        _table.SetLiveHeaders(xHeader, yHeader);
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
        _table.EnsureRowCount(count);

        for (int i = 0; i < count; i++)
        {
            _table.WriteLiveCell(i, FormatXValue(spectrum.WavelengthNm[i]), FormatYValue(spectrum.YAxis[i], spectrum.Mode));
        }

        _table.SetLiveRowCount(count);
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

        _table.AppendLiveRow(elapsedSeconds.ToString("F2"), FormatYValue(y, spectrum.Mode));

        if (!ContinuousDataCollection && elapsedSeconds >= TimeResolvedDuration)
        {
            AutoStopRequested?.Invoke();
        }
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
    /// Builds the acquisition-mode selection specific to spectrometers.
    /// </summary>
    private void BuildAcquisitionModeOptions()
    {
        AddAcquisitionModeOption(AcquisitionMode.FullSpectrum, AppResources.Spectrometer_FullSpectrum);
        AddAcquisitionModeOption(AcquisitionMode.TimeResolved, AppResources.Device_TimeResolved);
        AddAcquisitionModeOption(AcquisitionMode.EventTriggered, AppResources.Device_EventTriggered);
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

    private void AddAcquisitionModeOption(AcquisitionMode mode, string displayName)
    {
        AcquisitionModeOptions.Add(new AcquisitionModeOption(
            mode,
            displayName,
            isSelected: AcquisitionMode == mode
        ));
    }

    /// <summary>
    /// Synchronizes the selected option with the operating mode currently stored in the spectrometer session.
    ///
    /// Public so callers can resynchronize the radio-button selection after a failed mode switch,
    /// where the UI-bound option was already marked selected before the backend call failed.
    /// </summary>
    public void RefreshOperatingModeSelection()
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

    private bool CanEditTimeResolvedSettingsForCurrentMode()
    {
        return !IsMeasurementRunning && AcquisitionMode is not AcquisitionMode.FullSpectrum
            and not AcquisitionMode.EventTriggered;
    }

    private bool CanEditEventTriggeredSettingsForCurrentMode()
    {
        return !IsMeasurementRunning && AcquisitionMode is not AcquisitionMode.FullSpectrum
            and not AcquisitionMode.TimeResolved;
    }

    /// <summary>
    /// Determines whether the sampled wavelength may be edited in the current
    /// acquisition and recording state. Relevant for time-resolved and event-triggered
    /// acquisition, but not for full-spectrum acquisition.
    /// </summary>
    private bool CanEditWavelengthForCurrentMode()
    {
        return !IsMeasurementRunning && AcquisitionMode is not AcquisitionMode.FullSpectrum;
    }

    /// <summary>
    /// Recomputes all acquisition-mode-dependent field edit flags from the current
    /// acquisition mode and recording state.
    /// </summary>
    private void RefreshAcquisitionModeEditFlags()
    {
        CanEditTimeResolvedSettings = CanEditTimeResolvedSettingsForCurrentMode();
        CanEditEventTriggeredSettings = CanEditEventTriggeredSettingsForCurrentMode();
        CanEditWavelength = CanEditWavelengthForCurrentMode();
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
    /// Formats a numeric value with one decimal place.
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

    private static (double Minimum, double Maximum) GetRangeWithPadding(IReadOnlyList<double> values, double paddingFraction = 0.05)
    {
        double min = values.Min();
        double max = values.Max();
        double range = max - min;

        if (range <= 0)
        {
            double fallback = min != 0 ? Math.Abs(min) * 0.1 : 1;
            return (min - fallback, max + fallback);
        }

        double padding = range * paddingFraction;
        return (min - padding, max + padding);
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