using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;
using Color = Windows.UI.Color;
using System.Text.Json;
using FontStyle = Windows.UI.Text.FontStyle;
using FontStretch = Windows.UI.Text.FontStretch;

namespace MapleDay.Views;

/// <summary>Game frame, typography and inline option colors for the hover popup.</summary>
internal static class EquipmentTooltipCard
{
    private const string Prefix = "Equipment/UIToolTip_Item_Equip/";
    private static readonly Dictionary<string, OriginalFont> Fonts = JsonSerializer.Deserialize<Dictionary<string, OriginalFont>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Equipment", "UIToolTip_Item_Equip", "fonts.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static readonly Color White = Colors.White;
    private static readonly Color Muted = ColorHelper.FromArgb(255, 183, 191, 197);
    private static readonly Color Gold = ColorHelper.FromArgb(255, 255, 204, 0);
    private static readonly Color Cyan = ColorHelper.FromArgb(255, 10, 227, 173);

    public static Color GradeColor(string? grade) => EquipmentDetails.GradeKey(grade) switch
    {
        "rare" => ColorHelper.FromArgb(255, 102, 255, 255), "epic" => ColorHelper.FromArgb(255, 187, 119, 255),
        "unique" => Gold, "legendary" => ColorHelper.FromArgb(255, 204, 255, 0), _ => Muted
    };
    public static Image Sprite(string name, double? width = null, double? height = null) => new()
    {
        Source = LocalImageCache.Get(Prefix + name + ".png"), Width = width ?? double.NaN,
        Height = height ?? double.NaN, Stretch = Stretch.Fill, IsHitTestVisible = false
    };

