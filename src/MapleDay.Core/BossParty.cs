namespace MapleDay.Core;

public static class BossParty
{
    public static int Maximum(string name) => SchedulerIconAssets.BossFile(name) is
        "Bosses/icon_35.png" or "Bosses/icon_37.png" or "Bosses/icon_33.png" or "Bosses/icon_41.png" or "Bosses/icon_38.png" or "Bosses/icon_34.png" ? 3 : 6;
    public static int Clamp(string name, int size) => Math.Clamp(size, 1, Maximum(name));
    public static string RecordId(string name, BossCycle cycle, DateOnly date) =>
        $"{cycle}:{SchedulerBossHistory.Start(cycle, date):yyyy-MM-dd}:{SchedulerBossHistory.BossKey(name)}";

    public static IReadOnlyList<string> SavedRecordIds(IEnumerable<SchedulerSnapshot> snapshots, string name, BossCycle cycle, DateOnly today)
    {
        if (cycle is not (BossCycle.Weekly or BossCycle.Monthly)) return [];
        var key = SchedulerBossHistory.BossKey(name);
        return snapshots.Where(day => day.Date >= SchedulerBossHistory.FirstDate && day.Date <= today)
            .GroupBy(day => day.Date).Select(group => group.MaxBy(day => day.FetchedAt)!)
            .Where(day => day.State?.Bosses?.Any(boss => SchedulerBossHistory.Cycle(boss.Cycle) == cycle
                && SchedulerBossHistory.BossKey(boss.Name ?? "") == key) == true)
            .Select(day => RecordId(name, cycle, day.Date)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
