namespace MapleDay.Core;

public enum BossCycle { Weekly, Monthly, Daily }
public sealed record SchedulerSnapshot(DateOnly Date, SchedulerState? State, bool Final, DateTimeOffset FetchedAt)
{
    // Retain observed completion when the repeatable quest state later returns
    // to zero. Its snapshot date scopes the evidence to one Thursday-reset week.
    public bool ExtremeMonsterParkCompleted { get; init; }
}
public sealed record BossPeriodCount(BossCycle Cycle, DateOnly Start, DateOnly End, int Cleared, int Maximum, bool CompleteRange)
{
    public string Label => Cycle switch { BossCycle.Weekly => "주간", BossCycle.Monthly => "월간", _ => "일일" };
    public string Count => $"{Cleared} / {Maximum}";
    public string Display => $"{Label} {Count}{(CompleteRange ? "" : " · 일부 기록")}";
    public string Range => Start == End ? $"{Start:yyyy-MM-dd}" : $"{Start:yyyy-MM-dd} ~ {End:MM-dd}";
}

public static class SchedulerBossHistory
{
    public static readonly DateOnly FirstDate = new(2026, 6, 25);
    public static DateOnly KoreanToday(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(9)).DateTime);
    public static DateOnly Start(BossCycle cycle, DateOnly date) => cycle switch
    {
        BossCycle.Weekly => date.AddDays(-((int)date.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7),
        BossCycle.Monthly => new(date.Year, date.Month, 1), _ => date
    };
    public static DateOnly End(BossCycle cycle, DateOnly date) => cycle switch
    {
        BossCycle.Weekly => Start(cycle, date).AddDays(6),
        BossCycle.Monthly => Start(cycle, date).AddMonths(1).AddDays(-1), _ => date
    };
    public static BossCycle? Cycle(string? cycle) => cycle switch
    {
        "bossWeekly" or "weekly" => BossCycle.Weekly,
        "bossMonthly" or "monthly" => BossCycle.Monthly,
        "bossDaily" or "daily" => BossCycle.Daily, _ => null
    };
    public static string BossKey(string name) => SchedulerIconAssets.BossFile(name)
        ?? string.Concat(name.Where(character => !char.IsWhiteSpace(character)));

    public static BossPeriodCount Count(BossCycle cycle, DateOnly date, IEnumerable<SchedulerSnapshot> snapshots,
        SchedulerState roster, DateOnly today)
    {
        var start = Start(cycle, date);
        var end = End(cycle, date);
        var through = end < today ? end : today;
        var days = snapshots.Where(snapshot => snapshot.Date >= start && snapshot.Date <= through)
            .GroupBy(snapshot => snapshot.Date).Select(group => group.OrderByDescending(snapshot => snapshot.FetchedAt).First()).ToArray();
        var registered = (roster.Bosses ?? []).Where(boss => Cycle(boss.Cycle) == cycle && SchedulerEntries.Flag(boss.Registration)
            && !string.IsNullOrWhiteSpace(boss.Name)).Select(boss => BossKey(boss.Name!)).ToHashSet(StringComparer.Ordinal);
        var cleared = days.SelectMany(snapshot => snapshot.State?.Bosses ?? []).Where(boss => Cycle(boss.Cycle) == cycle
            && SchedulerEntries.Flag(boss.Complete) && !string.IsNullOrWhiteSpace(boss.Name))
            .Select(boss => BossKey(boss.Name!)).Where(key => cycle == BossCycle.Weekly || registered.Contains(key)).Distinct(StringComparer.Ordinal).Count();
        var knownDays = days.Where(snapshot => snapshot.Final || snapshot.Date == today).Select(snapshot => snapshot.Date).ToHashSet();
        var complete = true;
        for (var day = start; day <= through; day = day.AddDays(1))
            if (!knownDays.Contains(day)) { complete = false; break; }
        return new(cycle, start, end, cycle == BossCycle.Weekly ? Math.Min(cleared, BossIncome.WeeklyCap) : cleared,
            cycle == BossCycle.Weekly ? BossIncome.WeeklyCap : registered.Count, complete);
    }

    public static IReadOnlyList<BossPeriodCount> PreviousPeriods(IEnumerable<SchedulerSnapshot> snapshots, DateOnly today)
    {
        var all = snapshots.OrderBy(snapshot => snapshot.Date).ToArray();
        var periods = new List<BossPeriodCount>();
        foreach (var cycle in new[] { BossCycle.Weekly, BossCycle.Monthly })
        {
            foreach (var group in all.Where(snapshot => snapshot.Date < Start(cycle, today)).GroupBy(snapshot => Start(cycle, snapshot.Date)))
            {
                var roster = group.LastOrDefault(snapshot => snapshot.State is not null)?.State;
                if (roster is not null) periods.Add(Count(cycle, group.Key, all, roster, today));
            }
        }
        return periods.OrderByDescending(period => period.Start).ThenBy(period => period.Cycle).ToArray();
    }
}
