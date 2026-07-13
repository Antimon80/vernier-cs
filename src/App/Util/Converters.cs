using System.Globalization;

namespace App.Util;

public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not bool b || !b;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

public sealed class CountToWidthConverter : IValueConverter
{
    private const double ColumnWidth = 120.0;
    private const double ScrollbarAllowance = 24;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value is int i ? i : 0;
        return count * ColumnWidth + ScrollbarAllowance;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}