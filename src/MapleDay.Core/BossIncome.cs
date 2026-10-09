using System.Globalization;
using System.Text.Json;

namespace MapleDay.Core;

public sealed record CrystalPrice(string Name, string Difficulty, DateOnly EffectiveFrom, long Meso, string Source);

public static class CrystalPrices
{
    public static readonly DateOnly CheckedOn = new(2026, 10, 6);
    public static IReadOnlyList<CrystalPrice> All { get; } = Load();
    public static string DifficultyKey(string? difficulty) => difficulty?.Trim().ToLowerInvariant() switch
    {
        "이지" => "easy", "노멀" => "normal", "하드" => "hard", "카오스" => "chaos", "익스트림" => "extreme",
        var value => value ?? ""
    };
    public static CrystalPrice? Find(string name, string? difficulty, DateOnly date)
        => All.Where(price => SchedulerBossHistory.BossKey(price.Name) == SchedulerBossHistory.BossKey(name)
            && price.Difficulty == DifficultyKey(difficulty) && price.EffectiveFrom <= date)
            .MaxBy(price => price.EffectiveFrom);
    private static IReadOnlyList<CrystalPrice> Load()
    {
        using var stream = typeof(CrystalPrices).Assembly.GetManifestResourceStream("MapleDay.Core.CrystalPrices.json")!;
        var prices = JsonSerializer.Deserialize<List<CrystalPrice>>(stream)!;
        if (prices.Count == 0 || prices.Any(price => price.Meso <= 0)
            || prices.GroupBy(price => (SchedulerBossHistory.BossKey(price.Name), price.Difficulty, price.EffectiveFrom)).Any(group => group.Count() > 1))
            throw new InvalidDataException("Invalid crystal price catalog.");
        return prices.AsReadOnly();
    }
}

public sealed record BossIncomeRecord(string Id, string Name, string Difficulty, BossCycle Cycle, DateOnly PeriodStart,
    DateOnly Date, DateOnly EarliestDate, bool DateEstimated, CrystalPrice? Price, int PartySize, long? Meso, bool Included)
{
    public bool Manual { get; init; }
    public string DifficultyLabel => Difficulty switch { "easy" => "이지", "normal" => "노멀", "hard" => "하드", "chaos" => "카오스", "extreme" => "익스트림", _ => Difficulty };
}

public sealed record IncomePeriod(BossCycle Cycle, DateOnly Start, DateOnly End, long Meso, int Count, bool CompleteRange);
public sealed record BossIncomeResult(IReadOnlyList<BossIncomeRecord> Records, DateOnly Today, DateOnly? FirstDate, IReadOnlySet<DateOnly> KnownDates)
{
    public int KnownDays => KnownDates.Count;
    public long Weekly => Sum(SchedulerBossHistory.Start(BossCycle.Weekly, Today), Today);
    public long Monthly => Sum(SchedulerBossHistory.Start(BossCycle.Monthly, Today), Today);
    public long Total => Records.Where(record => record.Included).Sum(record => record.Meso ?? 0);
    public int Unpriced => Records.Count(record => record.Price is null);
    public int CapExcluded => Records.Count(record => record.Price is not null && !record.Included);
    public bool CompleteRange(BossCycle cycle)
        => CompleteRange(SchedulerBossHistory.Start(cycle, Today), Today);
    private bool CompleteRange(DateOnly start, DateOnly end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
            if (!KnownDates.Contains(date)) return false;
        return true;
    }
    public long Sum(DateOnly start, DateOnly end) => Records.Where(record => record.Included && record.Date >= start && record.Date <= end).Sum(record => record.Meso ?? 0);
    public IReadOnlyList<IncomePeriod> Periods(BossCycle cycle)
        => KnownDates.Concat(Records.Select(record => record.Date)).Select(date => SchedulerBossHistory.Start(cycle, date))
            .Distinct().OrderDescending().Select(start =>
            {
                var end = SchedulerBossHistory.End(cycle, start);
                var records = Records.Where(record => record.Date >= start && record.Date <= end && record.Included).ToArray();
                return new IncomePeriod(cycle, start, end, records.Sum(record => record.Meso ?? 0),
                    records.Count(record => record.Meso is not null), CompleteRange(start, end < Today ? end : Today));
            }).ToArray();
}

