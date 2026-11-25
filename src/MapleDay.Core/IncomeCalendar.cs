using System.Globalization;

namespace MapleDay.Core;

public sealed record CalendarBossRecord(string Ocid, string CharacterName, BossIncomeRecord Boss);
public sealed record CalendarLootRecord(string CharacterName, BossLootRecord Loot);
public sealed record CalendarHuntingRecord(string CharacterName, HuntingIncomeRecord Hunting);
public sealed record CalendarExpenseRecord(string CharacterName, IncomeExpenseRecord Expense);
public enum IncomeCalendarCategory { All, Boss, Hunting, Expense }
public sealed record CalendarIncomeDay(DateOnly Date, bool InMonth, long Meso, IReadOnlyList<CalendarBossRecord> Bosses, int KnownCharacters,
    IReadOnlyList<CalendarLootRecord>? Loot = null, IReadOnlyList<CalendarHuntingRecord>? Hunting = null,
    IReadOnlyList<CalendarExpenseRecord>? Expenses = null);

public static class IncomeCalendar
{
    public static string CompactMoney(long meso)
    {
        var absolute = Math.Abs((decimal)meso);
        var (divisor, unit) = absolute >= 100_000_000 ? (100_000_000m, "억") : absolute >= 10_000 ? (10_000m, "만") : (1m, "");
        return (meso < 0 ? "-" : "+") + (absolute / divisor).ToString("0.#", CultureInfo.InvariantCulture) + unit;
    }

    public static IReadOnlyList<CalendarIncomeDay> Month(DateOnly month, IEnumerable<CalendarBossRecord> records,
        IEnumerable<IReadOnlySet<DateOnly>> knownDates, IEnumerable<CalendarLootRecord>? lootRecords = null,
        IEnumerable<CalendarHuntingRecord>? huntingRecords = null, IncomeCalendarCategory category = IncomeCalendarCategory.All,
        IEnumerable<CalendarExpenseRecord>? expenseRecords = null)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);
        var grouped = (category is IncomeCalendarCategory.Hunting or IncomeCalendarCategory.Expense ? [] : records)
            .Where(record => record.Boss.Cycle != BossCycle.Daily).GroupBy(record => record.Boss.Date)
            .ToDictionary(group => group.Key, group => group.OrderBy(record => record.CharacterName, StringComparer.Ordinal)
                .ThenByDescending(record => record.Boss.Meso).ToArray());
        var known = (category is IncomeCalendarCategory.Hunting or IncomeCalendarCategory.Expense ? [] : knownDates).ToArray();
        var loot = (category is IncomeCalendarCategory.Hunting or IncomeCalendarCategory.Expense ? [] : lootRecords ?? []).Where(record => BossLoot.Valid(record.Loot)).GroupBy(record => record.Loot.Date)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var hunting = (category is IncomeCalendarCategory.Boss or IncomeCalendarCategory.Expense ? [] : huntingRecords ?? [])
            .Where(record => HuntingIncome.Valid(record.Hunting)).DistinctBy(record => record.Hunting.Id)
            .GroupBy(record => record.Hunting.Date).ToDictionary(group => group.Key,
                group => group.OrderBy(record => record.CharacterName, StringComparer.Ordinal).ThenBy(record => record.Hunting.Id).ToArray());
        var expenses = (category is IncomeCalendarCategory.Boss or IncomeCalendarCategory.Hunting ? [] : expenseRecords ?? [])
            .Where(record => IncomeExpenses.Valid(record.Expense)).DistinctBy(record => record.Expense.Id)
            .GroupBy(record => record.Expense.Date).ToDictionary(group => group.Key,
                group => group.OrderBy(record => record.CharacterName, StringComparer.Ordinal).ThenBy(record => record.Expense.Id).ToArray());
        return Enumerable.Range(0, 42).Select(index =>
        {
            var date = start.AddDays(index);
            var bosses = grouped.GetValueOrDefault(date) ?? [];
            var items = loot.GetValueOrDefault(date) ?? [];
            var hunts = hunting.GetValueOrDefault(date) ?? [];
            var ledger = expenses.GetValueOrDefault(date) ?? [];
            var adjustment = ledger.Sum(item => item.Expense.NetAmount);
            var income = bosses.Where(record => record.Boss.Included).Sum(record => record.Boss.Meso ?? 0) + items.Sum(item => item.Loot.Received)
                + hunts.Sum(record => record.Hunting.Total);
            return new CalendarIncomeDay(date, date.Month == first.Month && date.Year == first.Year,
                income + adjustment, bosses, known.Count(dates => dates.Contains(date)), items, hunts, ledger);
        }).ToArray();
    }
}
