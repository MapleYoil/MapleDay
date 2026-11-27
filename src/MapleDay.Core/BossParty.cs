using System.Globalization;

namespace MapleDay.Core;

public static class BossParty
{
    private const string DatedPreference = "Preference:";
    private const string PeriodOverride = "Override:";
    private const string LegacyPreference = "LegacyPreference:";

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
        var prefix = owner + "|";
        if (sizes.TryGetValue(prefix + recordId, out var exact)) return exact;
        if (!TryParseRecordId(recordId, out var cycle, out var start, out var boss)) return 1;
        var currentStart = SchedulerBossHistory.Start(cycle, today);
        if (start > currentStart) return 1;

        (DateOnly Start, int Size)? preference = null;
        (DateOnly Start, int Size)? legacy = null;
        foreach (var (key, size) in sizes)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var savedId = key[prefix.Length..];
            var dated = savedId.StartsWith(DatedPreference, StringComparison.Ordinal);
            var preserved = savedId.StartsWith(LegacyPreference, StringComparison.Ordinal);
            if (dated) savedId = savedId[DatedPreference.Length..];
            else if (preserved) savedId = savedId[LegacyPreference.Length..];
            if (!savedId.EndsWith(boss, StringComparison.Ordinal)) continue;
            if (!TryParseRecordId(savedId, out var savedCycle, out var savedStart, out var savedBoss)
                || savedCycle != cycle || savedBoss != boss || savedStart > start) continue;
            if (dated)
            {
                if (preference is null || savedStart > preference.Value.Start) preference = (savedStart, size);
            }
            else if ((preserved || !sizes.ContainsKey(prefix + PeriodOverride + savedId))
                && (legacy is null || savedStart > legacy.Value.Start))
            {
                legacy = (savedStart, size);
            }
        }

        if (preference is { } saved) return saved.Size;
        // Old defaults have no effective date. Only use them for the current
        // period, and use dated records to recover missing historical sizes.
        if (start == currentStart && sizes.TryGetValue(prefix + PreferenceId(recordId), out var preferred))
            return preferred;
        return legacy?.Size ?? 1;
    }

    public static void SetCurrentPreference(IDictionary<string, int> sizes, string owner, string recordId, int size, DateOnly today)
    {
        if (!TryParseRecordId(recordId, out var cycle, out _, out var boss))
        {
            SetPeriodSize(sizes, owner, recordId, size);
            return;
        }
        var currentId = $"{cycle}:{SchedulerBossHistory.Start(cycle, today):yyyy-MM-dd}:{boss}";
        var prefix = owner + "|";
        sizes[prefix + currentId] = size;
        sizes[prefix + DatedPreference + currentId] = size;
        sizes[prefix + PreferenceId(currentId)] = size;
        sizes.Remove(prefix + PeriodOverride + currentId);
        sizes.Remove(prefix + LegacyPreference + currentId);
    }

    public static void SetPeriodSize(IDictionary<string, int> sizes, string owner, string recordId, int size)
    {
        var prefix = owner + "|";
        var key = prefix + recordId;
        var overrideKey = prefix + PeriodOverride + recordId;
        // Keep the old inferred default when an undated setting is edited
        // for one period, and retain the original key for older app versions.
        if (!sizes.ContainsKey(prefix + DatedPreference + recordId) && !sizes.ContainsKey(overrideKey)
            && sizes.TryGetValue(key, out var previous))
            sizes[prefix + LegacyPreference + recordId] = previous;
        sizes[key] = size;
        sizes[overrideKey] = size;
    }

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
