using System.Collections.ObjectModel;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay;

public sealed partial class MainWindow
{
    public ObservableCollection<AppAnnouncement> Announcements { get; } = [];
    private readonly HashSet<long> _announcementsNotifiedThisRun = [];
    private bool _announcementsRefreshing;

    private void InitializeAnnouncements()
    {
        _settings.ReadAnnouncements ??= [];
        UpdateAnnouncementBadge();
        _ = _support.WatchAnnouncementsAsync(() => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed && !_dataDeleting) _ = RefreshAnnouncementsAsync(showErrors: false, startup: false);
        }), _supportLifetime.Token);
    }

    private async Task RefreshAnnouncementsAsync(bool showErrors, bool startup)
    {
        if (_announcementsRefreshing || _closed || _dataDeleting) return;
        _announcementsRefreshing = true;
        AnnouncementLoading.IsActive = true;
        AnnouncementLoading.Visibility = Visibility.Visible;
        try
        {
            var version = typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
            var inbox = await _support.AnnouncementsAsync(version, _supportLifetime.Token);
            if (_closed || _dataDeleting) return;
            Announcements.Clear();
            foreach (var announcement in inbox.Announcements) Announcements.Add(announcement);
            AnnouncementEmpty.Visibility = Announcements.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateAnnouncementBadge();

            var unread = Announcements.Where(item => !_settings.ReadAnnouncements.Contains(item.Id)).ToArray();
            var pending = startup ? unread : unread.Where(item => !_announcementsNotifiedThisRun.Contains(item.Id)).ToArray();
            foreach (var item in unread) _announcementsNotifiedThisRun.Add(item.Id);
            if (_settings.AnnouncementNotifications && pending.Length > 0 && _notificationReady)
            {
                var latest = pending[0];
                var body = latest.Body.Length > 120 ? latest.Body[..120] + "…" : latest.Body;
                var suffix = unread.Length > 1 ? $"읽지 않은 공지 {unread.Length}개 · {body}" : body;
                _windowsNotifications.Show("메요일 · " + latest.Title, [suffix],
                    new Dictionary<string, string> { ["announcement"] = latest.Id.ToString() });
            }
        }
        catch (Exception error) when (IsSupportError(error))
        {
            if (showErrors && !_closed)
            {
                AnnouncementMessage.Title = "공지사항을 불러오지 못했어요. 잠시 후 다시 시도해주세요.";
                AnnouncementMessage.Severity = InfoBarSeverity.Warning;
                AnnouncementMessage.IsOpen = true;
            }
            ReportDiagnostic(error, "support");
        }
        finally
        {
            _announcementsRefreshing = false;
            if (!_closed)
            {
                AnnouncementLoading.IsActive = false;
                AnnouncementLoading.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void AnnouncementReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id } || _settings.ReadAnnouncements.Contains(id)) return;
        _settings.ReadAnnouncements.Add(id);
        try { _settings.Save(); }
        catch (Exception error) when (IsSupportError(error))
        {
            _settings.ReadAnnouncements.Remove(id);
            AnnouncementMessage.Title = "읽음 상태를 저장하지 못했어요.";
            AnnouncementMessage.Severity = InfoBarSeverity.Warning;
            AnnouncementMessage.IsOpen = true;
        }
        UpdateAnnouncementBadge();
    }

    private void UpdateAnnouncementBadge()
    {
        var unread = Announcements.Any(item => !_settings.ReadAnnouncements.Contains(item.Id));
        AnnouncementBadge.Visibility = unread ? Visibility.Visible : Visibility.Collapsed;
    }
}
