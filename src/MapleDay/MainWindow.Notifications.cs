using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using MapleDay.Core;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay;

public sealed partial class MainWindow
{
    private bool _notificationSettingsOpen;
    private void NotificationSettingsButton_Click(object sender, RoutedEventArgs e) => ShowNotificationSettings(true);
    private void NotificationListButton_Click(object sender, RoutedEventArgs e) => ShowNotificationSettings(false);
    private void ShowNotificationSettings(bool open)
    {
        _notificationSettingsOpen = open;
        NotificationSettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        NotificationListPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        NotificationSettingsButton.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        ClearRemindersButton.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        NotificationListButton.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        NotificationsTitle.Text = open ? "알림 설정" : "알림";
        NotificationsPage.ChangeView(null, 0, null, disableAnimation: true);
        BackButton.IsEnabled = open || _backStack.Count > 0;
        if (open) { UpdateReminderSchedule(); AllRemindersToggle.Focus(FocusState.Programmatic); }
        else { RefreshReminderList(); NotificationSettingsButton.Focus(FocusState.Programmatic); }
    }

    public ObservableCollection<ReminderCard> ReminderNotices { get; } = [];
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource _reminderLifetime = new();
    private bool _reminderSettingsReady, _reminderChecking;
    private DateTimeOffset _reminderRetryAfter;
    private bool _startupRemindersEnabledForLaunch;
    private string? _startupReminderAccount;
    private HashSet<string> _startupReminderTargets = [];
    private HashSet<ReminderKind> _startupReminderKinds = [];
    private readonly HashSet<string> _startupReminderChecks = [];

    private ReminderOptions ReminderOption(ReminderKind kind) => kind switch
    {
        ReminderKind.DailyQuest => _settings.DailyQuestReminder,
        ReminderKind.WeeklyQuest => _settings.WeeklyQuestReminder, _ => _settings.WeeklyBossReminder
    };
    private void InitializeReminders()
    {
        _settings.DailyQuestReminder ??= new(); _settings.WeeklyQuestReminder ??= new(); _settings.WeeklyBossReminder ??= new();
        _settings.CheckedReminders ??= []; _settings.ReminderNotices ??= [];
        foreach (var kind in Enum.GetValues<ReminderKind>())
            ReminderOption(kind).HoursBefore = Math.Clamp(ReminderOption(kind).HoursBefore, 1, SchedulerReminders.MaximumHours(kind));
        AllRemindersToggle.IsOn = _settings.RemindersEnabled;
        StartupRemindersToggle.IsOn = _settings.RemindOnStartup;
        _startupRemindersEnabledForLaunch = _settings.RemindOnStartup;
        DailyReminderToggle.IsOn = _settings.DailyQuestReminder.Enabled; DailyReminderHours.Value = _settings.DailyQuestReminder.HoursBefore;
        UnionQuestReminderToggle.IsOn = _settings.UnionQuestReminderEnabled;
        WeeklyQuestReminderToggle.IsOn = _settings.WeeklyQuestReminder.Enabled; WeeklyQuestReminderHours.Value = _settings.WeeklyQuestReminder.HoursBefore;
        WeeklyBossReminderToggle.IsOn = _settings.WeeklyBossReminder.Enabled; WeeklyBossReminderHours.Value = _settings.WeeklyBossReminder.HoursBefore;
        _reminderSettingsReady = true;
        RefreshReminderList(); UpdateReminderSchedule();
        _reminderTimer.Tick += async (_, _) => { UpdateReminderSchedule(); await CheckRemindersAsync(); };
        Closed += (_, _) => { _reminderTimer.Stop(); _reminderLifetime.Cancel(); };
    }

