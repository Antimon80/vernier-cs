using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Models;

public sealed partial class TableRow(string xValue, string yValue) : ObservableObject
{
    [ObservableProperty]
    public partial string XValue { get; set; } = xValue;
    [ObservableProperty]
    public partial string YValue { get; set; } = yValue;
}