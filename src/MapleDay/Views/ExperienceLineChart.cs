using MapleDay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MapleDay.Views;

public sealed class ExperienceLineChart : UserControl
{
    private readonly Canvas _canvas = new();
    private IReadOnlyList<ExperienceChartSample> _samples = [];
    private readonly Brush _accent = (Brush)Application.Current.Resources["AccentBrush"];
    private readonly Brush _muted = (Brush)Application.Current.Resources["MutedBrush"];
    private readonly Brush _line = (Brush)Application.Current.Resources["LineBrush"];

    public ExperienceLineChart()
    {
        Content = _canvas;
        Visibility = Visibility.Collapsed;
        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Draw();
        AutomationProperties.SetName(this, "최근 7일 보유 경험치 선 그래프");
    }

    public void SetSamples(IReadOnlyList<ExperienceChartSample> samples)
    {
        _samples = samples.ToArray();
        Visibility = _samples.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Draw();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        if (_samples.Count == 0 || ActualWidth <= 100 || ActualHeight <= 60) return;
        const double left = 68, right = 18, top = 16, bottom = 36;
        var width = ActualWidth - left - right;
        var height = ActualHeight - top - bottom;
        var layout = ExperienceChart.Build(_samples);
        var points = layout.Segments.SelectMany(segment => segment).ToArray();
        AutomationProperties.SetHelpText(this, string.Join("; ", _samples.Select(sample =>
            $"{sample.Date:MM/dd}: {(sample.Experience is { } value ? ExperienceHistory.Amount(value).TrimStart('+') : "기록 없음")}")));

        if (points.Length > 0)
        {
            for (var index = 0; index <= 2; index++)
            {
                var value = layout.Maximum - (layout.Maximum - layout.Minimum) * index / 2;
                var y = top + height * index / 2;
                AddLine(left, y, left + width, y, _line, 1);
                var label = new TextBlock
                {
                    Text = value == 0 ? "0" : ExperienceHistory.Amount(value).TrimStart('+'),
                    FontSize = 11, Foreground = _muted, Width = left - 10,
                    TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis
                };
                ToolTipService.SetToolTip(label, value.ToString("N0") + " 경험치");
                Add(label, 0, y - 8);
            }
            if (layout.Minimum < 0 && layout.Maximum > 0)
            {
                var zeroY = top + height * (double)(layout.Maximum / (layout.Maximum - layout.Minimum));
                AddLine(left, zeroY, left + width, zeroY, _muted, 1);
            }
            foreach (var segment in layout.Segments)
            {
                if (segment.Count < 2) continue;
                var curve = new Polyline { Stroke = _accent, StrokeThickness = 2, IsHitTestVisible = false };
                foreach (var point in segment) curve.Points.Add(new Point(left + width * point.X, top + height * point.Y));
                _canvas.Children.Add(curve);
            }
            foreach (var point in points)
            {
                var caption = $"{point.Date:yyyy-MM-dd} · 보유 경험치 {point.Experience:N0}"
                    + (point.Level is { } level ? $" · Lv. {level}" : "")
                    + (point.Percent is { } percent ? $" ({percent:0.###}%)" : "");
                var marker = new Button
                {
                    Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                    BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    Content = new Ellipse { Width = 8, Height = 8, Fill = _accent,
                        Stroke = new SolidColorBrush(Microsoft.UI.Colors.White), StrokeThickness = 1 }
                };
                AutomationProperties.SetName(marker, caption);
                var tooltip = new ToolTip { Content = caption };
                ToolTipService.SetToolTip(marker, tooltip);
                marker.GotFocus += (_, _) => tooltip.IsOpen = true;
                marker.LostFocus += (_, _) => tooltip.IsOpen = false;
                Add(marker, left + width * point.X - 12, top + height * point.Y - 12);
            }
        }
        else
        {
            var empty = new TextBlock
            {
                Text = "비교 가능한 경험치 기록이 없어요.", FontSize = 12, Foreground = _muted,
                Width = width, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
            };
            Add(empty, left, top + height / 2 - 10);
        }

        var span = _samples[^1].Date.DayNumber - _samples[0].Date.DayNumber;
        foreach (var sample in _samples)
        {
            var x = left + width * (span > 0 ? (double)(sample.Date.DayNumber - _samples[0].Date.DayNumber) / span : .5);
            var date = new TextBlock { Text = sample.Date.ToString("MM/dd"), Width = 44,
                FontSize = 11, Foreground = _muted, TextAlignment = TextAlignment.Center };
            Add(date, x - 22, top + height + 14);
            if (sample.Experience is not null) continue;
            var missing = new TextBlock { Text = "—", Width = 20, Foreground = _muted, TextAlignment = TextAlignment.Center };
            ToolTipService.SetToolTip(missing, $"{sample.Date:yyyy-MM-dd} · 비교 기록 없음");
            Add(missing, x - 10, top + height - 20);
        }
    }

    private void Add(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        _canvas.Children.Add(element);
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness)
        => _canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false });
}
