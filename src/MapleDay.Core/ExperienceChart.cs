namespace MapleDay.Core;

public sealed record ExperienceChartSample(DateOnly Date, decimal? Experience, int? Level = null, decimal? Percent = null);
public sealed record ExperienceChartPoint(DateOnly Date, decimal Experience, double X, double Y, int? Level, decimal? Percent);
public sealed record ExperienceChartLayout(decimal Minimum, decimal Maximum,
    IReadOnlyList<IReadOnlyList<ExperienceChartPoint>> Segments);

public static class ExperienceChart
{
    // X and Y are normalized; Y increases downward like the native canvas.
    // Include zero in the linear scale and never connect across missing records.
    public static ExperienceChartLayout Build(IEnumerable<ExperienceChartSample> source)
    {
        var samples = source.OrderBy(sample => sample.Date).ToArray();
        var values = samples.Where(sample => sample.Experience.HasValue).Select(sample => sample.Experience!.Value).ToArray();
        var minimum = Math.Min(0, values.DefaultIfEmpty(0).Min());
        var maximum = Math.Max(0, values.DefaultIfEmpty(0).Max());
        if (minimum == maximum) maximum = minimum + 1;
        var segments = new List<IReadOnlyList<ExperienceChartPoint>>();
        var segment = new List<ExperienceChartPoint>();
        var firstDate = samples.FirstOrDefault()?.Date ?? default;
        var daySpan = samples.Length > 0 ? samples[^1].Date.DayNumber - firstDate.DayNumber : 0;
        foreach (var sample in samples)
        {
            if (sample.Experience is not { } experience)
            {
                FinishSegment();
                continue;
            }
            var x = daySpan > 0 ? (double)(sample.Date.DayNumber - firstDate.DayNumber) / daySpan : .5;
            var y = (double)((maximum - experience) / (maximum - minimum));
            segment.Add(new(sample.Date, experience, x, y, sample.Level, sample.Percent));
        }
        FinishSegment();
        return new(minimum, maximum, segments);

        void FinishSegment()
        {
            if (segment.Count == 0) return;
            segments.Add(segment.ToArray());
            segment.Clear();
        }
    }
}
