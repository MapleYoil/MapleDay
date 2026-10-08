using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MapleDay.Services;

/// <summary>Keep shared brushes alive so existing cards, charts and templates update in place.</summary>
internal static class AppTheme
{
    private static readonly Dictionary<string, string> DarkColors = new()
    {
        ["PageBrush"] = "#1C1D21", ["CardBrush"] = "#26282E", ["StageBrush"] = "#303239",
        ["InkBrush"] = "#F2F2EF", ["MutedBrush"] = "#B8BAB5", ["LineBrush"] = "#41434A",
        ["AccentBrush"] = "#75A7FF", ["AccentSoftBrush"] = "#283A57",
        ["SchedulerSelectedBrush"] = "#393B42", ["AccentTextBrush"] = "#91B8FF",
        ["ReadBrush"] = "#303238", ["ReadTextBrush"] = "#B0B3AD",
        ["DangerTextBrush"] = "#FFABA3", ["DangerSoftBrush"] = "#482C2F",
        ["DangerLineBrush"] = "#87504F", ["NegativeBrush"] = "#FFAD80",
        ["TextControlPlaceholderForeground"] = "#B8BAB5"
    };
    private static readonly Dictionary<string, Color> LightColors = [];

    public static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.Resources[key];

    public static void Apply(bool dark)
    {
        foreach (var (key, hex) in DarkColors)
        {
            var brush = Brush(key);
            LightColors.TryAdd(key, brush.Color);
            brush.Color = dark ? Parse(hex) : LightColors[key];
        }
    }

    private static Color Parse(string hex)
    {
        var value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}

public sealed class ReadTicketBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var keys = ((string)parameter).Split('|');
        return AppTheme.Brush(value is true ? keys[0] : keys[1]);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
