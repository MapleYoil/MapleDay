namespace MapleDay.Core;

public sealed record ForecastBoss(string Name, string Difficulty, int PartySize, long? Meso, bool Included);
public sealed record WeeklyIncomeForecast(long Meso, int RemainingSlots, int Unpriced, IReadOnlyList<ForecastBoss> Bosses)
{
    public static WeeklyIncomeForecast Calculate(SchedulerState state, IEnumerable<SchedulerSnapshot> snapshots,
        IEnumerable<BossIncomeRecord> records, DateOnly today, Func<string, int>? partySize = null, int level = int.MaxValue)
    {
        var start = SchedulerBossHistory.Start(BossCycle.Weekly, today);
        var completed = snapshots.Where(day => day.Date >= start && day.Date <= today)
            .GroupBy(day => day.Date).Select(group => group.MaxBy(day => day.FetchedAt)!)
            .SelectMany(day => day.State?.Bosses ?? []).Concat(state.Bosses ?? [])
            .Where(boss => SchedulerBossHistory.Cycle(boss.Cycle) == BossCycle.Weekly && SchedulerEntries.Flag(boss.Complete)
                && !string.IsNullOrWhiteSpace(boss.Name)).Select(boss => SchedulerBossHistory.BossKey(boss.Name!)).ToHashSet();
        completed.UnionWith(records.Where(record => record.Cycle == BossCycle.Weekly && record.PeriodStart == start
            && record.Date <= today).Select(record => SchedulerBossHistory.BossKey(record.Name)));
        var used = Math.Max(completed.Count, (int)Math.Clamp(state.WeeklyBossClearCount ?? 0, 0, BossIncome.WeeklyCap));
        var slots = Math.Max(0, BossIncome.WeeklyCap - used);
        var pending = (state.Bosses ?? []).Where(boss => SchedulerBossHistory.Cycle(boss.Cycle) == BossCycle.Weekly
            && SchedulerEntries.Flag(boss.Registration) && !string.IsNullOrWhiteSpace(boss.Name)
            && !completed.Contains(SchedulerBossHistory.BossKey(boss.Name!))
            && level >= SchedulerRequirements.BossRequiredLevel(boss.Name!))
            .Select(boss =>
            {
                var party = BossParty.Clamp(boss.Name!, partySize?.Invoke(BossParty.RecordId(boss.Name!, BossCycle.Weekly, today)) ?? 1);
                return new ForecastBoss(boss.Name!, CrystalPrices.DifficultyKey(boss.Difficulty), party,
                    CrystalPrices.Find(boss.Name!, boss.Difficulty, today)?.Meso / party, false);
            }).GroupBy(boss => SchedulerBossHistory.BossKey(boss.Name)).Select(group => group.MaxBy(boss => boss.Meso ?? -1)!)
            .OrderByDescending(boss => boss.Meso ?? -1).Select((boss, index) => boss with { Included = index < slots }).ToArray();
        return new(pending.Where(boss => boss.Included).Sum(boss => boss.Meso ?? 0), slots,
            pending.Count(boss => boss.Meso is null), pending);
    }
}
