namespace MapleDay.Web;

// Deliberately exclude API keys, OCIDs, local paths, support tickets and telemetry identifiers.
public sealed record WebSnapshot(string Version, DateTimeOffset UpdatedAt, IReadOnlyList<WebCharacter> Characters,
    string IncomeMode = "meso", decimal CashRate = 1500)
{
    public static WebSnapshot Empty { get; } = new("", DateTimeOffset.UtcNow, []);
}
public sealed record WebCharacter(string Id, string Name, string World, string Class, int Level, string LevelText,
    string Status, DateTimeOffset? FetchedAt, int? WeeklyCleared, IReadOnlyList<WebEntry> Daily,
    IReadOnlyList<WebEntry> Weekly, IReadOnlyList<WebEntry> Bosses, WebIncome Income, WebLevel Experience, string? Image);
public sealed record WebEntry(string Name, string Difficulty, string Cycle, string Progress, string Unit, bool Complete,
    bool Blocked, string Status, int? PartySize, string? Icon, string? DifficultyIcon);
public sealed record WebIncome(long Weekly, long Monthly, long Total, IReadOnlyList<WebClear> Records,
    long RemainingWeekly = 0, int Unpriced = 0, IReadOnlyList<WebLoot>? Loot = null);
public sealed record WebLoot(string Date, string Boss, string ItemName, string? Icon, int PartySize, string Distribution, long Received);
public sealed record WebClear(string Date, string Name, string Difficulty, int PartySize, long? Meso, bool Included);
public sealed record WebLevel(string Today, string Average, string LevelUp, double Percent, IReadOnlyList<WebExpPoint> Points);
public sealed record WebExpPoint(string Date, int Level, string Exp, double Percent);
