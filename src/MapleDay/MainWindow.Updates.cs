using System.Diagnostics;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace MapleDay;

public sealed partial class MainWindow
{
    private readonly AppUpdateClient _updates = new();
    private readonly IncrementalUpdateClient _incrementalUpdates = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private CancellationTokenSource? _updateRequest;
    private bool _updatesInitialized, _updateChecking, _updateInstalling;
    private bool _updateHistoryDialogOpen;
    private string _localUpdatePreview = "";
    private AppUpdate? _readyUpdate;
    private PreparedUpdate? _preparedUpdate;
    private TaskbarUpdateBadge? _taskbarUpdateBadge;
    private bool IsStoreInstall => WindowsStartup.IsPackaged && Windows.ApplicationModel.Package.Current.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.Store;
    private Version CurrentAppVersion
    {
        get
        {
            if (!WindowsStartup.IsPackaged) return typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return new(version.Major, version.Minor, version.Build, version.Revision);
        }
    }

    private void InitializeUpdates()
    {
        _taskbarUpdateBadge = new TaskbarUpdateBadge(WinRT.Interop.WindowNative.GetWindowHandle(this));
        AppVersionText.Text = $"현재 버전 {CurrentAppVersion.ToString(4)}";
        AutomaticUpdatesToggle.IsOn = _settings.AutomaticUpdates;
        UpdateNotificationsToggle.IsOn = _settings.UpdateNotifications;
        AutomaticUpdatesToggle.IsEnabled = !WindowsStartup.IsPackaged;
        if (WindowsStartup.IsPackaged)
        {
            AutomaticUpdatesToggle.Visibility = Visibility.Collapsed;
            UpdateNotificationsHint.Text = "새 버전이 있으면 Windows·앱 알림과 ①을 표시합니다. MSIX 업데이트는 스토어 또는 새 MSIX 다운로드 페이지에서 진행합니다. 기본은 꺼짐입니다.";
            CheckUpdateButton.Content = "업데이트 확인";
            InstallUpdateButton.Content = IsStoreInstall ? "Microsoft Store 열기" : "새 MSIX 다운로드 페이지";
            UpdateStatusText.Text = IsStoreInstall ? "Microsoft Store에서 자동 업데이트를 관리합니다."
                : "직접 설치한 MSIX는 릴리즈에서 새 MSIX를 받아 설치해주세요. 저장된 데이터는 유지됩니다.";
        }
        else UpdateStatusText.Text = "실행 시와 6시간마다 새 버전을 확인하고 변경된 파일만 내려받습니다. 설치기 없이 적용 후 다시 시작합니다.";
        LoadLocalUpdatePreview();
        _updateTimer.Tick += (_, _) => StartAutomaticUpdates();
        Closed += (_, _) => { StopUpdates(); _taskbarUpdateBadge?.Dispose(); };
        _updatesInitialized = true;
    }

    private void StartAutomaticUpdates()
    {
        if ((WindowsStartup.IsPackaged ? !_settings.UpdateNotifications : !_settings.AutomaticUpdates)
            || _closed || _dataDeleting || _updateInstalling) return;
        _updateTimer.Start();
        _ = CheckForUpdatesAsync();
    }

    private void StopUpdates()
    {
        _updateTimer.Stop();
        _updateRequest?.Cancel();
    }

