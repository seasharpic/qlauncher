using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace MinecraftLauncher.Helpers
{
    public class ComparisonConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value is true) ? (parameter ?? BindingOperations.DoNothing) : BindingOperations.DoNothing;
        }
    }
}
