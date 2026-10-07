using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using MapleDay.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MapleDay.Models;

public sealed class CharacterCard(CharacterSummary summary) : INotifyPropertyChanged
{
    public string Ocid => summary.Ocid;
    public string Name { get; private set; } = summary.Name;
    public string Class { get; private set; } = summary.Class;
    public string World { get; private set; } = summary.World;
    public WriteableBitmap? WorldIconSource { get; private set; } = WorldIconAssets.Create(summary.World);
    public Visibility WorldIconVisibility => WorldIconSource is null ? Visibility.Collapsed : Visibility.Visible;
    public int Level { get; private set; } = summary.Level;
    public string LevelText { get; private set; } = $"Lv. {summary.Level} (—%)";
    public string Status { get; private set; } = "기본 정보 불러오는 중";
    public WriteableBitmap? ImageSource { get; private set; }
    public double ImageWidth => ImageSource?.PixelWidth ?? 68;
    public double ImageHeight => ImageSource?.PixelHeight ?? 80;
    public Visibility PlaceholderVisibility => ImageSource is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LoadingVisibility { get; private set; } = Visibility.Visible;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string UpdatedText => UpdatedAt is { } date ? $"마지막 갱신 {date.ToOffset(TimeSpan.FromHours(9)):yyyy.MM.dd HH:mm}" : "갱신 기록 없음";
    public string AccessibleName => $"{Name}, {World}, {Class}, {LevelText}. {UpdatedText}. {Status}";
    public string Tooltip => $"{World} · {UpdatedText}\n{Status}";
    public bool SchedulerAdded { get; private set; }
    public bool CanAddToScheduler => !SchedulerAdded;
    public string SchedulerButtonText => SchedulerAdded ? "스케줄러에 추가됨" : "스케줄러에 추가";
    public string SchedulerButtonAccessibleName => $"{Name} {SchedulerButtonText}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetSchedulerAdded(bool added)
    {
        if (SchedulerAdded == added) return;
        SchedulerAdded = added;
        NotifyAll();
    }

    public void Apply(CharacterBasic basic, CroppedCharacterImage? image, bool imageFailed, DateTimeOffset? updatedAt = null)
    {
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(basic.Name)) Name = basic.Name;
        if (!string.IsNullOrWhiteSpace(basic.Class)) Class = basic.Class;
        if (!string.IsNullOrWhiteSpace(basic.World) && World != basic.World)
        {
            World = basic.World;
            WorldIconSource = WorldIconAssets.Create(World);
        }
        Level = basic.Level ?? Level;
        LevelText = $"Lv. {Level} ({(string.IsNullOrWhiteSpace(basic.ExpRate) ? "—" : basic.ExpRate)}%)";
        Status = "기본 정보 조회 완료";
        LoadingVisibility = Visibility.Collapsed;
        if (image is not null)
        {
            ImageSource = new WriteableBitmap(image.Width, image.Height);
            using var pixels = ImageSource.PixelBuffer.AsStream();
            pixels.Write(image.Pixels);
            ImageSource.Invalidate();
        }
        else Status = imageFailed ? "캐릭터 이미지를 불러오지 못했습니다" : "제공된 캐릭터 이미지가 없습니다";
        NotifyAll();
    }

    public void MarkFailed(string message)
    {
        Status = message;
        LoadingVisibility = Visibility.Collapsed;
        NotifyAll();
    }

    public void BeginLoad()
    {
        Status = "기본 정보 불러오는 중";
        LoadingVisibility = Visibility.Visible;
        NotifyAll();
    }

    public void MarkCanceled()
    {
        if (LoadingVisibility == Visibility.Visible)
            MarkFailed("조회가 중단되었습니다. 새로고침으로 다시 조회할 수 있습니다");
    }

    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