    private void ReminderToggle_Toggled(object sender, RoutedEventArgs e) => SaveReminderPreferences();
    private void ReminderHours_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) => SaveReminderPreferences();
    private void SaveReminderPreferences()
    {
        if (!_reminderSettingsReady || _dataDeleting) return;
        var numbers = new[] { DailyReminderHours.Value, WeeklyQuestReminderHours.Value, WeeklyBossReminderHours.Value };
        if (numbers.Any(value => !double.IsFinite(value) || value != Math.Truncate(value)) || numbers[0] is < 1 or > 23 || numbers[1] is < 1 or > 167 || numbers[2] is < 1 or > 167)
        { ShowReminderMessage("알림 시간은 정수로 입력해주세요.", InfoBarSeverity.Warning); return; }
        _settings.RemindersEnabled = AllRemindersToggle.IsOn;
        _settings.RemindOnStartup = StartupRemindersToggle.IsOn;
        _settings.DailyQuestReminder.Enabled = DailyReminderToggle.IsOn;
        _settings.UnionQuestReminderEnabled = UnionQuestReminderToggle.IsOn;
        _settings.WeeklyQuestReminder.Enabled = WeeklyQuestReminderToggle.IsOn;
        _settings.WeeklyBossReminder.Enabled = WeeklyBossReminderToggle.IsOn;
        _settings.DailyQuestReminder.HoursBefore = (int)numbers[0];
        _settings.WeeklyQuestReminder.HoursBefore = (int)numbers[1];
        _settings.WeeklyBossReminder.HoursBefore = (int)numbers[2];
        try { _settings.Save(); ReminderMessage.IsOpen = false; }
        catch (Exception error) when (IsStorageError(error)) { ShowReminderMessage("알림 설정을 저장하지 못했어요. 이번 실행에만 적용됩니다.", InfoBarSeverity.Warning); }
        UpdateReminderSchedule();
    }

    private void UpdateReminderSchedule()
    {
        var now = DateTimeOffset.UtcNow;
        var controls = new[] { DailyReminderNext, WeeklyQuestReminderNext, WeeklyBossReminderNext };
        foreach (var kind in Enum.GetValues<ReminderKind>())
        {
            var option = ReminderOption(kind);
            controls[(int)kind].Text = !_settings.RemindersEnabled || !option.Enabled ? "알림 꺼짐"
                : $"다음 예정: {SchedulerReminders.NextDue(kind, now, option.HoursBefore).ToOffset(SchedulerReminders.Korea):MM-dd (ddd) HH:mm} · 초기화 {option.HoursBefore}시간 전";
        }
        DailyReminderHours.IsEnabled = _settings.RemindersEnabled && _settings.DailyQuestReminder.Enabled;
        UnionQuestReminderToggle.IsEnabled = _settings.RemindersEnabled && _settings.WeeklyQuestReminder.Enabled;
        WeeklyQuestReminderHours.IsEnabled = _settings.RemindersEnabled && _settings.WeeklyQuestReminder.Enabled;
        WeeklyBossReminderHours.IsEnabled = _settings.RemindersEnabled && _settings.WeeklyBossReminder.Enabled;
        StartupRemindersToggle.IsEnabled = _settings.RemindersEnabled;
        var reminderStatus = !_settings.RemindersEnabled ? "전체 알림이 꺼져 있어요."
            : _schedulerCharacters.Count == 0 ? "스케줄러에 캐릭터를 추가하면 미완료 항목 알림을 받을 수 있어요."
            : "스케줄러에 추가한 캐릭터의 미완료 퀘스트와 보스만 알립니다. 앱 실행 중 또는 트레이에 있을 때 동작합니다.";
        ReminderRuntimeStatus.Text = _windowsNotifications.Status + "\n" + reminderStatus;
    }

    private async Task CheckRemindersAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (_reminderChecking || _closed || _dataDeleting || !_settings.RemindersEnabled || !_hasLoadedCharacters
            || string.IsNullOrEmpty(_apiKey) || _schedulerCharacters.Count == 0 || now < _reminderRetryAfter) return;
        var key = _apiKey;
        var account = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        if (_startupRemindersEnabledForLaunch && _startupReminderAccount is null)
        {
            _startupReminderAccount = account;
            _startupReminderTargets = SchedulerAvatars.Select(character => character.Ocid).ToHashSet(StringComparer.Ordinal);
            _startupReminderKinds = Enum.GetValues<ReminderKind>().Where(kind => ReminderOption(kind).Enabled).ToHashSet();
        }
        var windows = Enum.GetValues<ReminderKind>().Where(kind => ReminderOption(kind).Enabled)
            .Select(kind => SchedulerReminders.Window(kind, now, ReminderOption(kind).HoursBefore)).ToArray();
        string CheckKey(ReminderWindow window, string ocid) => $"{account}|{window.PeriodKey}|{ocid}";
        string StartupKey(ReminderKind kind, string ocid) => $"{kind}|{ocid}";
        bool StartupPending(ReminderWindow window, string ocid) => _startupRemindersEnabledForLaunch
            && _startupReminderAccount == account && _startupReminderTargets.Contains(ocid) && _startupReminderKinds.Contains(window.Kind)
            && !_startupReminderChecks.Contains(StartupKey(window.Kind, ocid));
        bool NeedsCheck(ReminderWindow window, string ocid) => SchedulerReminders.ShouldCheck(window, now,
            _settings.CheckedReminders.ContainsKey(CheckKey(window, ocid)), StartupPending(window, ocid));
        var targets = SchedulerAvatars.Where(character => windows.Any(window => NeedsCheck(window, character.Ocid)))
            .Select(character => (character.Ocid, character.Name, World: character.Character.World, Level: character.Character.Level)).ToArray();
        if (targets.Length == 0) return;
        _reminderChecking = true;
        var states = new ConcurrentDictionary<string, SchedulerState>();
        var questHistories = new ConcurrentDictionary<string, IReadOnlyList<SchedulerSnapshot>>();
        var failed = new ConcurrentDictionary<string, byte>();
        try
        {
            await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = _reminderLifetime.Token }, async (target, token) =>
            {
                try
                {
                    SchedulerState state;
                    if (windows.Any(window => window.Kind == ReminderKind.WeeklyQuest && NeedsCheck(window, target.Ocid)))
                    {
                        var history = await new MapleDay.Services.SchedulerHistoryLoader(_api, _schedulerHistory).LoadAsync(target.Ocid, key, now, token);
                        state = history.Current;
                        questHistories[target.Ocid] = history.Snapshots;
                    }
                    else state = await _api.GetSchedulerAsync(target.Ocid, key, token);
                    // Missing data is not proof that every registered item is complete.
                    if (state.Daily is null && state.Weekly is null && state.Bosses is null) failed.TryAdd(target.Ocid, 0);
                    else states[target.Ocid] = state;
                }
                catch (Exception error) when (error is NexonApiException or HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
                { failed.TryAdd(target.Ocid, 0); }
            });
            if (_closed || _dataDeleting || key != _apiKey || !_settings.RemindersEnabled) return;
            now = DateTimeOffset.UtcNow;
            var today = SchedulerBossHistory.KoreanToday(now);
            if (questHistories.Count > 0)
                foreach (var character in SchedulerAvatars)
                    if (!questHistories.ContainsKey(character.Ocid))
                    {
                        var cached = character.History?.Snapshots;
                        if (cached is null)
                        {
                            try { cached = await _schedulerHistory.LoadAsync(key, character.Ocid, _reminderLifetime.Token); }
                            catch (Exception error) when (IsStorageError(error)) { cached = []; }
                        }
                        questHistories[character.Ocid] = cached;
                    }
            if (_closed || _dataDeleting || key != _apiKey || !_settings.RemindersEnabled) return;
            var notices = new List<ReminderNotice>();
            foreach (var window in windows)
            {
                var option = ReminderOption(window.Kind);
                var currentWindow = SchedulerReminders.Window(window.Kind, now, option.HoursBefore);
                if (!option.Enabled || currentWindow.PeriodKey != window.PeriodKey) continue;
                var characters = new List<ReminderCharacter>();
                var isStartup = false;
                foreach (var target in targets)
                {
                    var checkKey = CheckKey(window, target.Ocid);
                    if (!NeedsCheck(currentWindow, target.Ocid) || !_schedulerCharacters.TryGetValue(target.Ocid, out var currentCharacter)
                        || !states.TryGetValue(target.Ocid, out var state)) continue;
                    if (!SchedulerReminders.HasData(window.Kind, state)) { failed.TryAdd(target.Ocid, 0); continue; }
                    var world = state.World ?? currentCharacter.Character.World;
                    var extremeComplete = questHistories.TryGetValue(target.Ocid, out var history)
                        && SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, today, world);
                    if (!extremeComplete && window.Kind == ReminderKind.WeeklyQuest)
                        extremeComplete = questHistories.Values.Count(history =>
                            SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, today, world)) >= 2;
                    var pending = SchedulerReminders.Pending(window.Kind, state, currentCharacter.Character.Level,
                        _settings.UnionQuestReminderEnabled, extremeComplete);
                    if (StartupPending(window, target.Ocid))
                    {
                        _startupReminderChecks.Add(StartupKey(window.Kind, target.Ocid));
                        isStartup = true;
                    }
                    // Startup checks outside the due window must not consume the scheduled reminder.
                    if (currentWindow.IsDue(now)) _settings.CheckedReminders[checkKey] = window.Reset;
                    if (pending.Count > 0) characters.Add(new(target.Ocid, state.Name ?? target.Name, state.World ?? target.World, pending));
                }
                if (characters.Count > 0) notices.Add(new() { Kind = window.Kind, CreatedAt = now, Reset = window.Reset, Characters = characters, IsStartup = isStartup });
            }
            foreach (var notice in notices) _settings.ReminderNotices.Insert(0, notice);
            if (AppWindow.IsVisible && _currentPage == "notifications" && !_notificationSettingsOpen) _settings.MarkRemindersRead();
            _settings.ReminderNotices = _settings.ReminderNotices.Take(100).ToList();
            foreach (var old in _settings.CheckedReminders.Where(pair => pair.Value < now.AddDays(-60)).Select(pair => pair.Key).ToArray()) _settings.CheckedReminders.Remove(old);
            try { _settings.Save(); }
            catch (Exception error) when (IsStorageError(error)) { ShowReminderMessage("알림 기록을 저장하지 못했어요. 이번 실행에는 중복 알림을 방지합니다.", InfoBarSeverity.Warning); }
            RefreshReminderList();
            foreach (var notice in notices)
            {
                if (!_notificationReady) continue;
                var contents = notice.Characters.SelectMany(character => character.Pending).Distinct(StringComparer.Ordinal).ToArray();
                var contentPreview = string.Join("\n", contents.Take(3)) + (contents.Length > 3 ? $"\n외 {contents.Length - 3}개 콘텐츠" : "");
                if (!_windowsNotifications.Show(notice.Title, [notice.Summary, contentPreview],
                    new Dictionary<string, string> { ["reminder"] = notice.Id }))
                {
                    UpdateReminderSchedule();
                    ShowReminderMessage(_windowsNotifications.Status, InfoBarSeverity.Warning);
                }
            }
            if (!failed.IsEmpty)
            {
                _reminderRetryAfter = now.AddMinutes(2);
                ShowReminderMessage($"캐릭터 {failed.Count}명의 상태를 조회하지 못했어요. 잠시 후 다시 확인합니다.", InfoBarSeverity.Warning);
            }
        }
        catch (OperationCanceledException) when (_reminderLifetime.IsCancellationRequested) { }
        finally { _reminderChecking = false; }
    }

    private void RefreshReminderList()
    {
        if (AppWindow.IsVisible && _currentPage == "notifications" && !_notificationSettingsOpen && !_closed && !_dataDeleting && _settings.MarkRemindersRead())
        {
            try { _settings.Save(); }
            catch (Exception error) when (IsStorageError(error)) { ShowReminderMessage("읽음 상태를 저장하지 못했어요.", InfoBarSeverity.Warning); }
        }
        ReminderNotices.Clear();
        foreach (var notice in _settings.ReminderNotices) ReminderNotices.Add(new(notice));
        ReminderEmpty.Visibility = ReminderNotices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearRemindersButton.IsEnabled = ReminderNotices.Count > 0;
        var unread = ReminderNotices.Count(notice => !notice.Read);
        ReminderBadge.Value = unread; ReminderBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DeleteReminderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) RemoveReminderNotices(notice => notice.Id == id);
    }

    private void ClearRemindersButton_Click(object sender, RoutedEventArgs e)
        => RemoveReminderNotices(_ => true);

    private void RemoveReminderNotices(Func<ReminderNotice, bool> remove)
    {
        if (_closed || _dataDeleting) return;
        var previous = _settings.ReminderNotices;
        var remaining = previous.Where(notice => !remove(notice)).ToList();
        if (remaining.Count == previous.Count) return;
        _settings.ReminderNotices = remaining;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            _settings.ReminderNotices = previous;
            ShowReminderMessage("알림 삭제를 저장하지 못했어요. 다시 시도해주세요.", InfoBarSeverity.Warning);
            return;
        }
        // Keep the sent-reminder checkpoints so deleting history does not resend it.
        RefreshReminderList();
    }
    private void ReconnectNotifications_Click(object sender, RoutedEventArgs e)
    {
        _windowsNotifications.Initialize(SupportNotificationInvoked);
        UpdateReminderSchedule();
        ShowReminderMessage(_windowsNotifications.Status, _notificationReady ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }
    private void ShowReminderMessage(string message, InfoBarSeverity severity)
    { ReminderMessage.Title = message; ReminderMessage.Severity = severity; ReminderMessage.IsOpen = true; }
}
