using System.Globalization;
using Microsoft.Maui.Layouts;

namespace App.Views.Controls;

/// <summary>
/// Displays a measurement chart together with four small entry fields that let the user directly
/// edit the visible minimum/maximum of the x- and y-axis.
///
/// The chart itself is drawn by the wrapped <see cref="MeasurementChartCanvas"/>. The four entry
/// fields are positioned in an overlaid <see cref="AbsoluteLayout"/> so they sit right at the ends
/// of the axis lines, using the same plot-area fractions the canvas already publishes for the
/// spectrum strip beneath the chart.
/// </summary>
public sealed partial class MeasurementChart : ContentView
{
    public static readonly BindableProperty XMinimumProperty = BindableProperty.Create(
        nameof(XMinimum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: OnXMinimumChanged, defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty XMaximumProperty = BindableProperty.Create(
        nameof(XMaximum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: OnXMaximumChanged, defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty YMinimumProperty = BindableProperty.Create(
        nameof(YMinimum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: OnYMinimumChanged, defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty YMaximumProperty = BindableProperty.Create(
        nameof(YMaximum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: OnYMaximumChanged, defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty XValuesProperty = BindableProperty.Create(
        nameof(XValues), typeof(IReadOnlyList<double>), typeof(MeasurementChart), Array.Empty<double>(),
        propertyChanged: (bindable, _, newValue) => ((MeasurementChart)bindable)._canvas.XValues = (IReadOnlyList<double>)newValue);

    public static readonly BindableProperty YValuesProperty = BindableProperty.Create(
        nameof(YValues), typeof(IReadOnlyList<double>), typeof(MeasurementChart), Array.Empty<double>(),
        propertyChanged: (bindable, _, newValue) => ((MeasurementChart)bindable)._canvas.YValues = (IReadOnlyList<double>)newValue);

    /// <summary>
    /// Lowest value the user may enter for the x-axis minimum/maximum entry fields.
    /// A <see langword="null"/> value leaves the x-axis unbounded on that side (only the basic
    /// "minimum below maximum" rule still applies).
    /// </summary>
    public static readonly BindableProperty XAxisLowerLimitProperty = BindableProperty.Create(
        nameof(XAxisLowerLimit), typeof(double?), typeof(MeasurementChart), null);

    /// <summary>
    /// Highest value the user may enter for the x-axis minimum/maximum entry fields.
    /// See <see cref="XAxisLowerLimitProperty"/>.
    /// </summary>
    public static readonly BindableProperty XAxisUpperLimitProperty = BindableProperty.Create(
        nameof(XAxisUpperLimit), typeof(double?), typeof(MeasurementChart), null);

    /// <summary>
    /// Lowest value the user may enter for the y-axis minimum/maximum entry fields.
    /// See <see cref="XAxisLowerLimitProperty"/>.
    /// </summary>
    public static readonly BindableProperty YAxisLowerLimitProperty = BindableProperty.Create(
        nameof(YAxisLowerLimit), typeof(double?), typeof(MeasurementChart), null);

    /// <summary>
    /// Highest value the user may enter for the y-axis minimum/maximum entry fields.
    /// See <see cref="XAxisLowerLimitProperty"/>.
    /// </summary>
    public static readonly BindableProperty YAxisUpperLimitProperty = BindableProperty.Create(
        nameof(YAxisUpperLimit), typeof(double?), typeof(MeasurementChart), null);

    /// <summary>
    /// Left edge of the plotted data area, expressed as a fraction (0-1) of the control's width.
    /// Forwarded from the wrapped canvas so sibling controls (e.g. the spectrum strip) can align to
    /// it without needing a reference to the canvas itself.
    /// </summary>
    public static readonly BindableProperty PlotLeftFractionProperty = BindableProperty.Create(
        nameof(PlotLeftFraction), typeof(double), typeof(MeasurementChart), 0.0);

    /// <summary>
    /// Right edge of the plotted data area, expressed as a fraction (0-1) of the control's width.
    /// See <see cref="PlotLeftFractionProperty"/>.
    /// </summary>
    public static readonly BindableProperty PlotRightFractionProperty = BindableProperty.Create(
        nameof(PlotRightFraction), typeof(double), typeof(MeasurementChart), 1.0);

    /// <summary>
    /// Smallest allowed gap between an axis's minimum and maximum, used to stop the user from
    /// entering (or clamping into) a degenerate or inverted range.
    /// </summary>
    private const double MinimumAxisGapFraction = 0.001;

    private readonly MeasurementChartCanvas _canvas = new();
    private readonly Entry _xMinEntry;
    private readonly Entry _xMaxEntry;
    private readonly Entry _yMinEntry;
    private readonly Entry _yMaxEntry;

    /// <summary>
    /// Builds the canvas, the four axis-range entry fields and the overlay layout that positions
    /// them, and wires up the plumbing that keeps everything synchronized.
    /// </summary>
    public MeasurementChart()
    {
        _xMinEntry = CreateAxisEntry(CommitXMinimumEntry);
        _xMaxEntry = CreateAxisEntry(CommitXMaximumEntry);
        _yMinEntry = CreateAxisEntry(CommitYMinimumEntry);
        _yMaxEntry = CreateAxisEntry(CommitYMaximumEntry);

        AbsoluteLayout entryLayer = new() { BackgroundColor = Colors.Transparent };
        entryLayer.Children.Add(_xMinEntry);
        entryLayer.Children.Add(_xMaxEntry);
        entryLayer.Children.Add(_yMinEntry);
        entryLayer.Children.Add(_yMaxEntry);

        Grid root = new();
        root.Children.Add(_canvas);
        root.Children.Add(entryLayer);
        Content = root;

        _canvas.PlotAreaChanged += OnPlotAreaChanged;
        _canvas.SizeChanged += (_, _) => RepositionEntries();

        RefreshEntryText(_xMinEntry, XMinimum);
        RefreshEntryText(_xMaxEntry, XMaximum);
        RefreshEntryText(_yMinEntry, YMinimum);
        RefreshEntryText(_yMaxEntry, YMaximum);
    }

    public double XMinimum
    {
        get => (double)GetValue(XMinimumProperty);
        set => SetValue(XMinimumProperty, value);
    }

    public double XMaximum
    {
        get => (double)GetValue(XMaximumProperty);
        set => SetValue(XMaximumProperty, value);
    }

    public double YMinimum
    {
        get => (double)GetValue(YMinimumProperty);
        set => SetValue(YMinimumProperty, value);
    }

    public double YMaximum
    {
        get => (double)GetValue(YMaximumProperty);
        set => SetValue(YMaximumProperty, value);
    }

    public IReadOnlyList<double> XValues
    {
        get => (IReadOnlyList<double>)GetValue(XValuesProperty);
        set => SetValue(XValuesProperty, value);
    }

    public IReadOnlyList<double> YValues
    {
        get => (IReadOnlyList<double>)GetValue(YValuesProperty);
        set => SetValue(YValuesProperty, value);
    }

    public double? XAxisLowerLimit
    {
        get => (double?)GetValue(XAxisLowerLimitProperty);
        set => SetValue(XAxisLowerLimitProperty, value);
    }

    public double? XAxisUpperLimit
    {
        get => (double?)GetValue(XAxisUpperLimitProperty);
        set => SetValue(XAxisUpperLimitProperty, value);
    }

    public double? YAxisLowerLimit
    {
        get => (double?)GetValue(YAxisLowerLimitProperty);
        set => SetValue(YAxisLowerLimitProperty, value);
    }

    public double? YAxisUpperLimit
    {
        get => (double?)GetValue(YAxisUpperLimitProperty);
        set => SetValue(YAxisUpperLimitProperty, value);
    }

    public double PlotLeftFraction
    {
        get => (double)GetValue(PlotLeftFractionProperty);
        private set => SetValue(PlotLeftFractionProperty, value);
    }

    public double PlotRightFraction
    {
        get => (double)GetValue(PlotRightFractionProperty);
        private set => SetValue(PlotRightFractionProperty, value);
    }

    private static void OnXMinimumChanged(BindableObject bindable, object oldValue, object newValue)
    {
        MeasurementChart chart = (MeasurementChart)bindable;
        double value = (double)newValue;
        chart._canvas.XMinimum = value;
        chart.RefreshEntryText(chart._xMinEntry, value);
    }

    private static void OnXMaximumChanged(BindableObject bindable, object oldValue, object newValue)
    {
        MeasurementChart chart = (MeasurementChart)bindable;
        double value = (double)newValue;
        chart._canvas.XMaximum = value;
        chart.RefreshEntryText(chart._xMaxEntry, value);
    }

    private static void OnYMinimumChanged(BindableObject bindable, object oldValue, object newValue)
    {
        MeasurementChart chart = (MeasurementChart)bindable;
        double value = (double)newValue;
        chart._canvas.YMinimum = value;
        chart.RefreshEntryText(chart._yMinEntry, value);
    }

    private static void OnYMaximumChanged(BindableObject bindable, object oldValue, object newValue)
    {
        MeasurementChart chart = (MeasurementChart)bindable;
        double value = (double)newValue;
        chart._canvas.YMaximum = value;
        chart.RefreshEntryText(chart._yMaxEntry, value);
    }

    /// <summary>
    /// Forwards the canvas's plot-area fractions to this control's own bindable properties (so
    /// siblings such as the spectrum strip can bind to the chart without knowing about the inner
    /// canvas), and repositions the axis-range entry fields whenever the plot area moves or resizes.
    /// </summary>
    private void OnPlotAreaChanged()
    {
        PlotLeftFraction = _canvas.PlotLeftFraction;
        PlotRightFraction = _canvas.PlotRightFraction;
        RepositionEntries();
    }

    /// <summary>
    /// Moves the four axis-range entry fields so they sit centered in the margin band next to the
    /// axis line they edit (Y entries centered horizontally in the left margin and vertically on the
    /// plot top/bottom; X entries centered vertically in the bottom margin and horizontally on the
    /// plot left/right). Every resulting position is clamped to stay fully inside the control's own
    /// bounds, so small rounding differences between the canvas's margin math and this layout can
    /// never push a field outside the chart or into neighboring UI.
    /// </summary>
    private void RepositionEntries()
    {
        double width = _canvas.Width;
        double height = _canvas.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        double entryWidth = MeasurementChartCanvas.EntryWidth;
        double entryHeight = MeasurementChartCanvas.EntryHeight;

        double plotLeft = _canvas.PlotLeftFraction * width;
        double plotRight = _canvas.PlotRightFraction * width;
        double plotTop = _canvas.PlotTopFraction * height;
        double plotBottom = _canvas.PlotBottomFraction * height;

        // Centered in the left-hand margin gutter (between the control's left edge and the plot area).
        double yEntryX = (plotLeft - entryWidth) / 2;

        PlaceEntry(_yMaxEntry, yEntryX, plotTop - entryHeight / 2, entryWidth, entryHeight, width, height);
        PlaceEntry(_yMinEntry, yEntryX, plotBottom - entryHeight / 2, entryWidth, entryHeight, width, height);

        // Centered in the bottom margin gutter (between the plot area and the control's bottom edge).
        double xEntryY = plotBottom + (height - plotBottom - entryHeight) / 2;

        PlaceEntry(_xMinEntry, plotLeft - entryWidth / 2, xEntryY, entryWidth, entryHeight, width, height);
        PlaceEntry(_xMaxEntry, plotRight - entryWidth / 2, xEntryY, entryWidth, entryHeight, width, height);
    }

    /// <summary>
    /// Positions one axis-range entry field, clamping its top-left corner so the field never renders
    /// outside the given parent size.
    /// </summary>
    private static void PlaceEntry(Entry entry, double x, double y, double entryWidth, double entryHeight, double parentWidth, double parentHeight)
    {
        x = Math.Clamp(x, 0, Math.Max(0, parentWidth - entryWidth));
        y = Math.Clamp(y, 0, Math.Max(0, parentHeight - entryHeight));

        AbsoluteLayout.SetLayoutBounds(entry, new Rect(x, y, entryWidth, entryHeight));
    }

    /// <summary>
    /// Creates one axis-range entry field with the shared visual style and wires it to commit its
    /// value (via <paramref name="onCommitted"/>) whenever editing finishes.
    /// </summary>
    private static Entry CreateAxisEntry(Action<Entry> onCommitted)
    {
        Entry entry = new()
        {
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Colors.White,
            TextColor = Colors.Black,
        };

        AbsoluteLayout.SetLayoutFlags(entry, AbsoluteLayoutFlags.None);

        entry.Completed += (_, _) => onCommitted(entry);
        entry.Unfocused += (_, _) => onCommitted(entry);

        return entry;
    }

    /// <summary>
    /// Applies a newly entered x-axis minimum: clamps it to <see cref="XAxisLowerLimit"/>/
    /// <see cref="XAxisUpperLimit"/>, keeps it below the current maximum, and pushes the result back
    /// into <see cref="XMinimum"/>. Invalid text is discarded and the field reverts to the current value.
    /// </summary>
    private void CommitXMinimumEntry(Entry entry)
    {
        if (!TryParse(entry.Text, out double parsed))
        {
            RefreshEntryText(entry, XMinimum);
            return;
        }

        double clamped = Clamp(parsed, XAxisLowerLimit, XAxisUpperLimit);
        double gap = AxisGap(XAxisLowerLimit, XAxisUpperLimit, XMaximum - XMinimum);

        if (clamped >= XMaximum - gap)
        {
            clamped = XMaximum - gap;
        }

        XMinimum = clamped;
    }

    /// <summary>
    /// Applies a newly entered x-axis maximum. See <see cref="CommitXMinimumEntry"/>.
    /// </summary>
    private void CommitXMaximumEntry(Entry entry)
    {
        if (!TryParse(entry.Text, out double parsed))
        {
            RefreshEntryText(entry, XMaximum);
            return;
        }

        double clamped = Clamp(parsed, XAxisLowerLimit, XAxisUpperLimit);
        double gap = AxisGap(XAxisLowerLimit, XAxisUpperLimit, XMaximum - XMinimum);

        if (clamped <= XMinimum + gap)
        {
            clamped = XMinimum + gap;
        }

        XMaximum = clamped;
    }

    /// <summary>
    /// Applies a newly entered y-axis minimum. See <see cref="CommitXMinimumEntry"/>.
    /// </summary>
    private void CommitYMinimumEntry(Entry entry)
    {
        if (!TryParse(entry.Text, out double parsed))
        {
            RefreshEntryText(entry, YMinimum);
            return;
        }

        double clamped = Clamp(parsed, YAxisLowerLimit, YAxisUpperLimit);
        double gap = AxisGap(YAxisLowerLimit, YAxisUpperLimit, YMaximum - YMinimum);

        if (clamped >= YMaximum - gap)
        {
            clamped = YMaximum - gap;
        }

        YMinimum = clamped;
    }

    /// <summary>
    /// Applies a newly entered y-axis maximum. See <see cref="CommitXMinimumEntry"/>.
    /// </summary>
    private void CommitYMaximumEntry(Entry entry)
    {
        if (!TryParse(entry.Text, out double parsed))
        {
            RefreshEntryText(entry, YMaximum);
            return;
        }

        double clamped = Clamp(parsed, YAxisLowerLimit, YAxisUpperLimit);
        double gap = AxisGap(YAxisLowerLimit, YAxisUpperLimit, YMaximum - YMinimum);

        if (clamped <= YMinimum + gap)
        {
            clamped = YMinimum + gap;
        }

        YMaximum = clamped;
    }

    /// <summary>
    /// Updates an entry's displayed text to a formatted value, unless the user is currently editing
    /// that field (which would otherwise overwrite their in-progress keystrokes).
    /// </summary>
    private void RefreshEntryText(Entry entry, double value)
    {
        if (entry.IsFocused)
        {
            return;
        }

        entry.Text = value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Parses user-entered axis text, accepting both the current culture's decimal separator and
    /// the invariant one (e.g. a user on a German locale typing "." instead of ",").
    /// </summary>
    private static bool TryParse(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static double Clamp(double value, double? lower, double? upper)
    {
        if (lower.HasValue && value < lower.Value)
        {
            value = lower.Value;
        }

        if (upper.HasValue && value > upper.Value)
        {
            value = upper.Value;
        }

        return value;
    }

    /// <summary>
    /// Determines the smallest gap that must remain between an axis's minimum and maximum, scaled to
    /// the axis's allowed range where known, and otherwise to its current range.
    /// </summary>
    private static double AxisGap(double? lower, double? upper, double currentRange)
    {
        double referenceRange = lower.HasValue && upper.HasValue
            ? upper.Value - lower.Value
            : Math.Abs(currentRange);

        return referenceRange > 0
            ? referenceRange * MinimumAxisGapFraction
            : MinimumAxisGapFraction;
    }
}
