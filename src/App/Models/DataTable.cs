using System.Collections.ObjectModel;
using Backend.Devices.GoDirect;
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
    OperatingMode Mode,
    AcquisitionMode AcquisitionMode,
    DateTimeOffset RecordedAt,
    string XColumnHeader,
    string YColumnHeader,
    IReadOnlyList<TableRow> Rows
);