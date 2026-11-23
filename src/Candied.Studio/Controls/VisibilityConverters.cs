namespace Candied.Studio.Controls;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

public static class VisibilityConverters
{
    public static IValueConverter BooleanToVisibility { get; } = new BooleanToVisibilityConverter();
    public static IValueConverter InvertedBooleanToVisibility { get; } = new InvertedBooleanToVisibilityConverter();
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public sealed class InvertedBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is false ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not Visibility.Visible;
    }
}
