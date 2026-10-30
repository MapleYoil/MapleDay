using System.Globalization;

namespace MapleDay.Core;

public static class BossParty
{
    public static int Maximum(string name) => SchedulerIconAssets.BossFile(name) is
        "Bosses/icon_35.png" or "Bosses/icon_37.png" or "Bosses/icon_33.png" or "Bosses/icon_41.png" or "Bosses/icon_38.png" or "Bosses/icon_34.png" ? 3 : 6;
    public static int Clamp(string name, int size) => Math.Clamp(size, 1, Maximum(name));
    public static string RecordId(string name, BossCycle cycle, DateOnly date) =>
        $"{cycle}:{SchedulerBossHistory.Start(cycle, date):yyyy-MM-dd}:{SchedulerBossHistory.BossKey(name)}";

    public static string PreferenceId(string recordId)
    {
        return TryParseRecordId(recordId, out var cycle, out _, out var boss)
            ? $"Default:{cycle}:{boss}"
            : $"Default:{recordId}";
    }

    public static int Resolve(IReadOnlyDictionary<string, int> sizes, string owner, string recordId, DateOnly today)
    {
        var exactKey = owner + "|" + recordId;
        if (sizes.TryGetValue(exactKey, out var exact)) return exact;
        if (!TryParseRecordId(recordId, out var cycle, out var start, out var boss)
            || start != SchedulerBossHistory.Start(cycle, today)) return 1;

        if (sizes.TryGetValue(owner + "|" + PreferenceId(recordId), out var preferred)) return preferred;

        var prefix = owner + "|";
        var latest = sizes.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)
                && TryParseRecordId(pair.Key[prefix.Length..], out var savedCycle, out var savedStart, out var savedBoss)
                && savedCycle == cycle && savedStart < start && savedBoss == boss)
            .Select(pair => (Start: ParseStart(pair.Key[prefix.Length..]), pair.Value))
            .Where(pair => pair.Start is not null)
            .OrderByDescending(pair => pair.Start)
            .FirstOrDefault();
        return latest.Start is null ? 1 : latest.Value;
    }

    private static DateOnly? ParseStart(string recordId) =>
        TryParseRecordId(recordId, out _, out var start, out _) ? start : null;

    private static bool TryParseRecordId(string recordId, out BossCycle cycle, out DateOnly start, out string boss)
    {
        cycle = default; start = default; boss = "";
        var parts = recordId.Split(':', 3);
        if (parts.Length != 3 || !Enum.TryParse(parts[0], out cycle)
            || !DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)
            || parts[2].Length == 0) return false;
        boss = parts[2];
        return true;
    }

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
