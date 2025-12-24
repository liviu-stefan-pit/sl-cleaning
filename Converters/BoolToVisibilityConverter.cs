using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SL_Cleaning.Converters;

/// <summary>
/// Converts boolean to Visibility. True = Visible, False = Collapsed.
/// Use ConverterParameter="Inverse" to invert.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);

        if (inverse)
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visibility = value is Visibility v ? v : Visibility.Collapsed;
        var inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);

        var result = visibility == Visibility.Visible;

        return inverse ? !result : result;
    }
}

/// <summary>
/// Converts boolean to Visibility. True = Collapsed, False = Visible.
/// Inverse of standard BoolToVisibilityConverter.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        return boolValue ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visibility = value is Visibility v ? v : Visibility.Collapsed;
        return visibility != Visibility.Visible;
    }
}
