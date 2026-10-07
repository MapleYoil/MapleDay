using System.ComponentModel;
using System.Globalization;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MapleDay.Models;

public sealed class LevelCharacter(CharacterCard character) : INotifyPropertyChanged, IDisposable
{
    public CharacterCard Character { get; private set; } = character;
    public string Ocid => Character.Ocid;
    public string Name => Character.Name;
    public WriteableBitmap? ImageSource => Character.ImageSource;
    public double ImageWidth => Character.ImageWidth;
    public double ImageHeight => Character.ImageHeight;
    public string Identity => $"{Character.World} · {Character.Class}";
    public ExperienceHistoryResult? History { get; private set; }
    public ExperienceTrend? Trend { get; private set; }
    public ExperienceDay? TodayGain { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool Loading { get; private set; }
    public string Status { get; private set; } = "경험치 기록 조회 대기";
    public bool Selected { get; private set; }
    public Brush Background => new SolidColorBrush(Selected ? Microsoft.UI.ColorHelper.FromArgb(255, 238, 238, 238) : Microsoft.UI.Colors.White);
    public string LevelText => History?.Current is { } value ? $"Lv. {value.Level} ({value.Percent:0.###}%)" : Character.LevelText;
    public double Percent => (double)(History?.Current?.Percent ?? 0);
    public string TodayAmount
    {
        get
        {
            if (TodayGain?.Value is not { } current || History is not { } history)
                return Loading ? "불러오는 중…" : "현재 데이터 없음";
            if (TodayGain.Gained is not { } gain) return "비교 기록 부족";
            var required = ExperienceHistory.Required(current, history.Today, history.Snapshots);
            return ExperienceHistory.Amount(gain) + (required > 0
                ? $" ({(gain * 100 / required.Value).ToString("0.###", CultureInfo.InvariantCulture)}%)" : "");
        }
    }
    public string TodayCaption => History is { } history && history.Snapshots.Where(snapshot => snapshot.Date == history.Today && !snapshot.Final)
            .MaxBy(snapshot => snapshot.FetchedAt) is { } live
        ? $"{history.Today:MM/dd} · {live.FetchedAt.ToOffset(ExperienceHistory.Korea):HH:mm} 조회 · 전날 대비"
        : "현재 경험치를 조회하면 표시됩니다.";
    public string Average => Trend?.Average is { } average
        ? ExperienceHistory.Amount(average) + $" ({(Trend.AveragePercent is { } percent ? percent.ToString("0.###", CultureInfo.InvariantCulture) : "—")}%)"
        : "데이터 부족";
    public string AverageCaption => Trend is null ? "최근 7일 일평균" : $"{Trend.EndDate.AddDays(-6):MM/dd} ~ {Trend.EndDate:MM/dd} · {Trend.KnownDays}/7일 확인";
    public string LevelUp => History?.Current?.Level >= 300 ? "최고 레벨 도달"
        : Trend?.Average is null ? "7일 기록이 필요해요"
        : Trend.Average <= 0 ? "경험치 증가가 없어요"
        : Trend.LevelUpDays is not { } days ? "필요 경험치 확인 중"
        : ExperienceHistory.LevelUpDuration(days, ExperienceHistory.Today(DateTimeOffset.UtcNow));
    public string LevelUpDate => Trend?.LevelUpDays is { } days && ExperienceHistory.LevelUpAt(days, DateTimeOffset.UtcNow) is { } date
        ? $"{date:yyyy-MM-dd HH:mm} 예상" : "";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify() => PropertyChanged?.Invoke(this, new(string.Empty));
    private void CharacterChanged(object? sender, PropertyChangedEventArgs args) => Notify();
    public void Rebind(CharacterCard value)
    {
        Character.PropertyChanged -= CharacterChanged;
        Character = value;
        Character.PropertyChanged += CharacterChanged;
        Notify();
    }
    public void Dispose() => Character.PropertyChanged -= CharacterChanged;
    public void Select(bool selected) { if (Selected == selected) return; Selected = selected; Notify(); }
    public void Begin() { Loading = true; Status = "경험치 기록을 불러오는 중…"; Notify(); }
    public void Apply(ExperienceHistoryResult result, bool done)
    {
        History = result;
        Trend = ExperienceHistory.Trend(result.Snapshots, result.Current, result.Today, result.FinalDate);
        TodayGain = ExperienceHistory.TodayGain(result.Snapshots, result.Today);
        Loading = !done;
        if (done) UpdatedAt = DateTimeOffset.UtcNow;
        Status = done ? $"저장된 날짜 {result.Snapshots.Count(snapshot => snapshot.Final && snapshot.Value is not null)}일 · {DateTimeOffset.UtcNow.ToOffset(ExperienceHistory.Korea):HH:mm} 조회"
            : $"과거 기록 수집 {result.Completed} / {result.Requested}일";
        if (result.FailedDates > 0) Status += $" · {result.FailedDates}일 조회 실패";
        if (result.StorageFailed) Status += " · 기록 저장 실패";
        Notify();
    }
    public void Failed(string message) { Loading = false; Status = message; Notify(); }
}

public sealed class LevelHistoryRow(ExperienceDay day)
{
    public string Date => day.Date.ToString("yyyy-MM-dd");
    public string Level => day.Value is { } value ? $"Lv. {value.Level}" : "—";
    public string Experience => day.Value is { } value ? $"{value.Exp:N0} ({value.Percent:0.###}%)" : "데이터 없음";
    public string Gained => day.Gained is { } gain ? ExperienceHistory.Amount(gain) : "—";
    public string Note => day.LevelsGained is > 0 ? $"+{day.LevelsGained}레벨" : day.Value is not null && day.Gained is null ? "비교 기록 부족" : "";
    public string Tooltip => $"{Date} · {Level}\n보유 경험치 {Experience}\n일일 경험치 변화 {Gained} {Note}";
}

public sealed class LevelGainBar(ExperienceDay? day, DateOnly date, decimal maximum)
{
    public string Date => date.ToString("MM/dd");
    public string Amount => day?.Gained is { } gain ? ExperienceHistory.Amount(gain) : "—";
    public double Height => day?.Gained is { } gain && maximum > 0 ? (double)(Math.Abs(gain) / maximum) * 72 : 0;
    public Brush Color => new SolidColorBrush(day?.Gained < 0 ? Microsoft.UI.ColorHelper.FromArgb(255, 196, 91, 38)
        : Microsoft.UI.ColorHelper.FromArgb(255, 55, 125, 255));
    public string Tooltip => $"{date:yyyy-MM-dd} · 일일 경험치 변화 {Amount}";
}
