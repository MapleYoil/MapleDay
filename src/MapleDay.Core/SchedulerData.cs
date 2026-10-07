using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapleDay.Core;

public sealed class SchedulerState
{
    [JsonPropertyName("date")] public string? Date { get; init; }
    [JsonPropertyName("character_name")] public string? Name { get; init; }
    [JsonPropertyName("world_name")] public string? World { get; init; }
    [JsonPropertyName("daily_contents")] public List<SchedulerContent>? Daily { get; init; }
    [JsonPropertyName("weekly_contents")] public List<SchedulerContent>? Weekly { get; init; }
    [JsonPropertyName("boss_contents")] public List<SchedulerBoss>? Bosses { get; init; }
    [JsonPropertyName("weekly_boss_clear_count")] public long? WeeklyBossClearCount { get; init; }
    [JsonPropertyName("weekly_boss_clear_limit_count")] public long? WeeklyBossClearLimit { get; init; }
}

public sealed class SchedulerContent
{
    [JsonPropertyName("content_name")] public string? Name { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("registration_flag")] public JsonElement Registration { get; init; }
    [JsonPropertyName("now_count")] public long? Now { get; init; }
    [JsonPropertyName("max_count")] public long? Maximum { get; init; }
    [JsonPropertyName("quest_state")] public string? QuestState { get; init; }
}

public sealed class SchedulerBoss
{
    [JsonPropertyName("content_name")] public string? Name { get; init; }
    [JsonPropertyName("difficulty")] public string? Difficulty { get; init; }
    [JsonPropertyName("cycle")] public string? Cycle { get; init; }
    [JsonPropertyName("list_order_no")] public long Order { get; init; }
    [JsonPropertyName("registration_flag")] public JsonElement Registration { get; init; }
    [JsonPropertyName("complete_flag")] public JsonElement Complete { get; init; }
}

public enum SchedulerSection { Daily, Weekly, Boss, UnregisteredBoss }
public sealed record SchedulerEntry(string Name, string Detail, bool Complete, SchedulerSection Section, long? Now = null, long? Maximum = null, string? Difficulty = null, string? Type = null, string? Cycle = null, string? QuestState = null);

