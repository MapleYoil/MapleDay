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

public sealed class IncomeCalendarDay(CalendarIncomeDay day, DateOnly selectedDate, DateOnly today, int expectedCharacters)
{
    public DateOnly Date => day.Date;
    public string Day => day.Date.Day.ToString();
    public bool Enabled => day.Date >= SchedulerBossHistory.FirstDate && day.Date <= today;
    public double Opacity => day.InMonth ? 1 : 0.4;
    public string Amount => day.Bosses.Count > 0 || day.KnownCharacters > 0 ? IncomeCalendar.CompactMoney(day.Meso) : "";
    public string Note => day.Bosses.Count > 0 ? $"{day.Bosses.Count}마리" : Enabled && day.KnownCharacters == 0 ? "기록 없음" : "";
    public Brush Background { get; } = AppTheme.Brush(day.Date == selectedDate ? "AccentSoftBrush" : "CardBrush");
    public Brush BorderBrush { get; } = AppTheme.Brush(day.Date == selectedDate ? "AccentBrush" : "LineBrush");
    public Thickness BorderThickness => new(day.Date == selectedDate ? 2 : 1);
    public string AccessibleName => $"{day.Date:yyyy년 M월 d일}, {BossIncome.Money(day.Meso)}, 보스 {day.Bosses.Count}마리";
    public string Tooltip => AccessibleName + $"\n조회 기록 {day.KnownCharacters}/{expectedCharacters}캐릭터";
}

public sealed class IncomeCalendarBoss(CalendarBossRecord record)
{
    public string CharacterName => record.CharacterName;
    public string Name => $"{record.Boss.Name} · {record.Boss.DifficultyLabel}";
    public string? IconFile => SchedulerIconAssets.BossFile(record.Boss.Name) is { } file ? "Scheduler/" + file : null;
    public string Amount => record.Boss.Meso is { } meso ? IncomeCalendar.CompactMoney(meso) : "가격 확인 필요";
    public string Details => $"{(record.Boss.Cycle == BossCycle.Weekly ? "주간" : "월간")} · {record.Boss.PartySize}인 · "
        + (!record.Boss.Included ? "주간 12개 상한에서 제외" : record.Boss.Meso is null ? "합산 제외" : BossIncome.Money(record.Boss.Meso.Value));
}