public static class BossIncome
{
    public const int WeeklyCap = 12;
    public static string Money(long meso) => (meso / 100_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "억 메소";

    // Each call is one character. The cap is applied per Thursday period before
    // calendar-month filtering, including when a week crosses a month boundary.
    public static BossIncomeResult Calculate(IEnumerable<SchedulerSnapshot> snapshots, DateOnly today,
        Func<string, int>? partySize = null, IEnumerable<ManualWeeklyClear>? manual = null, IEnumerable<BossClearChange>? changes = null)
    {
        var days = snapshots.Where(snapshot => snapshot.Date >= SchedulerBossHistory.FirstDate && snapshot.Date <= today)
            .GroupBy(snapshot => snapshot.Date).Select(group => group.MaxBy(snapshot => snapshot.FetchedAt)!)
            .OrderBy(snapshot => snapshot.Date).ToArray();
        var clears = days.SelectMany(day => (day.State?.Bosses ?? []).Where(boss => SchedulerEntries.Flag(boss.Complete)
            && !string.IsNullOrWhiteSpace(boss.Name) && SchedulerBossHistory.Cycle(boss.Cycle) is BossCycle.Weekly or BossCycle.Monthly)
            .Select(boss => (Day: day.Date, Boss: boss, Cycle: SchedulerBossHistory.Cycle(boss.Cycle)!.Value)))
            .GroupBy(item => (item.Cycle, Start: SchedulerBossHistory.Start(item.Cycle, item.Day), Key: SchedulerBossHistory.BossKey(item.Boss.Name!)));
        var records = new List<BossIncomeRecord>();
        foreach (var group in clears)
        {
            // Repeated flags and alternative difficulties represent one reward.
            var chosen = group.OrderByDescending(item => Rank(CrystalPrices.DifficultyKey(item.Boss.Difficulty)))
                .ThenBy(item => item.Day).First();
            var name = chosen.Boss.Name!;
            var difficulty = CrystalPrices.DifficultyKey(chosen.Boss.Difficulty);
            var date = group.Where(item => CrystalPrices.DifficultyKey(item.Boss.Difficulty) == difficulty).Min(item => item.Day);
            var lastIncomplete = days.Where(day => day.Date >= group.Key.Start && day.Date < date
                && day.State?.Bosses?.Any(boss => SchedulerBossHistory.Cycle(boss.Cycle) == group.Key.Cycle
                    && SchedulerBossHistory.BossKey(boss.Name ?? "") == group.Key.Key && !SchedulerEntries.Flag(boss.Complete)) == true)
                .Select(day => (DateOnly?)day.Date).LastOrDefault();
            var earliest = lastIncomplete?.AddDays(1) ?? group.Key.Start;
            if (earliest < SchedulerBossHistory.FirstDate) earliest = SchedulerBossHistory.FirstDate;
            // A first observed clear after a missing interval cannot be assigned
            // to that observation date. Keep the snapshots, but omit this income.
            if (earliest != date) continue;
            var price = CrystalPrices.Find(name, difficulty, date);
            var id = BossParty.RecordId(name, group.Key.Cycle, group.Key.Start);
            var party = BossParty.Clamp(name, partySize?.Invoke(id) ?? 1);
            records.Add(new(id, name, difficulty, group.Key.Cycle, group.Key.Start, date, earliest,
                false, price, party, price?.Meso / party, true));
        }
        var existing = records.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var clear in (manual ?? []).Where(clear => clear.Date >= SchedulerBossHistory.FirstDate && clear.Date <= today)
            .OrderBy(clear => clear.Date))
        {
            if (!ManualWeeklyHistory.Valid(clear)) continue;
            var id = ManualWeeklyHistory.Id(clear);
            if (!existing.Add(id)) continue;
            var difficulty = CrystalPrices.DifficultyKey(clear.Difficulty);
            var price = CrystalPrices.Find(clear.Name, difficulty, clear.Date);
            var party = BossParty.Clamp(clear.Name, partySize?.Invoke(id) ?? 1);
            records.Add(new(id, clear.Name, difficulty, clear.Cycle, SchedulerBossHistory.Start(clear.Cycle, clear.Date),
                clear.Date, clear.Date, false, price, party, price?.Meso / party, true) { Manual = true });
        }
        var corrections = (changes ?? []).GroupBy(change => change.OriginalId).Select(group => group.Last())
            .Where(change => !string.IsNullOrEmpty(change.OriginalId) && (change.Replacement is null
                || ManualWeeklyHistory.Valid(change.Replacement) && change.Replacement.Ocid == change.Ocid
                    && change.Replacement.Date >= SchedulerBossHistory.FirstDate && change.Replacement.Date <= today)).ToArray();
        var hidden = corrections.Select(change => change.OriginalId).Concat(corrections.Where(change => change.Replacement is not null)
            .Select(change => ManualWeeklyHistory.Id(change.Replacement!))).ToHashSet(StringComparer.Ordinal);
        records.RemoveAll(record => hidden.Contains(record.Id));
        foreach (var clear in corrections.Select(change => change.Replacement).OfType<ManualWeeklyClear>().DistinctBy(ManualWeeklyHistory.Id))
        {
            var id = ManualWeeklyHistory.Id(clear);
            var difficulty = CrystalPrices.DifficultyKey(clear.Difficulty);
            var price = CrystalPrices.Find(clear.Name, difficulty, clear.Date);
            var party = BossParty.Clamp(clear.Name, partySize?.Invoke(id) ?? 1);
            records.Add(new(id, clear.Name, difficulty, clear.Cycle, SchedulerBossHistory.Start(clear.Cycle, clear.Date),
                clear.Date, clear.Date, false, price, party, price?.Meso / party, true) { Manual = true });
        }
        foreach (var week in records.Where(record => record.Cycle == BossCycle.Weekly).GroupBy(record => record.PeriodStart))
        {
            var excluded = week.OrderByDescending(record => record.Meso ?? -1).ThenBy(record => record.Id, StringComparer.Ordinal)
                .Skip(WeeklyCap).Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < records.Count; index++)
                if (excluded.Contains(records[index].Id)) records[index] = records[index] with { Included = false };
        }
        return new(records.OrderByDescending(record => record.Date).ThenByDescending(record => record.Meso).ToArray(), today,
            days.Select(day => day.Date).Concat(records.Select(record => record.Date)).Select(date => (DateOnly?)date).Min(),
            days.Where(day => day.Final || day.Date == today).Select(day => day.Date).ToHashSet());
    }
    private static int Rank(string difficulty) => difficulty switch { "extreme" => 5, "chaos" => 4, "hard" => 3, "normal" => 2, "easy" => 1, _ => 0 };
}
