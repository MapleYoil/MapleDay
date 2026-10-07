using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace MapleDay.Views;

public sealed partial class CircularBossProgress : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double),
        typeof(CircularBossProgress), new PropertyMetadata(0.0, (sender, _) => ((CircularBossProgress)sender).UpdateArc()));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public CircularBossProgress() { InitializeComponent(); UpdateArc(); }
    private void UpdateArc()
    {
        if (ProgressArc is null) return;
        var fraction = double.IsFinite(Value) ? Math.Clamp(Value, 0, 1) : 0;
        CompletedCircle.Visibility = fraction >= 1 ? Visibility.Visible : Visibility.Collapsed;
        ProgressArc.Visibility = fraction > 0 && fraction < 1 ? Visibility.Visible : Visibility.Collapsed;
        if (fraction is <= 0 or >= 1) { ProgressArc.Data = null; return; }
        var angle = fraction * Math.Tau;
        var figure = new PathFigure { StartPoint = new Point(88, 6), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(88 + Math.Sin(angle) * 82, 88 - Math.Cos(angle) * 82),
            Size = new Size(82, 82), IsLargeArc = fraction > .5, SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        ProgressArc.Data = geometry;
    }
}