    private void AutomaticUpdatesToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_updatesInitialized || _dataDeleting || _updateInstalling) return;
        var previous = _settings.AutomaticUpdates;
        _settings.AutomaticUpdates = AutomaticUpdatesToggle.IsOn;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            _settings.AutomaticUpdates = previous;
            _updatesInitialized = false;
            AutomaticUpdatesToggle.IsOn = previous;
            _updatesInitialized = true;
            UpdateStatusText.Text = "자동 업데이트 설정을 저장하지 못했어요.";
            return;
        }
        if (_settings.AutomaticUpdates) StartAutomaticUpdates();
        else { StopUpdates(); UpdateStatusText.Text = "자동 확인을 껐습니다. 업데이트 확인 버튼으로 직접 확인할 수 있습니다."; }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs args)
        => await CheckForUpdatesAsync();

    private void UpdateNotificationsToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_updatesInitialized || _dataDeleting || _updateInstalling) return;
        var previous = _settings.UpdateNotifications;
        _settings.UpdateNotifications = UpdateNotificationsToggle.IsOn;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            _settings.UpdateNotifications = previous;
            _updatesInitialized = false;
            UpdateNotificationsToggle.IsOn = previous;
            _updatesInitialized = true;
            ShowReminderMessage("앱 업데이트 알림 설정을 저장하지 못했어요.", InfoBarSeverity.Warning);
            return;
        }
        RefreshUpdateBadge();
        NotifyPreparedUpdate();
        if (WindowsStartup.IsPackaged)
        {
            if (_settings.UpdateNotifications) StartAutomaticUpdates();
            else StopUpdates();
        }
    }

    private void RefreshUpdateBadge()
    {
        var visible = _readyUpdate is not null && (_preparedUpdate is not null || WindowsStartup.IsPackaged) && _settings.UpdateNotifications
            && _settings.RemindersEnabled
            && !_closed && !_dataDeleting && !_updateInstalling;
        SettingsUpdateBadge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(SettingsNavigationItem, visible ? "설정, 새 버전이 있습니다. 업데이트를 확인할 수 있습니다." : "설정");
        ToolTipService.SetToolTip(SettingsNavigationItem, visible ? "① 새 버전 있음 · 설정에서 업데이트" : "설정");
        _tray?.SetUpdateAvailable(visible);
        _taskbarUpdateBadge?.SetVisible(visible);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateChecking || _updateInstalling || _closed || _dataDeleting) return;
        _updateChecking = true;
        var request = new CancellationTokenSource();
        _updateRequest = request;
        CheckUpdateButton.IsEnabled = InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "GitHub에서 새 버전 확인 중…";
        try
        {
            var update = await _updates.CheckAsync(CurrentAppVersion, request.Token);
            if (_closed || _dataDeleting) return;
            _readyUpdate = null;
            if (_preparedUpdate is { } previous) { Directory.Delete(previous.Directory, true); _preparedUpdate = null; }
            InstallUpdateButton.Visibility = Visibility.Collapsed;
            UpdateDetailsExpander.Visibility = Visibility.Collapsed;
            RefreshUpdateBadge();
            if (update is null)
            {
                UpdateStatusText.Text = "최신 버전을 사용 중입니다.";
                ShowLocalUpdatePreview();
                return;
            }
            ShowUpdateDetails(update.Version, update.ReleaseNotes);
            if (WindowsStartup.IsPackaged)
            {
                _readyUpdate = update;
                InstallUpdateButton.Visibility = Visibility.Visible;
                UpdateStatusText.Text = $"{update.Version} 새 버전이 있어요. 변경사항을 확인한 뒤 업데이트할 수 있습니다.";
                RefreshUpdateBadge(); NotifyPreparedUpdate();
                return;
            }
            UpdateDownloadProgress.Value = 0;
            UpdateDownloadProgress.Visibility = Visibility.Visible;
            UpdateStatusText.Text = $"{update.Version} 다운로드 중…";
            var progress = new Progress<double>(value =>
            {
                if (!_closed && !_dataDeleting && !request.IsCancellationRequested) UpdateDownloadProgress.Value = value;
            });
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "updates");
            var launcher = WindowsStartup.FindLauncher(AppContext.BaseDirectory)
                ?? throw new InvalidDataException("배포 폴더의 MapleDay.exe로 실행한 뒤 업데이트해주세요.");
            var manifestFile = await _updates.DownloadAsync(update, directory, null, request.Token);
            if (new FileInfo(manifestFile).Length > 4 * 1024 * 1024) throw new InvalidDataException("업데이트 파일 목록이 너무 큽니다.");
            var manifest = IncrementalUpdatePolicy.Parse(await File.ReadAllTextAsync(manifestFile, request.Token), update);
            UpdateStatusText.Text = $"{update.Version} 변경된 파일 확인 및 다운로드 중…";
            var prepared = await _incrementalUpdates.PrepareAsync(manifest, Path.GetDirectoryName(launcher)!, directory, progress, request.Token);
            if (_closed || _dataDeleting) return;
            _readyUpdate = update; _preparedUpdate = prepared;
            InstallUpdateButton.Visibility = Visibility.Visible;
            UpdateStatusText.Text = $"{update.Version} 준비 완료 · 변경 {prepared.ChangedFiles}개 · 다운로드 {prepared.DownloadSize / 1024.0 / 1024.0:0.0}MB. 설치기 없이 적용하고 다시 시작합니다.";
            RefreshUpdateBadge();
            NotifyPreparedUpdate();
        }
        catch (OperationCanceledException)
        { if (!_closed && !_dataDeleting) UpdateStatusText.Text = request.IsCancellationRequested ? "업데이트 다운로드를 중단했습니다." : "연결 시간이 초과됐어요. 업데이트 확인을 다시 눌러주세요."; }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { if (!_closed && !_dataDeleting) UpdateStatusText.Text = error is InvalidDataException ? error.Message : "업데이트를 확인하거나 내려받지 못했어요. 잠시 후 다시 시도해주세요."; }
        finally
        {
            _updateRequest = null;
            request.Dispose();
            _updateChecking = false;
            if (!_closed)
            {
                CheckUpdateButton.IsEnabled = InstallUpdateButton.IsEnabled = true;
                UpdateDownloadProgress.Visibility = Visibility.Collapsed;
                RefreshUpdateBadge();
            }
        }
    }

    private void LoadLocalUpdatePreview()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "release-notes-preview.txt");
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 64 * 1024) return;
            _localUpdatePreview = AppUpdatePolicy.NormalizeReleaseNotes(File.ReadAllText(file));
            ShowLocalUpdatePreview();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _localUpdatePreview = "";
        }
    }

    private void ShowLocalUpdatePreview()
    {
        if (string.IsNullOrWhiteSpace(_localUpdatePreview)) return;
        ShowUpdateDetails(null, _localUpdatePreview, preview: true);
    }

    private void ShowUpdateDetails(Version? version, string notes, bool preview = false)
    {
        UpdateDetailsExpander.Header = preview ? "다음 업데이트 내용 미리보기" : $"{version?.ToString(4)} 업데이트 내용";
        UpdateDetailsHint.Text = preview ? "검토용 빌드에만 표시되는 초안입니다. 아직 사용자에게 배포되지 않았습니다." : "설치하기 전에 변경사항을 확인하세요.";
        UpdateDetailsHint.Visibility = Visibility.Visible;
        UpdateDetailsText.Text = string.IsNullOrWhiteSpace(notes)
            ? "이번 버전의 상세 변경사항이 등록되지 않았습니다."
            : notes;
        UpdateDetailsExpander.Visibility = Visibility.Visible;
        UpdateDetailsExpander.IsExpanded = true;
    }

    private async void AllUpdateHistoryButton_Click(object sender, RoutedEventArgs args)
    {
        if (_updateHistoryDialogOpen || _closed || _dataDeleting) return;
        _updateHistoryDialogOpen = true;
        try
        {
            var page = 0;
            var entries = new StackPanel { Spacing = 0 };
            var previous = new Button { Content = "이전", MinWidth = 84 };
            var next = new Button { Content = "다음", MinWidth = 84 };
            var pageText = new TextBlock { MinWidth = 80, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
            AutomationProperties.SetName(previous, "이전 업데이트 내역 페이지");
            AutomationProperties.SetName(next, "다음 업데이트 내역 페이지");
            pager.Children.Add(previous); pager.Children.Add(pageText); pager.Children.Add(next);

            void RenderPage()
            {
                entries.Children.Clear();
                foreach (var entry in UpdateHistoryCatalog.Page(page))
                {
                    var item = new StackPanel { Spacing = 8, Padding = new Thickness(0, 16, 0, 16) };
                    item.Children.Add(new TextBlock
                    {
                        Text = $"{entry.Version.ToString(4)} - {entry.Date:yyyy.MM.dd}",
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    });
                    item.Children.Add(new TextBlock
                    {
                        Text = entry.Notes,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                        LineHeight = 22
                    });
                    entries.Children.Add(new Border
                    {
                        BorderBrush = AppTheme.Brush("LineBrush"),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Child = item
                    });
                }
                previous.IsEnabled = page > 0;
                next.IsEnabled = page + 1 < UpdateHistoryCatalog.PageCount;
                pageText.Text = $"{page + 1} / {UpdateHistoryCatalog.PageCount}";
                AutomationProperties.SetName(pageText, $"{UpdateHistoryCatalog.PageCount}페이지 중 {page + 1}페이지");
            }

            previous.Click += (_, _) => { if (page > 0) { page--; RenderPage(); } };
            next.Click += (_, _) => { if (page + 1 < UpdateHistoryCatalog.PageCount) { page++; RenderPage(); } };
            RenderPage();
            var content = new Grid { Width = Math.Max(320, Math.Min(680, Root.ActualWidth - 96)) };
            content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var scroller = new ScrollViewer
            {
                MaxHeight = Math.Max(320, Math.Min(600, Root.ActualHeight - 220)),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = entries
            };
            Grid.SetRow(scroller, 0); Grid.SetRow(pager, 1);
            content.Children.Add(scroller); content.Children.Add(pager);
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
                Title = "모든 업데이트 내역",
                Content = content,
                CloseButtonText = "닫기"
            };
            await dialog.ShowAsync();
        }
        finally { _updateHistoryDialogOpen = false; }
    }

    private void NotifyPreparedUpdate()
    {
        if (_readyUpdate is null || _preparedUpdate is null && !WindowsStartup.IsPackaged || !_settings.UpdateNotifications || !_settings.RemindersEnabled
            || _closed || _dataDeleting || _updateInstalling) return;
        var version = _readyUpdate.Version.ToString();
        if (_settings.NotifiedUpdateVersion == version) return;
        var previous = _settings.NotifiedUpdateVersion;
        _settings.NotifiedUpdateVersion = version;
        var message = WindowsStartup.IsPackaged ? $"{version} 새 버전이 있어요. 설정에서 변경사항을 확인하고 업데이트할 수 있습니다."
            : $"{version} 버전이 준비됐어요. 설정에서 변경사항을 확인하고 업데이트할 수 있습니다.";
        if (!AddAnnouncementNotice("앱 업데이트", message, "mapleday:update"))
            _settings.NotifiedUpdateVersion = previous;
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs args)
    {
        if (WindowsStartup.IsPackaged)
        {
            if (_readyUpdate is null || _updateChecking || _closed || _dataDeleting) return;
            try
            {
                var target = IsStoreInstall ? new Uri("ms-windows-store://downloadsandupdates")
                    : _readyUpdate.ReleasePage ?? new Uri("https://github.com/MapleYoil/MapleDay/releases/latest");
                await Launcher.LaunchUriAsync(target);
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
            { UpdateStatusText.Text = "업데이트 페이지를 열지 못했어요. 잠시 후 다시 시도해주세요."; }
            return;
        }
        if (_readyUpdate is null || _preparedUpdate is not { } prepared || _updateChecking || _updateInstalling || _closed || _dataDeleting) return;
        if (_supportSubmitting || _supportReplySending)
        { UpdateStatusText.Text = "문의·답장 전송이 끝난 뒤 업데이트해주세요."; return; }
        var launcher = WindowsStartup.FindLauncher(AppContext.BaseDirectory);
        if (launcher is null) { UpdateStatusText.Text = "배포 폴더의 MapleDay.exe로 실행한 뒤 업데이트해주세요."; return; }
        _updateInstalling = true;
        Root.IsHitTestVisible = false;
        InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = AutomaticUpdatesToggle.IsEnabled = UpdateNotificationsToggle.IsEnabled = false;
        try
        {
            if (_closed || _dataDeleting) return;
            var status = Path.Combine(prepared.Directory, "status.txt");
            if (File.Exists(status)) File.Delete(status);
            var start = new ProcessStartInfo(Path.Combine(prepared.Directory, "worker.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = prepared.Directory };
            foreach (var argument in new[] { "--apply-update", prepared.Directory, Environment.ProcessId.ToString() })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("업데이트를 시작하지 못했어요.");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            string? ready = null;
            while (ready is null && !process.HasExited && DateTime.UtcNow < deadline)
            {
                try { if (File.Exists(status)) { var value = await File.ReadAllTextAsync(status); if (value is "ready" or "error") ready = value; } }
                catch (IOException) { /* Native writer is finishing the flushed status file. */ }
                if (ready is null) await Task.Delay(50);
            }
            if (ready != "ready")
                throw new InvalidDataException("업데이트 적용을 준비하지 못했어요. 기존 앱은 그대로 유지됩니다.");
            RefreshUpdateBadge();
            UpdateStatusText.Text = "변경된 파일을 적용하고 다시 시작합니다…";
            Root.IsHitTestVisible = false;
            StopUpdates();
            StopTelemetry();
            await StopWebHostAsync();
            await IncomeReplayPanel.StopAsync();
            _replyTimer.Stop(); _reminderTimer.Stop();
            _reminderLifetime.Cancel(); _loadCts?.Cancel();
            CancelScheduler(); CancelLevels(); _supportLifetime.Cancel();
            while (_characterActiveLoads > 0 || _schedulerActiveLoads > 0 || _levelActiveLoads > 0 || _profileWrites > 0 || _supportRefreshing || _reminderChecking || _windowsStartupApplying || _telemetryBusy)
                await Task.Delay(50);
            ExitApp();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            UpdateStatusText.Text = error is InvalidDataException ? error.Message : "업데이트를 시작하지 못했어요. 다시 시도해주세요.";
            _updateInstalling = false;
            RefreshUpdateBadge();
            Root.IsHitTestVisible = true;
            InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = AutomaticUpdatesToggle.IsEnabled = UpdateNotificationsToggle.IsEnabled = true;
        }
    }
}
