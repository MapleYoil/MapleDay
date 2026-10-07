namespace MapleDay.Core;

public static class SchedulerRequirements
{
    // Required levels from the user's in-game scheduler settings screenshots.
    private static readonly (string Region, int Level)[] DailyQuestLevels =
    [
        ("소멸의여로", 200), ("츄츄", 210), ("레헬른", 220), ("아르카나", 225),
        ("모라스", 230), ("에스페라", 235), ("문브릿지", 245), ("고통의미궁", 250),
        ("리멘", 255), ("세르니움", 260), ("호텔아르크스", 265), ("오디움", 270),
        ("도원경", 275), ("아르테리아", 280), ("카르시온", 285), ("탈라하트", 290), ("기어드락", 295)
    ];
    private static readonly Dictionary<string, int> BossLevels = new(StringComparer.Ordinal)
    {
        ["Bosses/icon_1.png"] = 90, ["Bosses/icon_10.png"] = 175, ["Bosses/icon_22.png"] = 190,
        ["Bosses/icon_4.png"] = 180, ["Bosses/icon_5.png"] = 180, ["Bosses/icon_6.png"] = 180, ["Bosses/icon_7.png"] = 180,
        ["Bosses/icon_13.png"] = 190, ["Bosses/icon_15.png"] = 190,
        ["Bosses/icon_29.png"] = 210, ["Bosses/icon_19.png"] = 220, ["Bosses/icon_23.png"] = 235,
        ["Bosses/icon_26.png"] = 245, ["Bosses/icon_24.png"] = 250, ["Bosses/icon_27.png"] = 255,
        ["Bosses/icon_28.png"] = 260, ["Bosses/icon_30.png"] = 265, ["Bosses/icon_35.png"] = 270,
        ["Bosses/icon_31.png"] = 275, ["Bosses/icon_37.png"] = 280, ["Bosses/icon_41.png"] = 280,
        ["Bosses/icon_33.png"] = 285, ["Bosses/icon_34.png"] = 290, ["Bosses/icon_38.png"] = 295,
        ["Bosses/icon_25.png"] = 255
    };

    public static int RequiredLevel(SchedulerEntry entry)
    {
        if (entry.Section is SchedulerSection.Boss or SchedulerSection.UnregisteredBoss)
        {
            if (entry.Cycle is "bossDaily" or "daily") return 0;
            return BossRequiredLevel(entry.Name);
        }
        if (entry.Section != SchedulerSection.Daily || entry.Type != "quest") return 0;
        var name = string.Concat(entry.Name.Where(character => !char.IsWhiteSpace(character)));
        return DailyQuestLevels.FirstOrDefault(region => name.Contains(region.Region, StringComparison.Ordinal)).Level;
    }
    public static int BossRequiredLevel(string name)
    {
        var icon = SchedulerIconAssets.BossFile(name);
        return icon is not null && BossLevels.TryGetValue(icon, out var level) ? level : 0;
    }
    public static bool IsLevelBlocked(SchedulerEntry entry, int characterLevel) => !entry.Complete && characterLevel < RequiredLevel(entry);
    public static bool IsQuestReady(SchedulerEntry entry) => entry.Type == "quest" && entry.QuestState == "1"
        && entry.Maximum > 0 && entry.Now >= entry.Maximum;
}
