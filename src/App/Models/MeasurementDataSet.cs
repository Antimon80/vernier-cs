using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Models;

public sealed partial class MeasurementDataSet : ObservableObject
{
    private static readonly Color[] Palette = [
        Color.FromArgb("#C62828"),
        Color.FromArgb("#2E7D32"),
        Color.FromArgb("#1565C0"),
        Color.FromArgb("#E65100"),
        Color.FromArgb("#6A1B9A"),
        Color.FromArgb("#00838F"),
        Color.FromArgb("#AD1457"),
        Color.FromArgb("#4E342E"),
        Color.FromArgb("#283593"),
        Color.FromArgb("#558B2F"),
        Color.FromArgb("#37474F")
    ];

    public ObservableCollection<MeasurementSeries> Series { get; } = [];

    [ObservableProperty]
    private MeasurementSeries? _live;

    public void StartLive(string namePrefix, SeriesAxis x, SeriesAxis y, SeriesStyle style)
    {
        Live = new MeasurementSeries(Guid.NewGuid(), DateTimeOffset.UtcNow, x, y,
            style, NextName(namePrefix), NextFreeColor());
    }

    public void FreezeLive()
    {
        if (Live is null || Live.Count == 0)
        {
            return;
        }

        MeasurementSeries frozen = Live;

        frozen.Freeze();
        Series.Add(frozen);

        if (Series.Count > 10)
        {
            // TODO: nachfragen statt löschen
            Series.RemoveAt(0);
        }

        Live = null;
    }

    private Color NextFreeColor()
    {
        foreach (Color color in Palette)
        {
            bool usedBySeries = Series.Any(series => series.Color == color);
            bool usedByLive = Live is not null && Live.Color == color;

            if (!usedBySeries && !usedByLive)
            {
                return color;
            }
        }

        throw new InvalidOperationException("No free color is available for another measurement series.");
    }

    private string NextName(string prefix)
    {
        int maximum = 0;
        string namePrefix = $"{prefix}_";

        foreach (MeasurementSeries series in Series)
        {
            if (series.Name.StartsWith(namePrefix, StringComparison.Ordinal)
                && int.TryParse(series.Name[namePrefix.Length..], out int number))
            {
                maximum = Math.Max(maximum, number);
            }
        }

        if (Live is not null && Live.Name.StartsWith(namePrefix, StringComparison.Ordinal)
            && int.TryParse(Live.Name[namePrefix.Length..], out int liveNumber))
        {
            maximum = Math.Max(maximum, liveNumber);
        }

        return $"{prefix}_{maximum + 1}";
    }
}