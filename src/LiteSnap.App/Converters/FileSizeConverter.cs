using System;
using System.Globalization;
using Avalonia.Data.Converters;
using LiteSnap.Core.Models;

namespace LiteSnap.App.Converters;

public class FileSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileVersionObjects f)
            return null;

        if (f.ObjectType == ObjectType.Directory)
            return "—";

        return f.Length switch
        {
            < 1024 => $"{f.Length} B",
            < 1024 * 1024 => $"{f.Length / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{f.Length / (1024.0 * 1024):F1} MB",
            _ => $"{f.Length / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
