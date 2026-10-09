namespace MapleDay.Core;

public sealed record ReplayLoot(string Name, long Meso, string Icon);
public sealed record ReplayClear(DateOnly Date, string Boss, long Meso, IReadOnlyList<ReplayLoot>? Loot = null, int KillCount = 1);
public sealed record ReplayBoss(string Name, int Count, long Meso);
public sealed record ReplayLootTotal(string Name, long Meso, string Icon, int Count);
public sealed record ReplayFrame(DateOnly? Date, long Total, int Count, IReadOnlyList<ReplayBoss> Bosses, IReadOnlyList<ReplayLoot>? Loot = null, string? LootBoss = null,
    IReadOnlyList<ReplayLootTotal>? CollectedLoot = null);

public sealed class IncomeReplay
{
    public static string CompactMeso(long meso)
    {
        var (divisor, unit, format) = meso >= 1_000_000_000_000 ? (1_000_000_000_000m, "조", "0.##")
            : meso >= 100_000_000 ? (100_000_000m, "억", "0.##")
            : meso >= 10_000 ? (10_000m, "만", "0.0") : (1m, "", "0");
        return (meso / divisor).ToString(format, System.Globalization.CultureInfo.InvariantCulture) + unit;
    }
    public IReadOnlyList<ReplayClear> Clears { get; }
    public IReadOnlyList<string> Names { get; }
    public long Total { get; }
    public IncomeReplay(IEnumerable<BossIncomeRecord> records) : this(records
        .Where(record => record.Cycle == BossCycle.Weekly && record.Included && !record.DateEstimated && record.Meso is > 0)
        .Select(record => new ReplayClear(record.Date, record.Name, record.Meso!.Value))) { }
    public IncomeReplay(IEnumerable<(string Ocid, BossIncomeRecord Clear)> records, IEnumerable<BossLootRecord> loot)
        : this(WithLoot(records, loot)) { }
    private static IEnumerable<ReplayClear> WithLoot(IEnumerable<(string Ocid, BossIncomeRecord Clear)> records, IEnumerable<BossLootRecord> loot)
    {
        var remaining = loot.Where(BossLoot.Valid).Where(item => item.Received > 0).DistinctBy(item => item.Id).ToList();
        foreach (var (ocid, clear) in records.Where(row => row.Clear.Cycle == BossCycle.Weekly && !row.Clear.DateEstimated)
            .OrderBy(row => row.Clear.Date))
        {
            var drops = remaining.Where(item => BossLoot.ForClear(item, ocid, clear)).ToArray();
            foreach (var item in drops) remaining.Remove(item);
            var crystal = clear.Included ? clear.Meso ?? 0 : 0;
            if (crystal + drops.Sum(item => item.Received) > 0)
                yield return new(clear.Date, clear.Name, crystal + drops.Sum(item => item.Received), drops.Select(ReplayItem).ToArray());
        }
        // A standalone settlement (including monthly loot) contributes income, never another kill.
        foreach (var group in remaining.GroupBy(item => (item.Ocid, item.Date, item.Boss, item.ClearId)))
            yield return new(group.Key.Date, group.Key.Boss, group.Sum(item => item.Received), group.Select(ReplayItem).ToArray(), 0);
    }
    private static ReplayLoot ReplayItem(BossLootRecord item) => new(item.ItemName, item.Received,
        BossLootCatalog.Items.FirstOrDefault(entry => entry.Id == item.ItemId)?.Icon ?? "");
    public IncomeReplay(IEnumerable<ReplayClear> clears)
    {
        Clears = clears.Where(clear => clear.Meso > 0).OrderBy(clear => clear.Date).ThenBy(clear => clear.Boss, StringComparer.Ordinal).ToArray();
        Names = Clears.GroupBy(clear => clear.Boss).OrderByDescending(group => group.Sum(clear => clear.Meso))
            .ThenBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key).ToArray();
        Total = Clears.Sum(clear => clear.Meso);
    }
    public ReplayFrame At(double fraction)
    {
        var position = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * Clears.Count;
        var completed = Math.Min((int)position, Clears.Count);
        var totals = Names.ToDictionary(name => name, _ => (Count: 0, Meso: 0L));
        long total = 0; var kills = 0;
        for (var i = 0; i < completed; i++)
        {
            var clear = Clears[i]; var value = totals[clear.Boss];
            totals[clear.Boss] = (value.Count + clear.KillCount, value.Meso + clear.Meso); total += clear.Meso; kills += clear.KillCount;
        }
        if (completed < Clears.Count)
        {
            var clear = Clears[completed]; var value = totals[clear.Boss];
            var partial = (long)((position - completed) * clear.Meso);
            totals[clear.Boss] = (value.Count, value.Meso + partial); total += partial;
        }
        var active = Clears.Count == 0 ? null : Clears[Math.Min(completed, Clears.Count - 1)];
        var collected = new Dictionary<(string Name, string Icon), (long Meso, int Count)>();
        for (var i = 0; i < Clears.Count && i <= completed; i++)
        {
            var portion = i < completed ? 1 : position - completed;
            if (portion <= 0) continue;
            foreach (var item in Clears[i].Loot ?? [])
            {
                var key = (item.Name, item.Icon);
                collected.TryGetValue(key, out var value);
                collected[key] = (value.Meso + (long)(item.Meso * portion), value.Count + 1);
            }
        }
        return new(active?.Date, total, kills,
            Names.Select(name => new ReplayBoss(name, totals[name].Count, totals[name].Meso)).OrderByDescending(row => row.Meso)
                .ThenByDescending(row => row.Count).ThenBy(row => Array.IndexOf(Names.ToArray(), row.Name)).ToArray(), active?.Loot, active?.Boss,
            collected.Select(item => new ReplayLootTotal(item.Key.Name, item.Value.Meso, item.Key.Icon, item.Value.Count)).ToArray());
    }
}
