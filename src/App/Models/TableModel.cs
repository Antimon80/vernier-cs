using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Models;

public sealed partial class TableCell : ObservableObject
{
    [ObservableProperty]
    public partial string? Value { get; set; }

    public Color Color { get; init; } = Colors.Black;
}

public sealed class WideTableRow
{
    public ObservableCollection<TableCell> Cells { get; } = [];
}

public sealed partial class TableRow(string xValue, string yValue) : ObservableObject
{
    [ObservableProperty]
    public partial string XValue { get; set; } = xValue;
    [ObservableProperty]
    public partial string YValue { get; set; } = yValue;
}

public sealed record TableColumn(string Header, Color Color);

public sealed record MeasurementSeries(
    Guid Id,
    DateTimeOffset RecordedAt,
    string XColumnHeader,
    string YColumnHeader,
    IReadOnlyList<TableRow> Rows
);

/// <summary>
/// Owns and maintains a device-agnostic wide-table representation: one live series plus a bounded
/// history of previously archived series, each contributing an x/y pair of columns.
///
/// This class only knows how to lay out columns, rows and cells. It has no notion of what a device
/// measures, which acquisition mode is active, or what the header text should say - callers decide
/// that and pass it in via <see cref="SetLiveHeaders"/>.
/// </summary>
public sealed class WideMeasurementTable
{
    private const int MaxArchivedSeries = 10;

    /// <summary>
    /// Repeating color palette used for the live series and archived series columns.
    /// </summary>
    private static readonly Color[] SeriesColors = [Colors.Green, Colors.Red, Colors.Blue, Colors.DarkOrange, Colors.Purple, Colors.Teal];

    private string _liveXHeader = "";
    private string _liveYHeader = "";

    /// <summary>
    /// Gets the column definitions displayed by the wide table.
    /// </summary>
    public ObservableCollection<TableColumn> Columns { get; } = [];

    /// <summary>
    /// Gets the rows containing the live series and all retained archived series.
    /// </summary>
    public ObservableCollection<WideTableRow> WideRows { get; } = [];

    /// <summary>
    /// Gets previously archived measurement series retained for comparison.
    /// </summary>
    public ObservableCollection<MeasurementSeries> ArchivedSeries { get; } = [];

    /// <summary>
    /// Gets the number of rows currently occupied by the live series.
    /// </summary>
    public int LiveRowCount { get; private set; }

    /// <summary>
    /// Sets the header text of the live series' x/y columns and rebuilds the column structure.
    /// </summary>
    public void SetLiveHeaders(string xHeader, string yHeader)
    {
        _liveXHeader = xHeader;
        _liveYHeader = yHeader;

        RebuildColumns();
    }

    /// <summary>
    /// Ensures that the wide table contains at least the requested number of rows and initializes
    /// each new row with one cell per current column.
    /// </summary>
    public void EnsureRowCount(int minimumCount)
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
    /// Writes one formatted x/y pair into the live series at the given row index, growing the
    /// table if necessary. Does not update <see cref="LiveRowCount"/>; use <see cref="SetLiveRowCount"/>
    /// or <see cref="AppendLiveRow"/> for that.
    /// </summary>
    public void WriteLiveCell(int rowIndex, string xValue, string yValue)
    {
        EnsureRowCount(rowIndex + 1);

        WideRows[rowIndex].Cells[0].Value = xValue;
        WideRows[rowIndex].Cells[1].Value = yValue;
    }

    /// <summary>
    /// Sets <see cref="LiveRowCount"/> directly, for callers that write a whole batch of rows
    /// themselves (e.g. a full-spectrum sweep) via <see cref="WriteLiveCell"/>.
    /// </summary>
    public void SetLiveRowCount(int count)
    {
        LiveRowCount = count;
    }

    /// <summary>
    /// Appends one formatted x/y pair to the live series.
    /// </summary>
    public void AppendLiveRow(string xValue, string yValue)
    {
        WriteLiveCell(LiveRowCount, xValue, yValue);
        LiveRowCount++;
    }

    /// <summary>
    /// Converts the current live series into an archived measurement series and resets the live-series state.
    ///
    /// An empty live series is ignored. When the archive limit is exceeded, the oldest retained series is removed.
    /// </summary>
    public void ArchiveLiveSeries()
    {
        if (LiveRowCount == 0)
        {
            return;
        }

        List<TableRow> rows = new(LiveRowCount);

        for (int i = 0; i < LiveRowCount; i++)
        {
            WideTableRow row = WideRows[i];
            rows.Add(new TableRow(row.Cells[0].Value ?? "", row.Cells[1].Value ?? ""));
        }

        ArchivedSeries.Add(new MeasurementSeries(Guid.NewGuid(), DateTimeOffset.Now, _liveXHeader, _liveYHeader, rows));

        if (ArchivedSeries.Count > MaxArchivedSeries)
        {
            ArchivedSeries.RemoveAt(0);
        }

        WideRows.Clear();
        LiveRowCount = 0;
        RebuildColumns();
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
}
