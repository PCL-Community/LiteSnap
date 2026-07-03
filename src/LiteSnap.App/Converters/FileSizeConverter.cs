using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LiteSnap.App.Converters;

public class FileSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long length) return null;
        if (length == 0) return "—";

        return length switch
        {
            < 1024 => $"{length} B",
            < 1024 * 1024 => $"{length / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{length / (1024.0 * 1024):F1} MB",
            _ => $"{length / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
