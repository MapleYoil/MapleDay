using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class ExperienceChartTests
{
    private static readonly DateOnly Start = new(2026, 9, 30);

    [Fact]
    public void PositiveExperienceUsesLinearScaleFromZeroAndDatesStayChronological()
    {
        var layout = ExperienceChart.Build([
            new(Start.AddDays(6), 10_000_000_000_000m),
            new(Start, 0), new(Start.AddDays(3), 5_000_000_000_000m)]);
        Assert.Equal(0m, layout.Minimum);
        Assert.Equal(10_000_000_000_000m, layout.Maximum);
        var points = Assert.Single(layout.Segments);
        Assert.Equal(new[] { 0.0, .5, 1.0 }, points.Select(point => point.X));
        Assert.Equal(new[] { 1.0, .5, 0.0 }, points.Select(point => point.Y));
        Assert.Equal(Start, points[0].Date);
    }

    [Fact]
    public void MissingDayBreaksTheLineWithoutInventingZeroExperience()
    {
        var layout = ExperienceChart.Build([
            new(Start, 10), new(Start.AddDays(1), 20),
            new(Start.AddDays(2), null), new(Start.AddDays(3), 30)]);
        Assert.Equal(2, layout.Segments.Count);
        Assert.Equal(2, layout.Segments[0].Count);
        Assert.Single(layout.Segments[1]);
        Assert.Equal(3, layout.Segments.Sum(segment => segment.Count));
        Assert.DoesNotContain(layout.Segments.SelectMany(segment => segment), point => point.Date == Start.AddDays(2));
        Assert.Equal(1.0, layout.Segments[1][0].X);
    }

    [Fact]
    public void LevelUpResetShowsTheActualExperienceDropAndKeepsLevelInformation()
    {
        var layout = ExperienceChart.Build([
            new(Start, 900, 291, 90), new(Start.AddDays(1), 0, 292, 0), new(Start.AddDays(2), 450, 292, 45)]);
        Assert.Equal(0m, layout.Minimum);
        Assert.Equal(900m, layout.Maximum);
        var points = Assert.Single(layout.Segments);
        Assert.Equal(new[] { 0.0, 1.0, .5 }, points.Select(point => point.Y));
        Assert.Equal(new int?[] { 291, 292, 292 }, points.Select(point => point.Level));
        Assert.Equal(45m, points[2].Percent);
    }

    [Fact]
    public void EmptyMissingAndZeroOnlySeriesHaveNoInvalidCoordinates()
    {
        Assert.Empty(ExperienceChart.Build([]).Segments);
        Assert.Empty(ExperienceChart.Build([new(Start, null)]).Segments);
        var single = Assert.Single(Assert.Single(ExperienceChart.Build([new(Start, 0)]).Segments));
        Assert.Equal(.5, single.X);
        Assert.True(double.IsFinite(single.Y));
        Assert.Equal(0, single.Experience);
        var zeros = Assert.Single(ExperienceChart.Build([new(Start, 0), new(Start.AddDays(1), 0)]).Segments);
        Assert.Equal(zeros[0].Y, zeros[1].Y);
    }
}
