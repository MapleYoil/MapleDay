using System.Globalization;

namespace MapleDay.Core;

public sealed record CalendarBossRecord(string Ocid, string CharacterName, BossIncomeRecord Boss);
public sealed record CalendarIncomeDay(DateOnly Date, bool InMonth, long Meso, IReadOnlyList<CalendarBossRecord> Bosses, int KnownCharacters);

public static class IncomeCalendar
{
    public static string CompactMoney(long meso)
    {
        var (divisor, unit) = meso >= 100_000_000 ? (100_000_000m, "억") : meso >= 10_000 ? (10_000m, "만") : (1m, "");
        return "+" + (meso / divisor).ToString("0.#", CultureInfo.InvariantCulture) + unit;
    }

    public static IReadOnlyList<CalendarIncomeDay> Month(DateOnly month, IEnumerable<CalendarBossRecord> records,
        IEnumerable<IReadOnlySet<DateOnly>> knownDates)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);
        var grouped = records.Where(record => record.Boss.Cycle != BossCycle.Daily && !record.Boss.DateEstimated).GroupBy(record => record.Boss.Date)
            .ToDictionary(group => group.Key, group => group.OrderBy(record => record.CharacterName, StringComparer.Ordinal)
                .ThenByDescending(record => record.Boss.Meso).ToArray());
        var known = knownDates.ToArray();
        return Enumerable.Range(0, 42).Select(index =>
        {
            var date = start.AddDays(index);
            var bosses = grouped.GetValueOrDefault(date) ?? [];
            return new CalendarIncomeDay(date, date.Month == first.Month && date.Year == first.Year,
                bosses.Where(record => record.Boss.Included).Sum(record => record.Boss.Meso ?? 0), bosses,
                known.Count(dates => dates.Contains(date)));
        }).ToArray();
    }
}
