using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MidgardStudio.Core.Model;

namespace MidgardStudio.App.Common;

/// <summary>Maps a record origin to an accent brush for the list pill.</summary>
public sealed class OriginToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RecordOrigin.Overridden => new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)), // amber
        RecordOrigin.NewCustom => new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x8A)),  // teal
        _ => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),                        // muted
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a record origin to a short label.</summary>
public sealed class OriginToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RecordOrigin.Overridden => "OVERRIDE",
        RecordOrigin.NewCustom => "CUSTOM",
        _ => "BASE",
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A count (int) > 0 -> Visible, else Collapsed (for optional metadata sections).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true -> Wrap, false -> NoWrap (the preview text "wrap" toggle).</summary>
public sealed class BoolToTextWrappingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A full path -> just the file name (for the GRF source picker).</summary>
public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? System.IO.Path.GetFileName(s) : value;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string -> true (used to open an InfoBar when there is a message).</summary>
public sealed class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean (used for IsReadOnly = !IsEditable).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => !(value is bool b && b);
}

/// <summary>Non-null -> Visible, null -> Collapsed.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string -> Visible, else Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True -> Visible, False -> Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}
