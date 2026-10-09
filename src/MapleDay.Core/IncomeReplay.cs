namespace MapleDay.Core;

public sealed record ReplayClear(DateOnly Date, string Boss, long Meso);
public sealed record ReplayBoss(string Name, int Count, long Meso);
public sealed record ReplayFrame(DateOnly? Date, long Total, int Count, IReadOnlyList<ReplayBoss> Bosses);

public sealed class IncomeReplay
{
    public static string CompactMeso(long meso)
    {
        var (divisor, unit, format) = meso >= 1_000_000_000_000 ? (1_000_000_000_000m, "조", "0.000")
            : meso >= 100_000_000 ? (100_000_000m, "억", "0.00")
            : meso >= 10_000 ? (10_000m, "만", "0.0") : (1m, "", "0");
        return (meso / divisor).ToString(format, System.Globalization.CultureInfo.InvariantCulture) + unit;
    }
    public IReadOnlyList<ReplayClear> Clears { get; }
    public IReadOnlyList<string> Names { get; }
    public long Total { get; }
    public IncomeReplay(IEnumerable<BossIncomeRecord> records) : this(records
        .Where(record => record.Cycle == BossCycle.Weekly && record.Included && !record.DateEstimated && record.Meso is > 0)
        .Select(record => new ReplayClear(record.Date, record.Name, record.Meso!.Value))) { }
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
        long total = 0;
        for (var i = 0; i < completed; i++)
        {
            var clear = Clears[i]; var value = totals[clear.Boss];
            totals[clear.Boss] = (value.Count + 1, value.Meso + clear.Meso); total += clear.Meso;
        }
        if (completed < Clears.Count)
        {
            var clear = Clears[completed]; var value = totals[clear.Boss];
            var partial = (long)((position - completed) * clear.Meso);
            totals[clear.Boss] = (value.Count, value.Meso + partial); total += partial;
        }
        return new(Clears.Count == 0 ? null : Clears[Math.Min(completed, Clears.Count - 1)].Date, total, completed,
            Names.Select(name => new ReplayBoss(name, totals[name].Count, totals[name].Meso)).OrderByDescending(row => row.Meso)
                .ThenByDescending(row => row.Count).ThenBy(row => Array.IndexOf(Names.ToArray(), row.Name)).ToArray());
    }
}
