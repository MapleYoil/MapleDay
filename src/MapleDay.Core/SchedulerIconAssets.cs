namespace MapleDay.Core;

public static class SchedulerIconAssets
{
    // IDs/names come from scheduler bossIcon/info and UIBoss.img/BossList/info.
    private static readonly Dictionary<string, int> BossIds = new(StringComparer.Ordinal)
    {
        ["발록"] = 0, ["자쿰"] = 1, ["혼테일"] = 2, ["힐라"] = 3,
        ["피에르"] = 4, ["반반"] = 5, ["블러디퀸"] = 6, ["벨룸"] = 7,
        ["반레온"] = 8, ["아카이럼"] = 9, ["매그너스"] = 10, ["핑크빈"] = 11,
        ["시그너스"] = 12, ["스우"] = 13, ["데미안"] = 15, ["루시드"] = 19,
        ["카웅"] = 21, ["파풀라투스"] = 22, ["윌"] = 23, ["진힐라"] = 24,
        ["검은마법사"] = 25, ["더스크"] = 26, ["듄켈"] = 27, ["선택받은세렌"] = 28,
        ["가디언엔젤슬라임"] = 29, ["감시자칼로스"] = 30, ["카링"] = 31,
        ["몬스터파크익스트림"] = 32,
        ["림보"] = 33, ["발드릭스"] = 34, ["최초의대적자"] = 35,
        ["카이"] = 36, ["찬란한흉성"] = 37, ["유피테르"] = 38,
        ["메이린"] = 40, ["벨로나"] = 41
    };

    public static string? BossFile(string name)
    {
        var normalized = string.Concat(name.Where(character => !char.IsWhiteSpace(character)));
        if (normalized.StartsWith("시즌보스", StringComparison.Ordinal)) normalized = normalized[4..];
        // The weekly quest uses a different name for this same in-game content.
        if (normalized.Contains("익스트림몬스터파커", StringComparison.Ordinal)) normalized = "몬스터파크익스트림";
        return BossIds.TryGetValue(normalized, out var id) ? $"Bosses/icon_{id}.png" : null;
    }

    public static string? DifficultyFile(string? difficulty) => difficulty?.Trim().ToLowerInvariant() switch
    {
        "easy" => "Difficulty/easy.png", "normal" => "Difficulty/normal.png",
        "hard" => "Difficulty/hard.png", "chaos" => "Difficulty/chaos.png",
        "extreme" => "Difficulty/extreme.png", _ => null
    };
}
