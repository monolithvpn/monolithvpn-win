using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MonolithVpnClient.Views;

namespace MonolithVpnClient;

public class HistoryActionToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var action = value as string ?? "";
        if (action.Contains("unexpected", StringComparison.OrdinalIgnoreCase) ||
            action.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return (System.Windows.Media.Brush)Application.Current.Resources["Red"];
        if (action.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("Reconnected", StringComparison.OrdinalIgnoreCase))
            return (System.Windows.Media.Brush)Application.Current.Resources["Green"];
        return (System.Windows.Media.Brush)Application.Current.Resources["TextLow"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class TagColorToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, System.Windows.Media.Color> LegacyColors = new()
    {
        ["blue"] = System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6),
        ["green"] = System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E),
        ["amber"] = System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B),
        ["red"] = System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44),
        ["gray"] = System.Windows.Media.Color.FromRgb(0x7C, 0x86, 0xA3),
    };

    private static System.Windows.Media.Color Resolve(string? value)
    {
        var raw = (value ?? "").Trim();
        if (raw.StartsWith("#"))
        {
            try
            {
                if (System.Windows.Media.ColorConverter.ConvertFromString(raw) is System.Windows.Media.Color parsed)
                    return parsed;
            }
            catch (FormatException)
            {
            }
        }
        return LegacyColors.TryGetValue(raw.ToLowerInvariant(), out var c) ? c : LegacyColors["gray"];
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = Resolve(value as string);
        var mode = (parameter as string)?.ToLowerInvariant();

        if (mode == "text")
        {
            byte Lighten(byte c) => (byte)Math.Round(c + (255 - c) * 0.55);
            var tinted = System.Windows.Media.Color.FromRgb(Lighten(color.R), Lighten(color.G), Lighten(color.B));
            return new System.Windows.Media.SolidColorBrush(tinted);
        }

        byte alpha = mode == "border" ? (byte)0x40 : (byte)0x1A;
        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class AlertKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ToastKind.Success => (System.Windows.Media.Brush)Application.Current.Resources["Green"],
            ToastKind.Error => (System.Windows.Media.Brush)Application.Current.Resources["Red"],
            _ => (System.Windows.Media.Brush)Application.Current.Resources["TextLow"],
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value is true);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value is true);
}

public class InverseStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
