using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Models;

public sealed partial class ChartModel : ObservableObject
{
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

    public void AutoscaleFull()
    {
        if (XValues.Count == 0 || YValues.Count == 0)
        {
            return;
        }

        (XMinimum, XMaximum) = GetRangeWithPadding(XValues);
        (YMinimum, YMaximum) = GetRangeWithPadding(YValues);
    }

    public void AutoscaleYAxis()
    {
        if (XValues.Count == 0 || YValues.Count == 0)
        {
            return;
        }

        (YMinimum, YMaximum) = GetRangeWithPadding(YValues);
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
}