using System.Globalization;
using System.Text.Json;

namespace MapleDay.Core;

public sealed record BossLootItem(string Id, string Name, string[] Bosses, string Icon);
public static class BossLootCatalog
{
    public const string Guide = "https://maplestory.nexon.com/guide/n23gameinformation/articles/459";
    public const string SoulGuide = "https://maplestory.nexon.com/guide/n23gameinformation/articles/416";
    public static IReadOnlyList<BossLootItem> Items { get; } = Load();
    private static IReadOnlyList<BossLootItem> Load()
    {
        using var stream = typeof(BossLootCatalog).Assembly.GetManifestResourceStream("MapleDay.Core.BossLootItems.json")!;
        return JsonSerializer.Deserialize<BossLootItem[]>(stream)!;
    }
    public static IReadOnlyList<BossLootItem> ForBoss(string name) => Items.Where(item => item.Bosses
        .Any(boss => SchedulerBossHistory.BossKey(boss) == SchedulerBossHistory.BossKey(name))).ToArray();
    private static readonly Dictionary<string, Dictionary<string, string[]>> Rules = LoadRules();
    private static Dictionary<string, Dictionary<string, string[]>> LoadRules()
    {
        using var stream = typeof(BossLootCatalog).Assembly.GetManifestResourceStream("MapleDay.Core.BossLootDifficulty.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(stream)!;
    }
    private static Dictionary<string, string[]>? RulesFor(string boss) => Rules.FirstOrDefault(entry => SchedulerBossHistory.BossKey(entry.Key) == SchedulerBossHistory.BossKey(boss)).Value;
    public static IReadOnlyList<string> Difficulties(string boss) => RulesFor(boss)?.Keys.ToArray() ?? [];
    public static IReadOnlyList<BossLootItem> ForBoss(string boss, string difficulty)
    {
        var names = RulesFor(boss)?.GetValueOrDefault(CrystalPrices.DifficultyKey(difficulty)) ?? [];
        return ForBoss(boss).Where(item => names.Contains(item.Name)).ToArray();
    }
    public static string DifficultyLabel(string difficulty) => CrystalPrices.DifficultyKey(difficulty) switch
        { "easy" => "이지", "normal" => "노멀", "hard" => "하드", "chaos" => "카오스", "extreme" => "익스트림", _ => difficulty };
}
public sealed record BossLootRecord(string Id, string Ocid, DateOnly Date, string Boss, string ItemId, string ItemName,
    string Mode, long Amount, int PartySize, string Ratios = "", int OwnMember = 1, string Difficulty = "", string ClearId = "")
{
    public long Received => BossLoot.Calculate(Mode, Amount, PartySize, Ratios, OwnMember);
}
public static class BossLoot
{
    public static bool ForClear(BossLootRecord loot, string ocid, BossIncomeRecord clear) => Valid(loot)
        && loot.Ocid == ocid && SchedulerBossHistory.BossKey(loot.Boss) == SchedulerBossHistory.BossKey(clear.Name)
        && (string.IsNullOrEmpty(loot.ClearId) ? SchedulerBossHistory.Start(clear.Cycle, loot.Date) == clear.PeriodStart
            : loot.ClearId == clear.Id)
        && (string.IsNullOrEmpty(loot.Difficulty) || CrystalPrices.DifficultyKey(loot.Difficulty) == clear.Difficulty);
    public const long MaximumAmount = 1_000_000_000_000_000;
    public static long Calculate(string mode, long amount, int partySize, string ratios = "", int ownMember = 1)
    {
        if (amount < 0 || amount > MaximumAmount) throw new ArgumentException("금액은 0~1000조 메소의 정수로 입력하세요.");
        if (partySize is < 1 or > 6) throw new ArgumentException("파티 인원은 1~6인으로 입력하세요.");
        if (mode == "received") return amount;
        if (mode == "equal") return amount / partySize;
        if (mode != "ratio") throw new ArgumentException("분배 방법을 선택하세요.");
        var parts = (ratios ?? "").Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != partySize || ownMember < 1 || ownMember > partySize)
            throw new ArgumentException("인원 수와 비율 항목 수가 같아야 하며, 내 순번을 선택해야 합니다.");
        var weights = parts.Select(part => decimal.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            && value is >= 0 and <= 1_000_000 ? value : throw new ArgumentException("비율은 0 이상의 숫자를 콜론(:)으로 구분하세요.")).ToArray();
        if (weights.Sum() <= 0) throw new ArgumentException("비율 합계는 0보다 커야 합니다.");
        return decimal.ToInt64(decimal.Floor(amount * weights[ownMember - 1] / weights.Sum()));
    }
    public static bool Valid(BossLootRecord? record) { if (record is null) return false; try { _ = record.Received; return !string.IsNullOrWhiteSpace(record.Ocid)
        && !string.IsNullOrWhiteSpace(record.Id) && !string.IsNullOrWhiteSpace(record.Boss) && !string.IsNullOrWhiteSpace(record.ItemName)
        && record.Date >= new DateOnly(2003, 4, 29) && record.PartySize <= BossParty.Maximum(record.Boss)
        && (string.IsNullOrEmpty(record.Difficulty) || string.IsNullOrEmpty(record.ItemId)
            || ForRecordedDifficulty(record)); } catch (ArgumentException) { return false; } }
    private static bool ForRecordedDifficulty(BossLootRecord record) => BossLootCatalog.ForBoss(record.Boss, record.Difficulty)
        .Any(item => item.Id == record.ItemId);
    public static long Sum(IEnumerable<BossLootRecord> records, DateOnly start, DateOnly end) => records
        .Where(record => record.Date >= start && record.Date <= end && Valid(record)).Sum(record => record.Received);
    public static string Distribution(BossLootRecord record) => record.Mode == "received" ? "수령액 직접 입력"
        : record.Mode == "equal" ? $"{record.PartySize}인 균등 분배 · 총액 {BossIncome.Money(record.Amount)}"
        : $"{record.PartySize}인 · {record.Ratios} · 내 순번 {record.OwnMember} · 총액 {BossIncome.Money(record.Amount)}";
}