    public static FrameworkElement Gear(EquipmentItem item, Action<Image, string?> load, bool equipped = false)
    {
        var content = Body();
        var stars = EquipmentDetails.Stars(item);
        if (stars > 0)
        {
            var rows = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            for (var row = 0; row < (stars + 14) / 15; row++)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
                for (var i = row * 15; i < Math.Min(stars, (row + 1) * 15); i++)
                {
                    var star = Sprite("textIcon_starForce_enhanced", 11, 10);
                    if (i % 5 == 0 && i % 15 != 0) star.Margin = new Thickness(10, 0, 0, 0);
                    line.Children.Add(star);
                }
                rows.Children.Add(line);
            }
            content.Children.Add(rows);
        }
        Heading(content, EquipmentDetails.Title(item));
        var expiry = EquipmentDetails.UpgradeLines(item).Where(line => line.StartsWith("사용 기간", StringComparison.Ordinal));
        foreach (var line in expiry) { var text = Text(line, Gold); text.TextAlignment = TextAlignment.Center; content.Children.Add(text); }
        GearHeader(content, item, load, equipped);
        Divider(content);
        foreach (var stat in EquipmentDetails.StatLines(item))
        {
            content.Children.Add(Stat(stat));
        }
        foreach (var line in EquipmentDetails.UpgradeLines(item).Where(line => !line.StartsWith("스타포스", StringComparison.Ordinal)
            && !line.StartsWith("요구 레벨", StringComparison.Ordinal) && !line.StartsWith("사용 기간", StringComparison.Ordinal)))
            content.Children.Add(Text(line, White));
        Potential(content, "잠재능력", item.Grade, [item.Potential1, item.Potential2, item.Potential3], false);
        Potential(content, "에디셔널 잠재능력", item.AdditionalGrade, [item.Additional1, item.Additional2, item.Additional3], true);
        if (!string.IsNullOrWhiteSpace(item.SoulName) || !string.IsNullOrWhiteSpace(item.SoulOption))
        {
            Divider(content);
            if (!string.IsNullOrWhiteSpace(item.SoulName)) content.Children.Add(Text("♟ 소울 : " + item.SoulName, White));
            if (!string.IsNullOrWhiteSpace(item.SoulOption)) content.Children.Add(Text(item.SoulOption, White));
        }
        if (!string.IsNullOrWhiteSpace(item.Description)) { Divider(content); content.Children.Add(Text(item.Description, Muted)); }
        return Frame(content);
    }

    public static FrameworkElement Cash(CashEquipmentItem item, Action<Image, string?> load)
        => Simple(item.Name, item.Icon, load, [item.Part, item.Label, item.Description,
            item.Expires is { Length: > 0 } expires ? expires == "expired" ? "사용 기간 만료" : "사용 기간: " + expires : null]);

    public static FrameworkElement Symbol(EquipmentSymbol symbol, Action<Image, string?> load)
    {
        var content = Body(); Heading(content, symbol.Name ?? "심볼");
        IconHeader(content, symbol.Icon, $"Lv. {symbol.Level} · 포스 +{symbol.Force}", load);
        foreach (var (label, value, percent) in new[] { ("STR", symbol.Str, false), ("DEX", symbol.Dex, false), ("INT", symbol.Int, false), ("LUK", symbol.Luk, false), ("HP", symbol.Hp, false),
            ("경험치 획득량", symbol.Exp, true), ("메소 획득량", symbol.Meso, true), ("아이템 드롭률", symbol.Drop, true) })
            if (EquipmentSymbol.Number(value) > 0) content.Children.Add(Text(label + " +" + value?.TrimEnd('%') + (percent ? "%" : ""), Cyan));
        Divider(content);
        content.Children.Add(Text(symbol.Level >= 20 && symbol.Kind == SymbolKind.Arcane ? "MAX" : $"성장 경험치 {symbol.Growth:N0} / {symbol.RequiredGrowth:N0}", Gold));
        if (!string.IsNullOrWhiteSpace(symbol.Description)) content.Children.Add(Text(symbol.Description, Muted));
        return Frame(content);
    }

    public static FrameworkElement Simple(string? title, string? icon, Action<Image, string?> load, IEnumerable<string?> lines)
    {
        var content = Body(); Heading(content, title ?? "아이템");
        if (!string.IsNullOrEmpty(icon)) IconHeader(content, icon, null, load);
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line))) content.Children.Add(Text(line!, White));
        return Frame(content);
    }

    private static void Potential(StackPanel content, string label, string? grade, string?[] options, bool additional)
    {
        if (string.IsNullOrWhiteSpace(grade) && options.All(string.IsNullOrWhiteSpace)) return;
        Divider(content);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        row.Children.Add(Sprite("textIcon_" + (additional ? "additionalPotential" : "potential") + "_title_" + EquipmentDetails.GradeKey(grade), 10, 10));
        row.Children.Add(Text(label + (grade is null ? "" : " : " + grade), GradeColor(grade)));
        content.Children.Add(row);
        foreach (var option in options.Where(option => !string.IsNullOrWhiteSpace(option))) content.Children.Add(Text("▪ " + option, White));
    }
    private static StackPanel Body() => new() { Spacing = 2, Padding = new Thickness(15, 10, 15, 12), Width = 324 };
    private static void Heading(StackPanel content, string title)
    {
        var text = OriginalText(title, "common/itemName");
        text.TextAlignment = TextAlignment.Center; text.Margin = new Thickness(0, 4, 0, 3); content.Children.Add(text);
    }

    private static void GearHeader(StackPanel content, EquipmentItem item, Action<Image, string?> load, bool equipped)
    {
        var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 6, 0, 2), MinHeight = 90 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var stage = new Grid { Width = 80, Height = 80, VerticalAlignment = VerticalAlignment.Top };
        stage.Children.Add(Sprite("itemIcon_base", 80, 80));
        var image = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform }; stage.Children.Add(image); load(image, item.Icon); row.Children.Add(stage);
        var description = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (equipped)
        {
            description.Children.Add(Sprite("imgFont_atkPow_equipped", 159, 20));
        }
        description.Children.Add(Text(item.Part ?? item.Slot, Muted));
        var required = EquipmentDetails.UpgradeLines(item).FirstOrDefault(line => line.StartsWith("요구 레벨", StringComparison.Ordinal));
        if (required is not null) description.Children.Add(Text(required, White));
        if (item.SpecialRingLevel > 0) description.Children.Add(Text("특수 스킬 반지 Lv. " + item.SpecialRingLevel, Gold));
        Grid.SetColumn(description, 1); row.Children.Add(description); content.Children.Add(row);
    }

    private static TextBlock Stat(EquipmentStat stat)
    {
        var text = Text("", White);
        text.Inlines.Add(new Run { Text = stat.Label + "   +" + stat.Value, Foreground = new SolidColorBrush(White) });
        if (stat.Base is not null && stat.Components.Count > 1)
        {
            text.Inlines.Add(new Run { Text = "  (", Foreground = new SolidColorBrush(White) });
            var first = true;
            foreach (var part in stat.Components)
            {
                var color = part.Kind switch { "starforce" => Gold, "scroll" => ColorHelper.FromArgb(255, 175, 173, 255), "bonus" => Cyan,
                    "exceptional" => ColorHelper.FromArgb(255, 255, 51, 51), _ => White };
                text.Inlines.Add(new Run { Text = (first || part.Value < 0 ? "" : "+") + EquipmentStat.Format(part.Value) + (stat.Percent ? "%" : ""), Foreground = new SolidColorBrush(color) });
                first = false;
            }
            text.Inlines.Add(new Run { Text = ")", Foreground = new SolidColorBrush(White) });
        }
        return text;
    }
    private static void IconHeader(StackPanel content, string? icon, string? label, Action<Image, string?> load)
    {
        var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var stage = new Grid { Width = 64, Height = 64 }; stage.Children.Add(Sprite("itemIcon_base", 64, 64));
        var image = new Image { Width = 54, Height = 54, Stretch = Stretch.Uniform }; stage.Children.Add(image); load(image, icon);
        row.Children.Add(stage);
        var text = Text(label ?? "", Muted); text.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(text, 1); row.Children.Add(text);
        content.Children.Add(row);
    }
    private static void Divider(StackPanel content)
    {
        var line = Sprite("frame_common_line", 294, 3); line.Margin = new Thickness(0, 3, 0, 3); content.Children.Add(line);
    }
    private static TextBlock Text(string text, Color color)
    {
        var block = OriginalText(text, "common/normal");
        block.Foreground = new SolidColorBrush(color); return block;
    }
    internal static TextBlock OriginalText(string text, string style)
    {
        var original = Fonts[style];
        return new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(original.Family == "MD모리스9" ? "ms-appx:///Assets/Fonts/Morris9.ttf#MDmorris9" : original.Family),
            FontSize = original.Size, FontWeight = original.Bold == 1 ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = FontStyle.Normal, FontStretch = FontStretch.Normal, CharacterSpacing = 0,
            IsTextScaleFactorEnabled = false, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(Convert.ToByte(original.Color[..2], 16), Convert.ToByte(original.Color[2..4], 16),
                Convert.ToByte(original.Color[4..6], 16), Convert.ToByte(original.Color[6..8], 16)))
        };
    }
    private sealed record OriginalFont(string Source, string Family, double Size, int Bold, string Color);
    private static FrameworkElement Frame(StackPanel content)
    {
        return new TooltipFrame(content);
    }

    // The content determines the height. Background image DesiredSize must never size rows.
    private sealed class TooltipFrame : Panel
    {
        private readonly StackPanel _content;
        public TooltipFrame(StackPanel content)
        {
            _content = content; Width = EquipmentTooltipLayout.Width;
            Children.Add(Sprite("frame_common_top")); Children.Add(Sprite("frame_common_mid")); Children.Add(Sprite("frame_common_btm"));
            Children.Add(content);
        }
        protected override Size MeasureOverride(Size availableSize)
        {
            _content.Measure(new Size(EquipmentTooltipLayout.Width, double.PositiveInfinity));
            var height = Math.Max(42, _content.DesiredSize.Height);
            var plan = EquipmentTooltipLayout.Frame(height);
            var boxes = new[] { plan.Top, plan.Middle, plan.Bottom };
            for (var i = 0; i < 3; i++) Children[i].Measure(new Size(boxes[i].Width, boxes[i].Height));
            return new Size(EquipmentTooltipLayout.Width, height);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var plan = EquipmentTooltipLayout.Frame(finalSize.Height);
            var boxes = new[] { plan.Top, plan.Middle, plan.Bottom };
            for (var i = 0; i < 3; i++) Children[i].Arrange(new Rect(boxes[i].X, boxes[i].Y, boxes[i].Width, boxes[i].Height));
            _content.Arrange(new Rect(0, 0, EquipmentTooltipLayout.Width, finalSize.Height));
            return finalSize;
        }
    }
}
