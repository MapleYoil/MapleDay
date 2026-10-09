namespace MapleDay.Core;

// Corrections stay separate from API snapshots so refreshes cannot restore deleted clears.
public sealed record BossClearChange(string Ocid, string OriginalId, ManualWeeklyClear? Replacement);
public sealed record BossClearEditPlan(IReadOnlyList<BossClearChange> Changes, IReadOnlyList<BossLootRecord> Loot);

public static class BossClearHistory
{
    public static BossClearEditPlan Edit(string ocid, BossIncomeRecord source, ManualWeeklyClear? replacement,
        IEnumerable<BossClearChange> changes, IEnumerable<BossLootRecord> loot)
    {
        if (replacement is not null && (replacement.Ocid != ocid || !ManualWeeklyHistory.Valid(replacement)))
            throw new ArgumentException("보스와 난이도를 확인하세요.");
        var saved = changes.ToList();
        var previous = saved.LastOrDefault(change => change.Ocid == ocid && change.Replacement is { } clear
            && ManualWeeklyHistory.Id(clear) == source.Id);
        var originalId = previous?.OriginalId ?? source.Id;
        saved.RemoveAll(change => change.Ocid == ocid && (change.OriginalId == originalId || change.OriginalId == source.Id));
        saved.Add(new(ocid, originalId, replacement));
        // A later API response can report the former destination too.
        if (source.Id != originalId) saved.Add(new(ocid, source.Id, null));
        var updatedLoot = new List<BossLootRecord>();
        foreach (var item in loot)
        {
            if (!BossLoot.ForClear(item, ocid, source)) { updatedLoot.Add(item); continue; }
            if (replacement is null) continue;
            var updated = item with { Boss = replacement.Name, Difficulty = CrystalPrices.DifficultyKey(replacement.Difficulty),
                ClearId = ManualWeeklyHistory.Id(replacement) };
            if (!BossLoot.Valid(updated))
                throw new ArgumentException("연결된 물욕템의 보상 난이도 또는 분배 인원이 맞지 않아요. 물욕템 기록을 먼저 수정·삭제하세요.");
            updatedLoot.Add(updated);
        }
        return new(saved, updatedLoot);
    }

    public static List<BossClearChange> Restore(IEnumerable<BossClearChange> changes, IEnumerable<ManualWeeklyClear> clears)
    {
        var replacements = clears.ToDictionary(ManualWeeklyHistory.Key);
        return changes.Select(change => change.Replacement is null && replacements.TryGetValue(change.Ocid + "|" + change.OriginalId, out var clear)
            ? change with { Replacement = clear } : change).ToList();
    }
}
