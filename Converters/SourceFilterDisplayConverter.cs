using System;
using System.Globalization;
using System.Windows.Data;
using SL_Cleaning.Models;

namespace SL_Cleaning.Converters;

/// <summary>
/// Converts SourceFilterOption enum values to user-friendly display strings.
/// </summary>
[ValueConversion(typeof(SourceFilterOption), typeof(string))]
public sealed class SourceFilterDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SourceFilterOption option)
        {
            return option switch
            {
                SourceFilterOption.RegistryOnly => "Registry Only",
                SourceFilterOption.WindowsStoreOnly => "Windows Store Only",
                SourceFilterOption.All => "All Sources",
                _ => value.ToString() ?? string.Empty
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Not needed for display-only binding
        throw new NotImplementedException();
    }
}
