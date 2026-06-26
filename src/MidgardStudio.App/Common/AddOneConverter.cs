using System;
using System.Globalization;
using System.Windows.Data;

namespace MidgardStudio.App.Common;

/// <summary>Returns an int value + 1 — used to show a 0-based AlternationIndex as a 1-based position.</summary>
public sealed class AddOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? i + 1 : value;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? i - 1 : value;
}