public static class SchedulerEntries
{
    public static bool Flag(JsonElement value) => value.ValueKind == JsonValueKind.True
        || value.ValueKind == JsonValueKind.String && string.Equals(value.GetString()?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    public static List<SchedulerEntry> Create(SchedulerState state)
    {
        var result = new List<SchedulerEntry>();
        AddContents(result, state.Daily, SchedulerSection.Daily);
        AddContents(result, state.Weekly, SchedulerSection.Weekly);
        var bosses = state.Bosses ?? [];
        foreach (var boss in bosses.OrderBy(BossOrderKey).ThenBy(boss => boss.Order))
        {
            var registered = Flag(boss.Registration);
            var complete = Flag(boss.Complete);
            if ((!registered && !complete) || string.IsNullOrWhiteSpace(boss.Name)) continue;
            var difficulty = boss.Difficulty switch { "easy" => "이지", "normal" => "노멀", "hard" => "하드", "chaos" => "카오스", "extreme" => "익스트림", _ => boss.Difficulty };
            var cycle = boss.Cycle switch { "bossDaily" or "daily" => "일일", "bossWeekly" or "weekly" => "주간", "bossMonthly" or "monthly" => "월간", _ => boss.Cycle };
            result.Add(new(boss.Name!, string.Join(" · ", new[] { difficulty, cycle }.Where(value => !string.IsNullOrWhiteSpace(value))), complete,
                registered ? SchedulerSection.Boss : SchedulerSection.UnregisteredBoss, Difficulty: boss.Difficulty, Cycle: boss.Cycle));
        }
        return result;
    }

    private static (int Level, int Position) BossOrderKey(SchedulerBoss boss)
    {
        var name = boss.Name ?? "";
        var icon = SchedulerIconAssets.BossFile(name);
        // Below Lv. 200, use the order in the game's registration screen,
        // including Papulatus before Pierre and Damien directly after Lotus.
        var position = icon switch
        {
            "Bosses/icon_1.png" => 0, "Bosses/icon_10.png" => 1,
            "Bosses/icon_22.png" => 2, "Bosses/icon_4.png" => 3,
            "Bosses/icon_5.png" => 4, "Bosses/icon_6.png" => 5,
            "Bosses/icon_7.png" => 6, "Bosses/icon_13.png" => 7,
            "Bosses/icon_15.png" => 8, _ => -1
        };
        if (position >= 0) return (0, position);
        var level = SchedulerRequirements.BossRequiredLevel(name);
        // Bellona shares Lv. 280 with Radiant Omen, but follows it.
        return (level > 0 ? level : int.MaxValue, icon == "Bosses/icon_41.png" ? 1 : 0);
    }

    private static void AddContents(List<SchedulerEntry> entries, List<SchedulerContent>? contents, SchedulerSection section)
    {
        foreach (var content in contents ?? [])
        {
            if (!Flag(content.Registration) || string.IsNullOrWhiteSpace(content.Name)) continue;
            var quest = content.Type == "quest" || content.Name.Contains("퀘스트");
            var dailyQuest = section == SchedulerSection.Daily && quest;
            var epicDungeon = content.Name.Contains("에픽 던전", StringComparison.Ordinal);
            var maximum = dailyQuest ? 100 : epicDungeon ? 5 : Math.Max(content.Maximum ?? 0, 0);
            var now = Math.Max(content.Now ?? 0, 0);
            // Floors, guild points and scores are records, rather than clear flags.
            var record = content.Name.Contains("무릉도장") || content.Name.StartsWith("[길드]", StringComparison.Ordinal)
                || content.Name.Contains("지하 수로") || content.Name.Contains("플래그 레이스");
            var complete = quest ? content.QuestState == "2" : !record && maximum > 0 && now >= maximum;
            if (dailyQuest)
            {
                now = complete ? 100 : Math.Min(now, 100);
            }
            if (epicDungeon) now = Math.Min(now, 5);
            var detail = epicDungeon ? $"STAGE {now}" : maximum > 0 ? $"{SchedulerNumberFormat.Count(now)} / {SchedulerNumberFormat.Count(maximum)}"
                : content.QuestState is not null ? content.QuestState switch { "2" => "완료", "1" => "진행 중", _ => "미완료" }
                : now > 0 ? $"기록 {SchedulerNumberFormat.Count(now)}" : "미완료";
            entries.Add(new(content.Name!, detail, complete, section, now, maximum, Type: quest ? "quest" : "contents", QuestState: content.QuestState));
        }
    }
}

public static class SchedulerSelection
{
    public static IReadOnlyList<string> Reorder(IEnumerable<string> added, IEnumerable<string> displayedOrder)
    {
        var saved = added.Distinct(StringComparer.Ordinal).ToArray();
        var registered = saved.ToHashSet(StringComparer.Ordinal);
        var ordered = displayedOrder.Distinct(StringComparer.Ordinal).Where(registered.Contains).ToArray();
        var visible = ordered.ToHashSet(StringComparer.Ordinal);
        var queue = new Queue<string>(ordered);
        // Keep registrations unavailable under the current API key in their
        // original slots; sorting visible characters must never remove them.
        return saved.Select(ocid => visible.Contains(ocid) ? queue.Dequeue() : ocid).ToArray();
    }

    public static IReadOnlyList<string> Visible(IEnumerable<string> added, IEnumerable<string> available, string? selected)
    {
        var accessible = available.ToHashSet(StringComparer.Ordinal);
        return added.Distinct(StringComparer.Ordinal).Where(ocid => accessible.Contains(ocid) && (selected is null || selected == ocid)).ToArray();
    }
}
