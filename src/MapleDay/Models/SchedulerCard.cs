using System.ComponentModel;
using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;

namespace MapleDay.Models;

public sealed class SchedulerTile(SchedulerEntry entry, int characterLevel, string ocid, DateOnly date, Func<string, int>? partySize = null) : INotifyPropertyChanged
{
    public string Ocid => ocid;
    public string PartyId { get; } = BossParty.RecordId(entry.Name, SchedulerBossHistory.Cycle(entry.Cycle) ?? BossCycle.Daily, date);
    private int _partySize = BossParty.Clamp(entry.Name, partySize?.Invoke(BossParty.RecordId(entry.Name, SchedulerBossHistory.Cycle(entry.Cycle) ?? BossCycle.Daily, date)) ?? 1);
    public int PartySize => _partySize;
    public int MaximumPartySize => BossParty.Maximum(entry.Name);
    public string PartyText => $"{PartySize}인";
    public string PartyCount => PartySize.ToString();
    public string PartyAccessibleName => $"{Name}, 파티 인원 {PartySize}인 설정, 최대 {MaximumPartySize}인";
    public bool CanSetPartySize => !IsLevelBlocked;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetPartySize(int size)
    {
        _partySize = BossParty.Clamp(Name, size);
        PropertyChanged?.Invoke(this, new(nameof(PartyText)));
        PropertyChanged?.Invoke(this, new(nameof(PartyCount)));
        PropertyChanged?.Invoke(this, new(nameof(PartyAccessibleName)));
    }
    public string Name => entry.Name;
    public string Detail => entry.Detail;
    public Visibility CompletionVisibility => entry.Complete ? Visibility.Visible : Visibility.Collapsed;
    public bool IsComplete => entry.Complete;
    public bool IsQuest => entry.Type == "quest";
    // Daily quests, epic dungeon and guild labels use stronger edge coverage; shorter
    // content/boss names keep their lighter rendering.
    public double NameStrokeStrength => entry.Section == SchedulerSection.Daily && IsQuest
        || entry.Name.Contains("에픽 던전", StringComparison.Ordinal)
        || entry.Name.StartsWith("[길드]", StringComparison.Ordinal)
        || entry.Name.Contains("지하 수로", StringComparison.Ordinal)
        || entry.Name.Contains("플래그 레이스", StringComparison.Ordinal) ? 1.5 : 1.0;
    public int RequiredLevel => SchedulerRequirements.RequiredLevel(entry);
    public bool IsLevelBlocked => SchedulerRequirements.IsLevelBlocked(entry, characterLevel);
    public Visibility LevelBlockedVisibility => IsLevelBlocked ? Visibility.Visible : Visibility.Collapsed;
    public SolidColorBrush NameForeground { get; } = new(Microsoft.UI.ColorHelper.FromArgb(255,
        SchedulerRequirements.IsLevelBlocked(entry, characterLevel) ? (byte)165 : (byte)59,
        SchedulerRequirements.IsLevelBlocked(entry, characterLevel) ? (byte)175 : (byte)84,
        SchedulerRequirements.IsLevelBlocked(entry, characterLevel) ? (byte)181 : (byte)110));
    public string? Cycle => entry.Cycle;
    public Visibility ContentActionVisibility => IsComplete || IsLevelBlocked ? Visibility.Collapsed : Visibility.Visible;
    public Visibility BossActionVisibility => IsLevelBlocked ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProgressVisibility => IsComplete || IsLevelBlocked || SchedulerReminders.IsExtremeMonsterParkQuest(Name)
        ? Visibility.Collapsed : Visibility.Visible;
    public string RowActionImage => $"ms-appx:///Assets/Scheduler/UI/main_entity_{(IsQuest ? SchedulerRequirements.IsQuestReady(entry) ? "btComplete_normal" : entry.QuestState switch { "1" => "btComplete_disabled", "2" => "btMoveCompleted_normal", _ => "btStart_normal" } : IsComplete ? "btMoveCompleted_normal" : "btMove_normal")}_0.png";
    public string Progress => Name.Contains("에픽 던전") ? $"STAGE {SchedulerNumberFormat.Count(entry.Now)}" : SchedulerNumberFormat.Count(entry.Now);
    public string ProgressUnit => Name.Contains("에픽 던전") ? ""
        : Name.Contains("무릉도장") ? " 층"
        : Name.Contains("지하 수로") || Name.Contains("플래그 레이스") ? " 점"
        : entry.Maximum > 0 ? $" / {SchedulerNumberFormat.Count(entry.Maximum)}" : "";
    public string AccessibleName => IsLevelBlocked ? $"{Name}, 입장 조건 미달, 필요한 레벨 {RequiredLevel}, 현재 레벨 {characterLevel}" : $"{Name}, {Detail}, {(entry.Complete ? "클리어" : "미완료")}";
    public string Tooltip => IsLevelBlocked ? $"레벨 조건을 확인해 주세요.\n입장 Lv. {RequiredLevel} · 캐릭터 Lv. {characterLevel}" : AccessibleName;
    public WriteableBitmap? BossIcon { get; } = CreateIcon(SchedulerIconAssets.BossFile(entry.Name));
    public WriteableBitmap? DifficultyIcon { get; } = CreateIcon(SchedulerIconAssets.DifficultyFile(entry.Difficulty));
    public Visibility BossIconVisibility => BossIcon is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DifficultyIconVisibility => DifficultyIcon is null ? Visibility.Collapsed : Visibility.Visible;
    private static WriteableBitmap? CreateIcon(string? file) => file is null ? null : LocalImageCache.Get($"Scheduler/{file}");
}

