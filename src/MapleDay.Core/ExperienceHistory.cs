using System.Globalization;

namespace MapleDay.Core;

public sealed record ExperienceValue(int Level, long Exp, decimal Percent)
{
    public static ExperienceValue? From(CharacterBasic? basic)
    {
        if (basic?.Level is null) return null;
        if (basic.Level is < 1 or > 300 || basic.Exp is null or < 0
            || !decimal.TryParse(basic.ExpRate, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) || rate is < 0 or > 100)
            throw new InvalidDataException("경험치 응답을 읽을 수 없습니다.");
        return new(basic.Level.Value, basic.Exp.Value, rate);
    }
}
public sealed record ExperienceSnapshot(DateOnly Date, ExperienceValue? Value, bool Final, DateTimeOffset FetchedAt);
public sealed record ExperienceDay(DateOnly Date, ExperienceValue? Value, decimal? Gained, int? LevelsGained);
public sealed record ExperienceTrend(IReadOnlyList<ExperienceDay> Days, decimal? Average, decimal? AveragePercent,
    decimal? LevelUpDays, int KnownDays, DateOnly EndDate);

public static class ExperienceHistory
{
    public static readonly DateOnly FirstDate = new(2023, 12, 21);
    public static readonly TimeSpan Korea = TimeSpan.FromHours(9);
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(Korea).DateTime);
    public static DateOnly LatestFinalDate(DateTimeOffset now) => Today(now).AddDays(now.ToOffset(Korea).Hour >= 2 ? -1 : -2);
    public static DateOnly StartDate(string? created)
    {
        var date = created is { Length: >= 10 } && DateOnly.TryParseExact(created[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : FirstDate;
        return date > FirstDate ? date : FirstDate;
    }

    // Ratios are rounded by the API; use the observation for the relevant level/date
    // rather than today's EXP table for old snapshots whose requirements changed.
    public static decimal? Required(ExperienceValue value, DateOnly date, IReadOnlyList<ExperienceSnapshot> snapshots)
    {
        if (value.Level >= 300) return null;
        if (value.Exp > 0 && value.Percent > 0) return value.Exp * 100m / value.Percent;
        var observation = snapshots.Where(snapshot => snapshot.Date <= date && snapshot.Value is { Exp: > 0, Percent: > 0 }
                && snapshot.Value.Level == value.Level)
            .MaxBy(snapshot => snapshot.Date);
        return observation?.Value is { } previous ? previous.Exp * 100m / previous.Percent : null;
    }

    public static IReadOnlyList<ExperienceDay> Days(IEnumerable<ExperienceSnapshot> source)
    {
        var snapshots = source.Where(snapshot => snapshot.Final && snapshot.Date >= FirstDate)
            .GroupBy(snapshot => snapshot.Date).Select(group => group.MaxBy(snapshot => snapshot.FetchedAt)!)
            .OrderBy(snapshot => snapshot.Date).ToArray();
        var byDate = snapshots.ToDictionary(snapshot => snapshot.Date);
        return snapshots.Select(snapshot =>
        {
            decimal? gain = null;
            int? levels = null;
            if (snapshot.Value is { } value && byDate.GetValueOrDefault(snapshot.Date.AddDays(-1))?.Value is { } before)
            {
                levels = value.Level - before.Level;
                if (levels == 0) gain = (decimal)value.Exp - before.Exp;
                else if (levels > 0)
                {
                    decimal? needed = 0;
                    for (var level = before.Level; level < value.Level && needed is not null; level++)
                    {
                        var sample = level == before.Level ? before : snapshots.LastOrDefault(item => item.Date <= snapshot.Date
                            && item.Value?.Level == level)?.Value;
                        needed = sample is null ? null : needed + Required(sample, snapshot.Date.AddDays(-1), snapshots);
                    }
                    if (needed is not null) gain = needed + value.Exp - before.Exp;
                }
            }
            return new ExperienceDay(snapshot.Date, snapshot.Value, gain, levels);
        }).ToArray();
    }

    public static ExperienceDay TodayGain(IEnumerable<ExperienceSnapshot> source, DateOnly today)
    {
        var snapshots = source.ToArray();
        var live = snapshots.Where(snapshot => snapshot.Date == today && !snapshot.Final)
            .MaxBy(snapshot => snapshot.FetchedAt);
        if (live?.Value is null) return new(today, null, null, null);
        // Use the same consecutive-day/level-up calculation in isolation. The
        // live snapshot stays non-final in storage and in the seven-day trend.
        return Days(snapshots.Where(snapshot => snapshot.Date < today)
            .Append(live with { Final = true })).Last();
    }

    public static ExperienceTrend Trend(IEnumerable<ExperienceSnapshot> source, ExperienceValue? current, DateOnly today, DateOnly endDate)
    {
        var snapshots = source.ToArray();
        var days = Days(snapshots);
        var recent = days.Where(day => day.Date >= endDate.AddDays(-6) && day.Date <= endDate && day.Gained is not null).ToArray();
        decimal? average = recent.Length == 7 ? recent.Sum(day => day.Gained!.Value) / 7 : null;
        var required = current is not null ? Required(current, today, snapshots) : null;
        var percent = required > 0 && average is not null ? average * 100 / required : null;
        var remaining = required is not null && current is not null ? Math.Max(0, required.Value - current.Exp) : (decimal?)null;
        var eta = average > 0 && remaining is not null ? remaining / average : null;
        return new(days, average, percent, eta, recent.Length, endDate);
    }

    public static string Amount(decimal amount)
    {
        var absolute = Math.Abs(amount);
        var (divisor, unit) = absolute >= 10_000_000_000_000_000m ? (10_000_000_000_000_000m, "경")
            : absolute >= 1_000_000_000_000m ? (1_000_000_000_000m, "조")
            : absolute >= 100_000_000m ? (100_000_000m, "억") : absolute >= 10_000m ? (10_000m, "만") : (1m, "");
        return (amount < 0 ? "−" : "+") + (absolute / divisor).ToString("0.0", CultureInfo.InvariantCulture) + unit;
    }

    public static string LevelUpDuration(decimal days, DateOnly startDate)
    {
        // Gregorian dates repeat every 400 years. Keep the large year count in
        // decimal and only convert the remainder to DateTime's supported range.
        var wholeDays = decimal.Floor(Math.Max(0, days));
        var hours = (int)decimal.Ceiling((Math.Max(0, days) - wholeDays) * 24);
        if (hours == 24) { wholeDays++; hours = 0; }
        var cycles = decimal.Floor(wholeDays / 146097);
        var remaining = (int)(wholeDays % 146097);
        var start = new DateTime(2000 + startDate.Year % 400, startDate.Month, startDate.Day);
        var end = start.AddDays(remaining);
        var years = end.Year - start.Year;
        if (start.AddYears(years) > end) years--;
        var anniversary = start.AddYears(years);
        var months = (end.Year - anniversary.Year) * 12 + end.Month - anniversary.Month;
        if (anniversary.AddMonths(months) > end) months--;
        var residualDays = (end - anniversary.AddMonths(months)).Days;
        var totalYears = cycles * 400 + years;
        var parts = new List<string>();
        if (totalYears > 0) parts.Add(totalYears.ToString("0", CultureInfo.InvariantCulture) + "년");
        if (totalYears > 0 || months > 0) parts.Add($"{months}개월");
        parts.Add($"{residualDays}일");
        parts.Add($"{hours}시간");
        return "약 " + string.Join(" ", parts);
    }

    public static DateTimeOffset? LevelUpAt(decimal days, DateTimeOffset now)
    {
        var local = now.ToOffset(Korea);
        if (days <= 0 || days > (decimal)(DateTime.MaxValue - local.DateTime).TotalDays) return null;
        return local.AddDays((double)days);
    }
}
