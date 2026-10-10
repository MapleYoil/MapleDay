using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace MapleDay;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer _calendarTimer = null!;
    private readonly CancellationTokenSource _calendarLifetime = new();
    private bool _calendarBusy;
    private DateTimeOffset? _calendarAttempt;
    private readonly SemaphoreSlim _calendarGate = new(1, 1);
    private int _calendarSelections;
    private readonly HttpClient _calendarHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    private SundayArchive _sundayArchive = null!;
    private SundayImageCache _sundayImages = null!;
    private bool _sundayChecking, _holidaysChecking;
    private DateTimeOffset? _sundayRetryAfter, _holidayRetryAfter;
    private DateOnly? _sundayAttemptDate;
    private readonly Dictionary<int, HashSet<DateOnly>> _publicHolidays = [];

    private void InitializeCalendar()
    {
        CalendarPanel.SetContext(_settings, SchedulerAvatars);
        _sundayArchive = new(_calendarHttp); _sundayImages = new(_calendarHttp);
        try { SundayImageCache.RemoveLegacyImages(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { CalendarPanel.SetNoticeStatus("이전 버전의 이미지 파일을 정리하지 못했어요. 새 이미지는 파일로 저장하지 않습니다."); }
        _calendarTimer = DispatcherQueue.CreateTimer();
        _calendarTimer.Interval = TimeSpan.FromMinutes(1);
        _calendarTimer.Tick += async (_, _) =>
        {
            await RefreshPublicHolidaysAsync();
            await CheckSundayAnnouncementAsync();
            if (!_settings.SundayNotifications || !_settings.RemindersEnabled) await RefreshCalendarAsync();
        };
        CalendarPanel.NoticeRefreshRequested += async (_, _) => await RefreshCalendarAsync(force: true);
        CalendarPanel.SundaySelected += async (_, date) => await LoadSelectedSundayAsync(date);
        Closed += (_, _) => StopCalendar();
        UpdateSundayReminderSchedule();
    }
    private void StopCalendar() { _calendarTimer.Stop(); _calendarLifetime.Cancel(); }
    private async Task RefreshCalendarAsync(bool force = false)
    {
        if (_closed || _dataDeleting || _calendarBusy) return;
        if (_currentPage == "calendar" || force) CalendarPanel.SetContext(_settings, SchedulerAvatars);
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            CalendarPanel.SetNoticeStatus("API 키를 연결하면 썬데이 메이플 공지를 자동으로 확인합니다. 개인 일정은 키 없이도 등록할 수 있어요.");
            if (_currentPage == "calendar" && CalendarPanel.ShowingSunday) await LoadSelectedSundayAsync(CalendarPanel.SelectedDate, force);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (!force && _settings.SundayNotifications && _settings.RemindersEnabled
            && !SundayAnnouncements.Upcoming(now, IsPublicHoliday).IsDue(now)) return;
        if (!force && (!ScheduleCalendar.NoticeRefreshDue(_settings.EventNoticesCheckedAt, now)
            || _calendarAttempt is { } attempt && now >= attempt && now - attempt < TimeSpan.FromMinutes(5)))
        {
            if (_currentPage == "calendar" && CalendarPanel.ShowingSunday) await LoadSelectedSundayAsync(CalendarPanel.SelectedDate);
            return;
        }
        _calendarBusy = true; _calendarAttempt = now;
        var key = _apiKey;
        CalendarPanel.SetNoticeStatus("썬데이 메이플 공지를 확인하는 중…");
        var entered = false;
        try
        {
            await _calendarGate.WaitAsync(_calendarLifetime.Token); entered = true;
            var notices = await SundayNotices.RefreshAsync(_api, key, _settings.SundayNotices ?? [], now, _calendarLifetime.Token);
            if (_closed || _dataDeleting || key != _apiKey) return;
            var previous = _settings.SundayNotices ?? []; var previousTime = _settings.EventNoticesCheckedAt;
            _settings.SundayNotices = notices.ToList(); _settings.EventNoticesCheckedAt = now;
            try { _settings.Save(); }
            catch { _settings.SundayNotices = previous; _settings.EventNoticesCheckedAt = previousTime; throw; }
            CalendarPanel.SetContext(_settings, SchedulerAvatars);
            CalendarPanel.SetNoticeStatus($"공지 확인 {now.ToOffset(TimeSpan.FromHours(9)):MM.dd HH:mm} · 썬데이 알림의 공개일은 한국 공휴일을 반영합니다.");
        }
        catch (OperationCanceledException) when (_calendarLifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is NexonApiException or HttpRequestException or OperationCanceledException || IsStorageError(error))
        {
            if (!_closed && !_dataDeleting)
                CalendarPanel.SetNoticeStatus("공지를 확인하지 못했어요. 저장된 일정과 공지는 유지하며 잠시 후 다시 확인합니다. API 키와 인터넷 연결을 확인해주세요.");
        }
        finally { if (entered) _calendarGate.Release(); _calendarBusy = false; }
        if (_currentPage == "calendar" && CalendarPanel.ShowingSunday) await LoadSelectedSundayAsync(CalendarPanel.SelectedDate, force);
    }

    private bool IsPublicHoliday(DateOnly date)
    {
        if (!_publicHolidays.TryGetValue(date.Year, out var holidays))
        {
            holidays = KoreanPublicHolidays.ForYear(date.Year);
            if (_settings.PublicHolidayDates?.TryGetValue(date.Year, out var official) == true) holidays.UnionWith(official);
            _publicHolidays[date.Year] = holidays;
        }
        return holidays.Contains(date);
    }

    private void UpdateSundayReminderSchedule()
    {
        if (!_reminderSettingsReady) return;
        var enabled = _settings.RemindersEnabled && _settings.SundayNotifications;
        if (_calendarTimer is not null) _calendarTimer.Interval = enabled ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
        if (!enabled) { SundayReminderNext.Text = "알림 꺼짐"; return; }
        var now = DateTimeOffset.UtcNow;
        var window = SundayAnnouncements.Upcoming(now, IsPublicHoliday);
        if (now >= window.Due && (!window.IsDue(now) || _settings.CheckedReminders.ContainsKey($"sunday:{window.Sunday:yyyy-MM-dd}")))
            window = SundayAnnouncements.Window(window.Sunday.AddDays(7), IsPublicHoliday);
        SundayReminderNext.Text = $"공개 확인 예정: {window.Due:MM-dd (ddd) HH:mm} · 한국 시간";
    }

    private void SundayNotificationsToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (!_reminderSettingsReady || _dataDeleting) return;
        var previous = _settings.SundayNotifications;
        _settings.SundayNotifications = SundayNotificationsToggle.IsOn;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            _settings.SundayNotifications = previous; _reminderSettingsReady = false;
            SundayNotificationsToggle.IsOn = previous; _reminderSettingsReady = true;
            ShowReminderMessage("썬데이 알림 설정을 저장하지 못했어요.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }
        UpdateSundayReminderSchedule();
        if (_settings.SundayNotifications) _ = RefreshPublicHolidaysAsync();
    }

    private async Task RefreshPublicHolidaysAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (_closed || _dataDeleting || _holidaysChecking || !_settings.RemindersEnabled || !_settings.SundayNotifications
            || _holidayRetryAfter is { } retry && now < retry) return;
        var today = ScheduleCalendar.KoreanDate(now);
        if (_settings.PublicHolidaysCheckedAt is { } last && ScheduleCalendar.KoreanDate(last) == today) return;
        _holidaysChecking = true;
        try
        {
            var years = new[] { today.Year, today.AddDays(14).Year }.Distinct();
            var loaded = new Dictionary<int, List<DateOnly>>();
            foreach (var year in years)
            {
                // No character data or API key is sent to the public calendar.
                var html = await _calendarHttp.GetStringAsync($"https://astro.kasi.re.kr/kor/life/post/calendarData?search_year={year}", _calendarLifetime.Token);
                loaded[year] = KoreanPublicHolidays.ParseAnnualCalendar(html, year).Order().ToList();
            }
            if (_closed || _dataDeleting) return;
            var previous = _settings.PublicHolidayDates; var previousTime = _settings.PublicHolidaysCheckedAt;
            _settings.PublicHolidayDates = loaded; _settings.PublicHolidaysCheckedAt = now;
            try { _settings.Save(); }
            catch { _settings.PublicHolidayDates = previous; _settings.PublicHolidaysCheckedAt = previousTime; throw; }
            _publicHolidays.Clear(); UpdateSundayReminderSchedule();
        }
        catch (OperationCanceledException) when (_calendarLifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or ArgumentException or InvalidDataException || IsStorageError(error))
        { _holidayRetryAfter = DateTimeOffset.UtcNow.AddMinutes(5); }
        finally { _holidaysChecking = false; }
    }

    private async Task CheckSundayAnnouncementAsync()
    {
        if (_closed || _dataDeleting || _sundayChecking || _holidaysChecking || string.IsNullOrWhiteSpace(_apiKey)) return;
        var now = DateTimeOffset.UtcNow;
        var window = SundayAnnouncements.Upcoming(now, IsPublicHoliday);
        if (_sundayAttemptDate != window.Sunday) { _sundayAttemptDate = window.Sunday; _sundayRetryAfter = null; }
        var checkpoint = $"sunday:{window.Sunday:yyyy-MM-dd}";
        if (!SundayAnnouncements.ShouldAttempt(window, now, _sundayRetryAfter,
            _settings.RemindersEnabled && _settings.SundayNotifications, _settings.CheckedReminders.ContainsKey(checkpoint))) return;
        _sundayChecking = true;
        var key = _apiKey;
        try
        {
            if (_calendarBusy) return;
            var started = DateTimeOffset.UtcNow;
            await RefreshCalendarAsync(force: true);
            if (_closed || _dataDeleting || key != _apiKey || !_settings.RemindersEnabled || !_settings.SundayNotifications
                || !window.IsDue(DateTimeOffset.UtcNow) || _settings.EventNoticesCheckedAt is not { } checkedAt || checkedAt < started) return;
            var notice = SundayAnnouncements.Published(_settings.SundayNotices, window.Sunday);
            if (notice is not null) AddAnnouncementNotice("썬데이 메이플", $"{window.Sunday:MM월 dd일} 썬데이 메이플 혜택이 공개됐어요. 캘린더 또는 공식 공지에서 확인해주세요.", notice.Url, checkpoint);
        }
        finally
        {
            _sundayRetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            _sundayChecking = false; UpdateSundayReminderSchedule();
        }
    }

    private async Task LoadSelectedSundayAsync(DateOnly date, bool force = false)
    {
        if (_closed || _dataDeleting || date.DayOfWeek != DayOfWeek.Sunday) return;
        var today = ScheduleCalendar.KoreanDate(DateTimeOffset.UtcNow);
        if (date < today.AddMonths(-6)) { CalendarPanel.SetNoticeStatus("종료된 썬데이는 최근 6개월까지만 조회할 수 있어요."); return; }
        _calendarSelections++;
        var entered = false;
        try
        {
            await _calendarGate.WaitAsync(_calendarLifetime.Token); entered = true;
            if (_closed || _dataDeleting || CalendarPanel.SelectedDate != date || !CalendarPanel.ShowingSunday) return;
            var notice = (_settings.SundayNotices ?? []).Where(item => date >= item.Start && date <= item.End)
                .OrderByDescending(item => item.NoticeId).FirstOrDefault();
            if (notice is null || notice.Images.Length == 0 || force && date < today)
            {
                if (!SundayArchive.CanFetch(date, today)) return;
                if (force) _sundayArchive.ClearIndex();
                CalendarPanel.SetNoticeStatus($"{date:yyyy.MM.dd} 종료된 썬데이 공지를 찾는 중…");
                notice = await _sundayArchive.FindAsync(date, today, _calendarLifetime.Token);
                if (_closed || _dataDeleting) return;
                if (notice is null)
                {
                    if (CalendarPanel.SelectedDate == date) CalendarPanel.SetNoticeStatus("해당 날짜의 썬데이 공지를 찾지 못했어요. 공지 새로고침으로 다시 확인할 수 있습니다.");
                    return;
                }
                var previous = _settings.SundayNotices ?? [];
                _settings.SundayNotices = previous.Where(item => item.NoticeId != notice.NoticeId && item.End >= today.AddMonths(-6)).Append(notice).OrderBy(item => item.Start).ToList();
                try { _settings.Save(); } catch { _settings.SundayNotices = previous; throw; }
            }
            var complete = true;
            var sources = new List<ImageSource>();
            foreach (var image in notice.Images)
            {
                try
                {
                    var bytes = await _sundayImages.LoadAsync(image, _calendarLifetime.Token);
                    if (bytes is null) { complete = false; continue; }
                    var bitmap = new BitmapImage { DecodePixelWidth = 640 };
                    using var memory = new MemoryStream(bytes, writable: false);
                    using var stream = memory.AsRandomAccessStream();
                    await bitmap.SetSourceAsync(stream);
                    sources.Add(bitmap);
                }
                catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException) { complete = false; }
            }
            if (_closed || _dataDeleting || CalendarPanel.SelectedDate != date || !CalendarPanel.ShowingSunday) return;
            CalendarPanel.SetContext(_settings, SchedulerAvatars);
            CalendarPanel.SetSundayImages(date, sources);
            CalendarPanel.SetNoticeStatus(complete && notice.Images.Length > 0
                ? $"{date:yyyy.MM.dd} 썬데이 혜택"
                : "혜택 이미지를 확인하지 못했어요. 공지 새로고침으로 다시 시도하거나 공식 공지를 열어주세요.");
        }
        catch (OperationCanceledException) when (_calendarLifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
        { if (!_closed && !_dataDeleting && CalendarPanel.SelectedDate == date) CalendarPanel.SetNoticeStatus("썬데이를 불러오지 못했어요. 저장된 공지는 유지하며 공지 새로고침으로 다시 시도할 수 있습니다."); }
        finally { if (entered) _calendarGate.Release(); _calendarSelections--; }
    }
}