public sealed class SchedulerRow
{
    public SchedulerTile? Tile { get; }
    public string BackgroundImage { get; }
    public string? HeaderText { get; }
    public string WeeklyCount { get; } = "";
    public string WeeklyLimit { get; } = "";
    public Visibility ContentVisibility { get; } = Visibility.Collapsed;
    public Visibility BossVisibility { get; } = Visibility.Collapsed;
    public Visibility QuestHeaderVisibility { get; } = Visibility.Collapsed;
    public Visibility WeeklyLimitVisibility { get; } = Visibility.Collapsed;
    public Visibility HeaderTextVisibility => HeaderText is null ? Visibility.Collapsed : Visibility.Visible;
    public string AccessibleName => Tile?.AccessibleName ?? HeaderText ?? "콘텐츠 구분";
    public string Tooltip => Tile?.Tooltip ?? AccessibleName;
    public SchedulerRow(SchedulerTile tile, bool boss)
    {
        Tile = tile;
        BackgroundImage = Image(boss ? "listBoss" : "list");
        ContentVisibility = boss ? Visibility.Collapsed : Visibility.Visible;
        BossVisibility = boss ? Visibility.Visible : Visibility.Collapsed;
    }
    public SchedulerRow(string header, long? clear = null, long? limit = null, string? text = null)
    {
        BackgroundImage = Image(header);
        HeaderText = text;
        QuestHeaderVisibility = header == "quest" ? Visibility.Visible : Visibility.Collapsed;
        if (clear is not null && limit is not null)
        {
            WeeklyLimitVisibility = Visibility.Visible;
            WeeklyCount = clear.Value.ToString();
            WeeklyLimit = limit.Value.ToString();
        }
    }
    private static string Image(string kind) => $"ms-appx:///Assets/Scheduler/UI/main_entity_back_{kind}.png";
}

