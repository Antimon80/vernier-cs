using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Models;

public enum SeriesStyle
{
    Line,
    Points
}

public sealed record SeriesAxis(string Title, string Unit, string Format)
{
    public string Header => string.IsNullOrEmpty(Unit) ? Title : $"{Title} [{Unit}]";

    public string FormatValue(double value) => value.ToString(Format, CultureInfo.CurrentCulture);
}

public sealed partial class MeasurementSeries : ObservableObject
{
    private readonly List<double> _xValues = [];
    private readonly List<double> _yValues = [];

    public MeasurementSeries(Guid id, DateTimeOffset recordedAt, SeriesAxis xAxis, SeriesAxis yAxis,
        SeriesStyle style, string name, Color color, Dictionary<string, string>? metadata = null)
    {
        Id = id;
        RecordedAt = recordedAt;
        XAxis = xAxis ?? throw new ArgumentNullException(nameof(xAxis));
        YAxis = yAxis ?? throw new ArgumentNullException(nameof(yAxis));
        Style = style;

        Name = name ?? throw new ArgumentNullException(nameof(name));
        Color = color ?? throw new ArgumentNullException(nameof(color));

        Metadata = metadata ?? [];
    }

    public Guid Id { get; }
    public DateTimeOffset RecordedAt { get; }

    public SeriesAxis XAxis { get; }
    public SeriesAxis YAxis { get; }

    public SeriesStyle Style { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private Color _color = Colors.Blue;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isConfirmed;

    public bool IsFrozen { get; private set; }

    public Dictionary<string, string> Metadata { get; }

    public IReadOnlyList<double> XValues => _xValues;
    public IReadOnlyList<double> YValues => _yValues;

    public int Count => _xValues.Count;

    public event EventHandler? DataChanged;

    public void Append(double x, double y)
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException("The measurement series is frozen.");
        }

        _xValues.Add(x);
        _yValues.Add(y);

        OnDataChanged();
    }

    public void ReplaceAll(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException("The measurement series is frozen.");
        }

        if (x.Count != y.Count)
        {
            throw new ArgumentException("X and Y values must contain the same number of elements.");
        }

        _xValues.Clear();
        _yValues.Clear();

        _xValues.AddRange(x);
        _yValues.AddRange(y);

        OnDataChanged();
    }

    public void Freeze()
    {
        if (IsFrozen)
        {
            return;
        }

        IsFrozen = true;
        OnPropertyChanged(nameof(IsFrozen));
    }

    private void OnDataChanged()
    {
        OnPropertyChanged(nameof(XValues));
        OnPropertyChanged(nameof(YValues));
        OnPropertyChanged(nameof(Count));

        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}