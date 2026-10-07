using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MapleDay.Views;

public sealed partial class SchedulerNumber : UserControl
{
    private static readonly Dictionary<string, int> Widths = LoadWidths();
    private readonly List<Image> _glyphImages = [];
    private readonly RectangleGeometry _clip = new();
    private string? _renderedNumber;
    private string? _renderedUnit;
    private bool _renderedWhite;

    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(nameof(Number), typeof(string), typeof(SchedulerNumber), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SchedulerNumber), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty WhiteProperty = DependencyProperty.Register(nameof(White), typeof(bool), typeof(SchedulerNumber), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty CenterProperty = DependencyProperty.Register(nameof(Center), typeof(bool), typeof(SchedulerNumber), new PropertyMetadata(false, Changed));
    public string Number { get => (string)GetValue(NumberProperty); set => SetValue(NumberProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public bool White { get => (bool)GetValue(WhiteProperty); set => SetValue(WhiteProperty, value); }
    public bool Center { get => (bool)GetValue(CenterProperty); set => SetValue(CenterProperty, value); }

    public SchedulerNumber()
    {
        InitializeComponent();
        GlyphCanvas.Clip = _clip;
        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Draw();
    }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((SchedulerNumber)sender).Draw();

    private void Draw()
    {
        if (GlyphCanvas is null) return;
        var number = Number ?? "";
        var unit = Unit ?? "";
        var glyphs = SchedulerNumberGlyphs.Layout(number, unit, Widths, ActualWidth, Center, White);
        if (number != _renderedNumber || unit != _renderedUnit || White != _renderedWhite)
        {
            GlyphCanvas.Children.Clear();
            _glyphImages.Clear();
            foreach (var glyph in glyphs)
            {
                var image = new Image { Source = LocalImageCache.Get($"Scheduler/Numbers/{glyph.Color}_{glyph.Key}.png"), Width = glyph.Width, Height = 18, Stretch = Stretch.None };
                _glyphImages.Add(image);
                GlyphCanvas.Children.Add(image);
            }
            _renderedNumber = number;
            _renderedUnit = unit;
            _renderedWhite = White;
        }
        for (var index = 0; index < glyphs.Count; index++) Canvas.SetLeft(_glyphImages[index], glyphs[index].Left);
        _clip.Rect = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight);
        AutomationProperties.SetName(this, number + unit);
    }

    private static Dictionary<string, int> LoadWidths()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Scheduler", "Numbers", "manifest.json")));
        return manifest.RootElement.GetProperty("widths").Deserialize<Dictionary<string, int>>()!;
    }
}