public sealed class SchedulerCharacter(CharacterCard character) : INotifyPropertyChanged
{
    public CharacterCard Character => character;
    public string Ocid => character.Ocid;
    public string Name => character.Name;
    public string Subtitle => $"{character.World} · Lv. {character.Level} · {character.Class}";
    public string Status { get; private set; } = "새로고침하여 스케줄러를 조회해주세요.";
    public Visibility LoadingVisibility { get; private set; } = Visibility.Collapsed;
    public ObservableCollection<SchedulerTile> Daily { get; } = [];
    public ObservableCollection<SchedulerTile> Weekly { get; } = [];
    public ObservableCollection<SchedulerTile> Bosses { get; } = [];
    public ObservableCollection<SchedulerTile> UnregisteredBosses { get; } = [];
    public ObservableCollection<SchedulerRow> DailyRows { get; } = [];
    public ObservableCollection<SchedulerRow> WeeklyRows { get; } = [];
    public ObservableCollection<SchedulerRow> BossRows { get; } = [];
    public ObservableCollection<string> BossHistoryRows { get; } = [];
    public ObservableCollection<BossIncomeRow> IncomeRows { get; } = [];
    public ObservableCollection<string> IncomeHistoryRows { get; } = [];
    public BossIncomeResult? Income { get; private set; }
    public string IncomeSummary { get; private set; } = "수익 기록을 불러오는 중…";
    public string IncomeStatus { get; private set; } = "";
    private SchedulerHistoryResult? _incomeHistory;
    public SchedulerHistoryResult? History => _incomeHistory;
    public int? WeeklyCleared { get; private set; }
    public double WeeklyProgressValue => (WeeklyCleared ?? 0) / (double)BossIncome.WeeklyCap;
    public string WeeklyProgressText => $"{(WeeklyCleared is { } cleared ? cleared.ToString() : "—")}/{BossIncome.WeeklyCap}";
    public string WeeklyProgressTooltip => $"{Name} · 주간 보스 {WeeklyProgressText}\n{(WeeklyCleared is null ? Status : HistoryStatus)}\n클릭하면 이 캐릭터 보기 · 다시 클릭하면 전체 보기";
    public string AvatarAccessibleName => $"{Name}, {character.World}, 주간 보스 {WeeklyProgressText}{(IsSelected ? ", 선택됨. 다시 누르면 전체 보기" : ". 선택하여 스케줄러 보기")}";
    public bool IsSelected { get; private set; }
    public Visibility SelectedVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ViewVisibility { get; private set; } = Visibility.Visible;
    public string HistoryStatus { get; private set; } = "";
    public DateOnly? HistoryDate { get; private set; }
    public Visibility NoDailyVisibility => DailyRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoWeeklyVisibility => WeeklyRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoBossVisibility => BossRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DailyVisibility => Daily.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WeeklyVisibility => Weekly.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BossVisibility => Bosses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnregisteredBossVisibility => UnregisteredBosses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool Loading { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetSelected(bool selected)
    {
        if (IsSelected == selected) return;
        IsSelected = selected;
        PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(AvatarAccessibleName)));
    }

    public void SetViewVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (ViewVisibility == visibility) return;
        ViewVisibility = visibility;
        PropertyChanged?.Invoke(this, new(nameof(ViewVisibility)));
    }

    public void BeginLoad()
    {
        Loading = true;
        LoadingVisibility = Visibility.Visible;
        Status = "스케줄러를 불러오는 중…";
        Notify();
    }

    public void RefreshCharacter(Func<string, int>? partySize = null)
    {
        if (_incomeHistory is { } history)
        {
            var updatedAt = UpdatedAt;
            var loading = Loading;
            var loadingVisibility = LoadingVisibility;
            var status = Status;
            Apply(history, partySize);
            UpdatedAt = updatedAt;
            Loading = loading; LoadingVisibility = loadingVisibility; Status = status;
        }
        Notify();
    }

    public void Apply(SchedulerHistoryResult history, Func<string, int>? partySize = null)
    {
        _incomeHistory = history;
        RefreshIncome(partySize);
        HistoryDate = history.Today;
        var state = history.Current;
        var periodCounts = Enum.GetValues<BossCycle>().ToDictionary(cycle => cycle,
            cycle => SchedulerBossHistory.Count(cycle, history.Today, history.Snapshots, state, history.Today));
        WeeklyCleared = periodCounts[BossCycle.Weekly].Cleared;
        BossHistoryRows.Clear();
        foreach (var period in SchedulerBossHistory.PreviousPeriods(history.Snapshots, history.Today))
            BossHistoryRows.Add($"{period.Range} · {period.Display}");
        HistoryStatus = $"과거 기록 {history.Snapshots.Count(snapshot => snapshot.Date < history.Today)}일 보관";
        if (history.FailedDates > 0) HistoryStatus += $" · {history.FailedDates}일 조회 실패, 새로고침으로 재시도";
        if (history.StorageFailed) HistoryStatus += " · 로컬 저장 실패, 이번 조회 결과만 표시";
        if (periodCounts.Values.Where(period => period.Cycle != BossCycle.Daily).Any(period => !period.CompleteRange)) HistoryStatus += " · 일부 구간은 API 조회 범위 밖의 기록이 없어 처치 수가 적게 표시될 수 있어요";
        if (BossHistoryRows.Count == 0) BossHistoryRows.Add("저장된 이전 주간·월간 기록이 아직 없어요.");
        Daily.Clear(); Weekly.Clear(); Bosses.Clear(); UnregisteredBosses.Clear();
        var entries = SchedulerEntries.Create(state);
        foreach (var entry in entries)
        {
            var target = entry.Section switch { SchedulerSection.Daily => Daily, SchedulerSection.Weekly => Weekly, SchedulerSection.Boss => Bosses, _ => UnregisteredBosses };
            target.Add(new(entry, character.Level, Ocid, history.Today, partySize));
        }
        BuildContentRows(DailyRows, Daily);
        BuildContentRows(WeeklyRows, Weekly);
        BossRows.Clear();
        foreach (var (cycle, header) in new[] { ("bossWeekly", "weekly"), ("bossMonthly", "monthly"), ("bossDaily", "daily") })
        {
            var rows = Bosses.Where(tile => tile.Cycle == cycle || tile.Cycle == cycle[4..].ToLowerInvariant()).ToArray();
            BossRows.Add(new(header, header == "weekly" ? state.WeeklyBossClearCount : null,
                header == "weekly" ? state.WeeklyBossClearLimit : null));
            foreach (var tile in rows) BossRows.Add(new(tile, true));
        }
        var other = Bosses.Where(tile => tile.Cycle is not ("bossWeekly" or "weekly" or "bossDaily" or "daily" or "bossMonthly" or "monthly")).ToArray();
        if (other.Length > 0)
        {
            BossRows.Add(new("contents", text: "등록한 보스"));
            foreach (var tile in other) BossRows.Add(new(tile, true));
        }
        if (UnregisteredBosses.Count > 0)
        {
            BossRows.Add(new("contents", text: "미등록 보스 · 클리어 기록"));
            foreach (var tile in UnregisteredBosses) BossRows.Add(new(tile, true));
        }
        UpdatedAt = DateTimeOffset.Now;
        var count = entries.Count;
        Status = count == 0 ? $"등록된 콘텐츠나 미등록 보스 클리어 기록이 없어요. · {UpdatedAt:HH:mm} 조회"
            : $"{entries.Count(entry => entry.Complete)} / {count} 완료 · {UpdatedAt:HH:mm} 조회";
        if (history.FallbackDate is { } savedDate)
            Status = $"현재 접속 데이터 없음 · {savedDate:yyyy.MM.dd} 저장 기준 · 초기화 반영";
        Loading = false;
        LoadingVisibility = Visibility.Collapsed;
        Notify();
    }

    public void RefreshIncome(Func<string, int>? partySize = null)
    {
        if (_incomeHistory is not { } history) return;
        foreach (var tile in Bosses.Concat(UnregisteredBosses)) tile.SetPartySize(partySize?.Invoke(tile.PartyId) ?? 1);
        Income = BossIncome.Calculate(history.Snapshots, history.Today, partySize);
        IncomeSummary = $"이번 주 {BossIncome.Money(Income.Weekly)} · 이번 달 {BossIncome.Money(Income.Monthly)} · 누적 {BossIncome.Money(Income.Total)}";
        IncomeStatus = $"{Income.FirstDate:yyyy-MM-dd}부터 저장된 기록 · 일일 결정 제외";
        if (!Income.CompleteRange(BossCycle.Monthly) || !Income.CompleteRange(BossCycle.Weekly)) IncomeStatus += " · 일부 기간의 기록이 없어 합계가 적게 표시될 수 있어요";
        if (Income.Unpriced > 0) IncomeStatus += $" · 가격 미확인 {Income.Unpriced}건 제외";
        if (Income.CapExcluded > 0) IncomeStatus += $" · 주간 상한 초과 {Income.CapExcluded}건 제외";
        IncomeRows.Clear();
        foreach (var record in Income.Records) IncomeRows.Add(new(Ocid, record));
        IncomeHistoryRows.Clear();
        foreach (var cycle in new[] { BossCycle.Weekly, BossCycle.Monthly })
            foreach (var period in Income.Periods(cycle))
                IncomeHistoryRows.Add($"{(cycle == BossCycle.Weekly ? "주간" : "월간")} {period.Start:yyyy-MM-dd} ~ {period.End:MM-dd} · {BossIncome.Money(period.Meso)} · 결정 {period.Count}개{(period.CompleteRange ? "" : " · 일부 기록")}");
        if (IncomeRows.Count == 0) IncomeStatus = "저장된 주간·월간 클리어 기록이 없어요.";
        Notify();
    }

    public void Failed(string message)
    {
        Loading = false;
        LoadingVisibility = Visibility.Collapsed;
        Status = message + (UpdatedAt is not null ? $" · {UpdatedAt:HH:mm} 조회 결과 유지" : "");
        Notify();
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    private static void BuildContentRows(ObservableCollection<SchedulerRow> rows, IEnumerable<SchedulerTile> tiles)
    {
        rows.Clear();
        foreach (var quest in new[] { false, true })
        {
            var group = tiles.Where(tile => tile.IsQuest == quest).ToArray();
            if (group.Length == 0) continue;
            rows.Add(new(quest ? "quest" : "contents"));
            foreach (var tile in group) rows.Add(new(tile, false));
        }
    }
}
