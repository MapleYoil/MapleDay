using System.Globalization;

namespace MapleDay.Core;

public sealed record CalendarBossRecord(string Ocid, string CharacterName, BossIncomeRecord Boss);
public sealed record CalendarLootRecord(string CharacterName, BossLootRecord Loot);
public sealed record CalendarIncomeDay(DateOnly Date, bool InMonth, long Meso, IReadOnlyList<CalendarBossRecord> Bosses, int KnownCharacters,
    IReadOnlyList<CalendarLootRecord>? Loot = null);

public static class IncomeCalendar
{
    public static string CompactMoney(long meso)
    {
        var (divisor, unit) = meso >= 100_000_000 ? (100_000_000m, "억") : meso >= 10_000 ? (10_000m, "만") : (1m, "");
        return "+" + (meso / divisor).ToString("0.#", CultureInfo.InvariantCulture) + unit;
    }

    public static IReadOnlyList<CalendarIncomeDay> Month(DateOnly month, IEnumerable<CalendarBossRecord> records,
        IEnumerable<IReadOnlySet<DateOnly>> knownDates, IEnumerable<CalendarLootRecord>? lootRecords = null)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);
        var grouped = records.Where(record => record.Boss.Cycle != BossCycle.Daily && !record.Boss.DateEstimated).GroupBy(record => record.Boss.Date)
            .ToDictionary(group => group.Key, group => group.OrderBy(record => record.CharacterName, StringComparer.Ordinal)
                .ThenByDescending(record => record.Boss.Meso).ToArray());
        var known = knownDates.ToArray();
        var loot = (lootRecords ?? []).Where(record => BossLoot.Valid(record.Loot)).GroupBy(record => record.Loot.Date)
            .ToDictionary(group => group.Key, group => group.ToArray());
        return Enumerable.Range(0, 42).Select(index =>
        {
            var date = start.AddDays(index);
            var bosses = grouped.GetValueOrDefault(date) ?? [];
            var items = loot.GetValueOrDefault(date) ?? [];
            return new CalendarIncomeDay(date, date.Month == first.Month && date.Year == first.Year,
                bosses.Where(record => record.Boss.Included).Sum(record => record.Boss.Meso ?? 0) + items.Sum(item => item.Loot.Received), bosses,
                known.Count(dates => dates.Contains(date)), items);
        }).ToArray();
    }
}
