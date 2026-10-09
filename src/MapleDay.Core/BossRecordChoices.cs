using System.Text.Json;

namespace MapleDay.Core;

public sealed record BossHealthEntry(string Name, string Difficulty, long Health);
public sealed record BossRecordChoice(CrystalPrice Price, long Health)
{
    public string Name => Price.Name;
    public string Difficulty => Price.Difficulty;
    public string DifficultyLabel => BossLootCatalog.DifficultyLabel(Difficulty);
    public string Label => $"{BossLootCatalog.DifficultyLabel(Difficulty)} {Name}";
}

public static class BossRecordChoices
{
    // Use the referenced table's displayed health consistently, without mixing in raw phase HP.
    public const string Source = "https://namu.wiki/w/메이플스토리/보스 몬스터/보스 티어#s-7.1";
    public static readonly DateOnly CheckedOn = new(2026, 10, 9);
    public static IReadOnlyList<BossHealthEntry> Health { get; } = Load();
    public static long MinimumHealth { get; } = Health.Single(entry => entry.Name == "스우" && entry.Difficulty == "hard").Health;
    public static IReadOnlyList<BossRecordChoice> Single { get; } = ManualWeeklyHistory.SingleChoices
        .Select(price => new BossRecordChoice(price, FindHealth(price.Name, price.Difficulty) ?? 0))
        .Where(choice => choice.Health >= MinimumHealth)
        .OrderBy(choice => choice.Health).ThenBy(choice => choice.Name, StringComparer.Ordinal).ThenBy(choice => choice.Difficulty, StringComparer.Ordinal).ToArray();
    public static IReadOnlyList<BossRecordChoice> Weekly { get; } = Single
        .Where(choice => ManualWeeklyHistory.CycleFor(choice.Name) == BossCycle.Weekly).ToArray();
    public static long? FindHealth(string name, string difficulty) => Health.FirstOrDefault(entry =>
        SchedulerBossHistory.BossKey(entry.Name) == SchedulerBossHistory.BossKey(name)
        && entry.Difficulty == CrystalPrices.DifficultyKey(difficulty))?.Health;
    public static bool Allows(string name, string difficulty) => FindHealth(name, difficulty) >= MinimumHealth;
    public static bool MatchesSearch(string name, string? query)
    {
        var normalized = string.Concat((query ?? "").Where(character => !char.IsWhiteSpace(character)));
        return string.Concat(name.Where(character => !char.IsWhiteSpace(character))).Contains(normalized,StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<BossHealthEntry> Load()
    {
        using var stream = typeof(BossRecordChoices).Assembly.GetManifestResourceStream("MapleDay.Core.BossHealth.json")!;
        var entries = JsonSerializer.Deserialize<BossHealthEntry[]>(stream)!;
        if (entries.Length == 0 || entries.Any(entry => entry.Health <= 0)
            || entries.GroupBy(entry => (SchedulerBossHistory.BossKey(entry.Name), entry.Difficulty)).Any(group => group.Count() > 1))
            throw new InvalidDataException("Invalid boss health catalog.");
        return Array.AsReadOnly(entries);
    }
}
