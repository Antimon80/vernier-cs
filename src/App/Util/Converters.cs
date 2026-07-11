using System.Globalization;

namespace App.Util;

public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}

public sealed class CountToWidthConverter : IValueConverter
{
    private const double ColumnWidth = 90;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value is int i ? i : 0;
        return count * ColumnWidth;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}