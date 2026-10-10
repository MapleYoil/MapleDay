using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MapleDay.Models;

public sealed class ScheduleLabel(ScheduleOccurrence occurrence)
{
    public string Name => (occurrence.Completed ? "✓ " : "") + occurrence.DisplayName;
    public string? IconFile => occurrence.Schedule is { Kind: ScheduleKind.Boss } item && SchedulerIconAssets.BossFile(item.Boss) is { } file ? "Scheduler/" + file : null;
    public Visibility IconVisibility => IconFile is null ? Visibility.Collapsed : Visibility.Visible;
    public string Party => occurrence.Schedule is { Kind: ScheduleKind.Boss } item ? $"{item.PartySize}인" : "";
    public string AccessibleName => occurrence.AccessibleName;
}

public sealed class ScheduleDay(ScheduleCalendarDay day, DateOnly selected, DateOnly today)
{
    public DateOnly Date => day.Date;
    public string Number => day.Date.Day.ToString();
    public double Opacity => day.InMonth ? 1 : .4;
    public IReadOnlyList<ScheduleLabel> Preview { get; } = day.Items.Take(3).Select(item => new ScheduleLabel(item)).ToArray();
    public string More => day.Items.Count > 3 ? $"+{day.Items.Count - 3}개" : "";
    public Visibility MoreVisibility => day.Items.Count > 3 ? Visibility.Visible : Visibility.Collapsed;
    public string Today => day.Date == today ? "오늘" : "";
    public Brush Background => AppTheme.Brush(day.Date == selected ? "AccentSoftBrush" : "CardBrush");
    public Brush BorderBrush => AppTheme.Brush(day.Date == selected ? "AccentBrush" : "LineBrush");
    public Thickness BorderThickness => new(day.Date == selected ? 2 : 1);
    public string AccessibleName => $"{day.Date:yyyy년 M월 d일}, {(day.Date == today ? "오늘, " : "")}{day.Items.Count}개 일정, "
        + string.Join(", ", day.Items.Select(item => item.AccessibleName));
}

public sealed class ScheduleRow(ScheduleOccurrence occurrence, DateOnly today, IReadOnlyList<ImageSource>? images = null)
{
    public ScheduleOccurrence Occurrence { get; } = occurrence;
    public ScheduleLabel Label { get; } = new(occurrence);
    public string Details => Occurrence.Schedule is { } item
        ? (item.Kind == ScheduleKind.Boss ? $"{BossLootCatalog.DifficultyLabel(item.Difficulty)} {item.Boss}" : "일반 일정")
            + $" · {(item.Time is { } time ? time.ToString("HH:mm") : "하루 종일")}{(item.Weekly ? " · 매주 반복" : "")}{(Occurrence.Completed ? " · 완료" : "")}" : "일요일 · 썬데이 메이플";
    public string Note => Occurrence.Schedule?.Note ?? (Occurrence.Date < today.AddMonths(-6)
        ? "종료된 썬데이는 최근 6개월까지만 조회할 수 있어요." : Occurrence.Sunday is null
        ? Occurrence.Date <= today ? "선택한 날짜의 종료된 썬데이 공지를 확인합니다." : "아직 공지가 공개되지 않았어요. 공개되면 혜택 이미지를 표시합니다."
        : Images.Count == 0 ? "혜택 이미지를 불러오지 못했어요. 공지 새로고침으로 다시 시도하거나 공식 공지를 열어주세요." : "");
    public string CompleteLabel => Occurrence.Completed ? "완료 취소" : "완료";
    public Visibility EditVisibility => Occurrence.IsSunday ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LinkVisibility => ScheduleCalendar.OfficialUri(Occurrence.Sunday?.Url) ? Visibility.Visible : Visibility.Collapsed;
    public Uri? Link => ScheduleCalendar.OfficialUri(Occurrence.Sunday?.Url) ? new(Occurrence.Sunday!.Url) : null;
    public IReadOnlyList<ImageSource> Images { get; } = images ?? [];
}
