using System.Collections.ObjectModel;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace MapleDay;

public sealed partial class MainWindow
{
    public ObservableCollection<SupportTicket> Tickets { get; } = [];
    private readonly SupportClient _support = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly CancellationTokenSource _supportLifetime = new();
    private readonly DispatcherTimer _replyTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private TrayIcon? _tray;
    private bool _supportRefreshing, _supportSubmitting, _exitRequested, _settingsInitialized;
    private readonly WindowsNotifications _windowsNotifications = new();
    private bool _notificationReady => _windowsNotifications.Ready;
    private string? _notificationTicket;
    private string? _notificationLandingPage;
    private string? _expandedSupportTicketId;
    private bool _supportRefreshAgain, _supportReplySending;
    private string? _supportReplyRequest, _supportReplyBody, _supportReplyTicket, _supportReplyRequestTicket;

    private void InitializeSupport()
    {
        _tray = new TrayIcon(RestoreWindow, ExitApp, () => { RestoreWindow(); NavigateTo("settings"); });
        _settings.ReadTickets ??= [];
        StartPageSelector.SelectedItem = StartPageSelector.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == _settings.ValidStartPage);
        CloseBehavior.SelectedIndex = _settings.CloseToTray ? 0 : 1;
        _settingsInitialized = true;
        AppWindow.Closing += (_, args) =>
        {
            if (!_exitRequested && _settings.CloseToTray && _tray.Available)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };
        _windowsNotifications.Initialize(SupportNotificationInvoked);
        _replyTimer.Tick += async (_, _) => await RefreshSupportAsync(showErrors: false);
        _ = _support.WatchAsync(() => DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _dataDeleting) return;
            if (_supportRefreshing) _supportRefreshAgain = true;
            else _ = RefreshSupportAsync(showErrors: false);
        }), _supportLifetime.Token);
        Closed += (_, _) =>
        {
            _replyTimer.Stop();
            _supportLifetime.Cancel();
            _tray.Dispose();
            _windowsNotifications.Dispose();
        };
    }

    private async Task InitializeSupportForLaunchAsync()
    {
        UpdateSupportLogin();
        try
        {
            var draft = await _support.PendingDraftAsync();
            if (draft is not null && !_closed && !_dataDeleting)
            {
                SupportSubject.Text = draft.Subject;
                SupportBody.Text = draft.Body;
                SupportKind.SelectedIndex = draft.Kind == "suggestion" ? 1 : 0;
            }
        }
        catch (Exception error) when (IsSupportError(error)) { }
        await RefreshSupportAsync(showErrors: false);
    }

    private void RestoreWindow()
    {
        AppWindow.Show();
        Activate();
        if (_currentPage == "notifications") RefreshReminderList();
        else if (_currentPage == "support" && _expandedSupportTicketId is not null)
            ShowSupportTicket(Tickets.FirstOrDefault(ticket => ticket.Id == _expandedSupportTicketId));
    }

    private void ExitApp()
    {
        _exitRequested = true;
        Close();
    }

    private void CloseBehavior_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsInitialized) return;
        _settings.CloseToTray = CloseBehavior.SelectedIndex == 0;
        try { _settings.Save(); }
        catch (Exception error) when (IsSupportError(error)) { }
    }

    private void StartPageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsInitialized || StartPageSelector.SelectedItem is not ComboBoxItem { Tag: string page }) return;
        _settings.StartPage = page;
        try { _settings.Save(); }
        catch (Exception error) when (IsSupportError(error)) { }
    }

    private void UpdateSupportLogin()
    {
        var connected = _hasLoadedCharacters && Characters.Count > 0;
        SupportLoginPrompt.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        SupportForm.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        SupportSendButton.IsEnabled = connected && !_supportSubmitting;
    }

    private async void SupportSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dataDeleting || _supportSubmitting || !_hasLoadedCharacters || SupportCharacter.SelectedItem is not CharacterCard character) return;
        var subject = SupportSubject.Text.Trim();
        var body = SupportBody.Text.Trim();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            ShowSupportMessage("제목과 내용을 입력해주세요.", InfoBarSeverity.Warning);
            return;
        }
        _supportSubmitting = true;
        SetSupportFormEnabled(false);
        SupportSending.Visibility = Visibility.Visible;
        try
        {
            var kind = ((ComboBoxItem)SupportKind.SelectedItem).Tag.ToString()!;
            await _support.SubmitAsync(kind, character.Name, subject, body, _supportLifetime.Token);
            if (_closed) return;
            SupportSubject.Text = SupportBody.Text = "";
            ShowSupportMessage(kind == "suggestion" ? "건의사항이 접수됐어요. 완료·반려 결과는 이곳과 Windows 알림에서 확인할 수 있어요." : "문의가 접수됐어요. 답변이 도착하면 이곳과 Windows 알림에서 확인할 수 있어요.", InfoBarSeverity.Success);
            await RefreshSupportAsync(showErrors: false);
        }
        catch (Exception error) when (IsSupportError(error))
        {
            if (!_closed) ShowSupportMessage(error is System.Net.Http.HttpRequestException ? error.Message : "접수 결과를 확인하지 못했어요. 같은 내용으로 다시 보내면 중복 접수를 방지합니다.", InfoBarSeverity.Error);
            ReportDiagnostic(error, "support");
        }
        finally
        {
            _supportSubmitting = false;
            if (!_closed)
            {
                SetSupportFormEnabled(true);
                SupportSending.Visibility = Visibility.Collapsed;
                UpdateSupportLogin();
            }
        }
    }

    private async void SupportRefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshSupportAsync(showErrors: true);

    private void SetSupportFormEnabled(bool enabled)
    {
        SupportKind.IsEnabled = SupportCharacter.IsEnabled = SupportSubject.IsEnabled = SupportBody.IsEnabled = SupportSendButton.IsEnabled = enabled;
    }

    private async Task RefreshSupportAsync(bool showErrors)
    {
        if (_supportRefreshing || _closed || _dataDeleting) return;
        _supportRefreshing = true;
        SupportRefreshButton.IsEnabled = false;
        try
        {
            var inbox = await _support.InboxAsync(_supportLifetime.Token);
            if (_closed || _dataDeleting) return;
            var selectedId = _notificationTicket ?? _expandedSupportTicketId;
            Tickets.Clear();
            foreach (var ticket in inbox.Tickets)
            {
                ticket.RestoreReadState(_settings.ReadTickets.Contains(ticket.Id) || _settings.ReadReplies.ContainsKey(ticket.Id), _settings.ReadReplies.GetValueOrDefault(ticket.Id));
                Tickets.Add(ticket);
            }
            if (Tickets.Count == 0) _replyTimer.Stop();
            else _replyTimer.Start();
            SupportEmpty.Visibility = Tickets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowSupportTicket(Tickets.FirstOrDefault(ticket => ticket.Id == selectedId));
            var replies = Tickets.SelectMany(ticket => ticket.Replies.Where(reply => reply.IsAdmin).Select(reply => (Ticket: ticket, Reply: reply)))
                .Where(item => item.Reply.Id > _settings.NotifiedReplyId).OrderBy(item => item.Reply.Id).ToArray();
            UpdateSupportBadge();
            if (_notificationReady && replies.Length > 0)
            {
                foreach (var item in replies.GroupBy(item => item.Ticket.Id).Select(group => group.Last()))
                {
                    if (!_windowsNotifications.Show(item.Ticket.NotificationTitle,
                        [item.Ticket.Subject, item.Reply.Body.Length > 100 ? item.Reply.Body[..100] + "…" : item.Reply.Body],
                        new Dictionary<string, string> { ["ticket"] = item.Ticket.Id }))
                        throw new InvalidOperationException(_windowsNotifications.Status);
                }
                _settings.NotifiedReplyId = replies.Max(item => item.Reply.Id);
                _settings.Save();
            }
            _notificationTicket = null;
        }
        catch (Exception error) when (IsSupportError(error))
        {
            if (showErrors && !_closed) ShowSupportMessage("답변을 불러오지 못했어요. 잠시 후 새로고침해주세요.", InfoBarSeverity.Warning);
            ReportDiagnostic(error, "support");
        }
        finally
        {
            _supportRefreshing = false;
            if (!_closed) SupportRefreshButton.IsEnabled = true;
            if (_supportRefreshAgain && !_closed && !_dataDeleting) { _supportRefreshAgain = false; _ = RefreshSupportAsync(showErrors: false); }
        }
    }

    private void SupportTickets_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SupportTicket ticket)
            ShowSupportTicket(_expandedSupportTicketId == ticket.Id ? null : ticket);
    }

    private void ShowSupportTicket(SupportTicket? ticket)
    {
        _expandedSupportTicketId = ticket?.Id;
        SupportReplyForm.Visibility = ticket?.Kind == "inquiry" ? Visibility.Visible : Visibility.Collapsed;
        if (_supportReplyTicket != ticket?.Id && !_supportReplySending) { SupportReplyBody.Text = ""; SupportReplyStatus.Text = ""; }
        _supportReplyTicket = ticket?.Id;
        if (ticket is not null)
        {
            SupportConversationPanel.Visibility = Visibility.Visible;
            SupportConversationTitle.Text = ticket.Subject;
            SupportConversation.Text = ticket.Conversation;
            if (AppWindow.IsVisible && _currentPage == "support")
            {
                _settings.ReadTickets.Add(ticket.Id);
                _settings.ReadReplies[ticket.Id] = ticket.Replies.Count > 0 ? ticket.Replies.Max(reply => reply.Id) : 0;
                ticket.RestoreReadState(true, _settings.ReadReplies[ticket.Id]);
                try { _settings.Save(); } catch (Exception error) when (IsSupportError(error)) { }
                UpdateSupportBadge();
            }
        }
        else SupportConversationPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateSupportBadge()
    {
        var unread = Tickets.Count(ticket => ticket.Replies.Any(reply => reply.IsAdmin && reply.Id > _settings.ReadReplies.GetValueOrDefault(ticket.Id)));
        SupportBadge.Value = unread;
        SupportBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SupportReplySend_Click(object sender, RoutedEventArgs args)
    {
        if (_supportReplySending || _closed || _dataDeleting || _expandedSupportTicketId is not { } identifier) return;
        var body = SupportReplyBody.Text.Trim();
        if (body.Length == 0) { SupportReplyStatus.Text = "답장 내용을 입력해주세요."; return; }
        if (_supportReplyBody != body || _supportReplyRequestTicket != identifier || _supportReplyRequest is null)
        { _supportReplyRequest = Guid.NewGuid().ToString("N"); _supportReplyBody = body; _supportReplyRequestTicket = identifier; }
        _supportReplySending = true; SupportReplySend.IsEnabled = SupportReplyBody.IsEnabled = false;
        try
        {
            await _support.ReplyAsync(identifier, _supportReplyRequest, body, _supportLifetime.Token);
            if (_closed || _dataDeleting) return;
            _supportReplyRequest = null;
            if (_expandedSupportTicketId == identifier)
            {
                SupportReplyBody.Text = "";
                SupportReplyStatus.Text = "답장을 보냈어요. 이 문의에서 계속 대화할 수 있어요.";
            }
            await RefreshSupportAsync(false);
        }
        catch (Exception error) when (IsSupportError(error)) { if (!_closed) SupportReplyStatus.Text = "답장을 보내지 못했어요. 다시 보내면 중복 답장을 방지합니다."; }
        finally { _supportReplySending = false; if (!_closed) SupportReplySend.IsEnabled = SupportReplyBody.IsEnabled = true; }
    }

    public void ShowFromActivation(AppActivationArguments? activation = null)
    {
        if (_closed) return;
        if (activation is not null && WindowsStartup.IsStartupLaunch(
            activation.Kind == ExtendedActivationKind.StartupTask,
            activation.Kind == ExtendedActivationKind.Launch && activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
                ? launch.Arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : [])) return;
        if (activation?.Data is AppNotificationActivatedEventArgs notification)
            SupportNotificationInvoked(notification.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        else RestoreWindow();
    }

    private void SupportNotificationInvoked(IReadOnlyDictionary<string, string> arguments)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _dataDeleting) return;
            RestoreWindow();
            if (arguments.ContainsKey("update"))
            {
                _notificationLandingPage = "settings";
                NavigateTo("settings");
                return;
            }
            if (arguments.ContainsKey("reminder"))
            {
                _notificationLandingPage = "notifications";
                NavigateTo("notifications");
                return;
            }
            if (arguments.TryGetValue("ticket", out var id)) _notificationTicket = id;
            _notificationLandingPage = "support";
            NavigateTo("support");
            _ = RefreshSupportAsync(showErrors: true);
        });
    }

    private void ShowSupportMessage(string message, InfoBarSeverity severity)
    {
        SupportMessage.Title = message;
        SupportMessage.Severity = severity;
        SupportMessage.IsOpen = true;
    }

    private static bool IsSupportError(Exception error) => error is System.Net.Http.HttpRequestException
        or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or OperationCanceledException
        or System.Runtime.InteropServices.COMException or InvalidOperationException;
}
