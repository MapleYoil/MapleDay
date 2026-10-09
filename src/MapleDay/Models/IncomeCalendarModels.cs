using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MapleDay.Models;

public sealed class IncomeCharacterChoice(string? ocid, string label)
{
    public string? Ocid { get; } = ocid;
    public string Label { get; } = label;
}

public sealed class IncomeCalendarDay(CalendarIncomeDay day, DateOnly selectedDate, DateOnly today, int expectedCharacters, IncomeDisplay? display = null)
{
    public DateOnly Date => day.Date;
    public string Day => day.Date.Day.ToString();
    public bool Enabled => (day.Date >= SchedulerBossHistory.FirstDate || day.Loot?.Count > 0) && day.Date <= today;
    public double Opacity => day.InMonth ? 1 : 0.4;
    public string Amount => day.Bosses.Count > 0 || day.KnownCharacters > 0 || day.Loot?.Count > 0 ? (display ?? new()).Format(day.Meso, true) : "";
    public double AmountFontSize => display?.ValidMode == "both" ? 11 : 15;
    public string Note => day.Loot?.Count > 0 ? $"보스 {day.Bosses.Count} · 물욕템 {day.Loot.Count}" : day.Bosses.Count > 0 ? $"{day.Bosses.Count}마리" : Enabled && day.KnownCharacters == 0 ? "기록 없음" : "";
    public Brush Background { get; } = AppTheme.Brush(day.Date == selectedDate ? "AccentSoftBrush" : "CardBrush");
    public Brush BorderBrush { get; } = AppTheme.Brush(day.Date == selectedDate ? "AccentBrush" : "LineBrush");
    public Thickness BorderThickness => new(day.Date == selectedDate ? 2 : 1);
    public string AccessibleName => $"{day.Date:yyyy년 M월 d일}, {(display ?? new()).Format(day.Meso)}, 보스 {day.Bosses.Count}마리, 물욕템 {day.Loot?.Count ?? 0}건";
    public string Tooltip => AccessibleName + $"\n조회 기록 {day.KnownCharacters}/{expectedCharacters}캐릭터";
}

public sealed class IncomeCalendarBoss(CalendarBossRecord record, IncomeDisplay? display = null, IEnumerable<BossLootRecord>? loot = null)
{
    public IReadOnlyList<BossLootRow> Loot { get; } = (loot ?? []).Where(item => BossLoot.ForClear(item, record.Ocid, record.Boss))
        .OrderBy(item => item.Date).Select(item => new BossLootRow(item, record.CharacterName, display ?? new())).ToArray();
    public string LootLabel => Loot.Count == 0 ? "물욕템 기록" : $"물욕템 수정 · {Loot.Count}건";
    public CalendarBossRecord Record { get; } = record;
    public string CharacterName => Record.CharacterName;
    public string EditLabel => $"{Record.CharacterName} · {Record.Boss.Name} · {Record.Boss.Date:yyyy.MM.dd} 인원 수정";
    public string Name => $"{Record.Boss.Name} · {Record.Boss.DifficultyLabel}";
    public string? IconFile => SchedulerIconAssets.BossFile(Record.Boss.Name) is { } file ? "Scheduler/" + file : null;
    public string Amount => Record.Boss.Meso is { } meso ? (display ?? new()).Format(meso) : "가격 확인 필요";
    public string Details => (Record.Boss.Manual ? "수동 추가 · " : "") + $"{(Record.Boss.Cycle == BossCycle.Weekly ? "주간" : "월간")} · {Record.Boss.PartySize}인 · "
        + (!Record.Boss.Included ? "주간 12개 상한에서 제외" : Record.Boss.Meso is null ? "합산 제외" : (display ?? new()).Format(Record.Boss.Meso.Value));
}
