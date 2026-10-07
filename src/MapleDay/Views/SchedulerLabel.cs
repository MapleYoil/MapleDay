using System.Runtime.InteropServices.WindowsRuntime;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MapleDay.Views;

public sealed class SchedulerLabel : UserControl
{
    private readonly Image _image = new() { Stretch = Stretch.None };
    private static readonly SchedulerLabelPixels Renderer = new(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "NanumGothicBold.ttf"));
    private static readonly Dictionary<(string Text, int Width, int Height, uint Color, double StrokeStrength), WriteableBitmap> Cache = [];
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(SchedulerLabel), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(SolidColorBrush), typeof(SchedulerLabel), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty StrokeStrengthProperty = DependencyProperty.Register(nameof(StrokeStrength), typeof(double), typeof(SchedulerLabel), new PropertyMetadata(1.5, Changed));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public SolidColorBrush? TextBrush { get => (SolidColorBrush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public double StrokeStrength { get => (double)GetValue(StrokeStrengthProperty); set => SetValue(StrokeStrengthProperty, value); }
    public SchedulerLabel() { Content = _image; UseLayoutRounding = true; SizeChanged += (_, _) => Draw(); Loaded += (_, _) => Draw(); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((SchedulerLabel)sender).Draw();
    private void Draw()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var color = TextBrush?.Color ?? Microsoft.UI.ColorHelper.FromArgb(255, 59, 84, 110);
        var key = (Text ?? "", (int)ActualWidth, (int)ActualHeight, (uint)(color.R | color.G << 8 | color.B << 16), StrokeStrength);
        if (!Cache.TryGetValue(key, out var bitmap))
        {
            var pixels = Renderer.Render(key.Item1, key.Item2, key.Item3, color.R, color.G, color.B, key.Item5);
            bitmap = new WriteableBitmap(key.Item2, key.Item3);
            using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
            bitmap.Invalidate();
            Cache.Add(key, bitmap);
        }
        if (!ReferenceEquals(_image.Source, bitmap)) _image.Source = bitmap;
        AutomationProperties.SetName(this, Text);
    }
}
