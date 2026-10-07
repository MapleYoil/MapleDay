using System.Diagnostics;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Windows.System;

namespace MapleDay;

public sealed partial class MainWindow
{
    private readonly AppUpdateClient _updates = new();
    private readonly IncrementalUpdateClient _incrementalUpdates = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private CancellationTokenSource? _updateRequest;
    private bool _updatesInitialized, _updateChecking, _updateInstalling;
    private AppUpdate? _readyUpdate;
    private PreparedUpdate? _preparedUpdate;
    private Version? _notifiedUpdateVersion;
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
        AppVersionText.Text = $"현재 버전 {CurrentAppVersion.ToString(4)}";
        AutomaticUpdatesToggle.IsOn = _settings.AutomaticUpdates;
        AutomaticUpdatesToggle.IsEnabled = !WindowsStartup.IsPackaged;
        if (WindowsStartup.IsPackaged)
        {
            AutomaticUpdatesToggle.Visibility = Visibility.Collapsed;
            CheckUpdateButton.Content = IsStoreInstall ? "Microsoft Store에서 업데이트 확인" : "새 MSIX 다운로드 페이지";
            UpdateStatusText.Text = IsStoreInstall ? "Microsoft Store에서 자동 업데이트를 관리합니다."
                : "직접 설치한 MSIX는 릴리즈에서 새 MSIX를 받아 설치해주세요. 저장된 데이터는 유지됩니다.";
        }
        else UpdateStatusText.Text = "실행 시와 6시간마다 새 버전을 확인하고 변경된 파일만 내려받습니다. 설치기 없이 적용 후 다시 시작합니다.";
        _updateTimer.Tick += (_, _) => StartAutomaticUpdates();
        Closed += (_, _) => StopUpdates();
        _updatesInitialized = true;
    }

    private void StartAutomaticUpdates()
    {
        if (!_settings.AutomaticUpdates || WindowsStartup.IsPackaged || _closed || _dataDeleting || _updateInstalling) return;
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
    {
        if (WindowsStartup.IsPackaged)
        {
            try { await Launcher.LaunchUriAsync(new Uri(IsStoreInstall ? "ms-windows-store://downloadsandupdates" : "https://github.com/MapleYoil/MapleDay/releases/latest")); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
            { UpdateStatusText.Text = "업데이트 페이지를 열지 못했어요. 설치한 경로에서 새 버전을 확인해주세요."; }
        }
        else await CheckForUpdatesAsync();
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
            InstallUpdateButton.Visibility = UpdateReadyButton.Visibility = Visibility.Collapsed;
            if (update is null) { UpdateStatusText.Text = "최신 버전을 사용 중입니다."; return; }
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
            InstallUpdateButton.Visibility = UpdateReadyButton.Visibility = Visibility.Visible;
            UpdateStatusText.Text = $"{update.Version} 준비 완료 · 변경 {prepared.ChangedFiles}개 · 다운로드 {prepared.DownloadSize / 1024.0 / 1024.0:0.0}MB. 설치기 없이 적용하고 다시 시작합니다.";
            if (_notifiedUpdateVersion != update.Version && _notificationReady
                && _windowsNotifications.Show("메요일 · 새 버전이 준비됐어요", [$"{update.Version} 업데이트를 내려받았습니다. 클릭해서 적용하세요."], new Dictionary<string, string> { ["update"] = "1" }))
                _notifiedUpdateVersion = update.Version;
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
            }
        }
    }

    private void UpdateReadyButton_Click(object sender, RoutedEventArgs args) => NavigateTo("settings");

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs args)
    {
        if (_readyUpdate is null || _preparedUpdate is not { } prepared || _updateChecking || _updateInstalling || _closed || _dataDeleting) return;
        if (_supportSubmitting || !string.IsNullOrWhiteSpace(SupportSubject.Text) || !string.IsNullOrWhiteSpace(SupportBody.Text))
        { UpdateStatusText.Text = "작성 중인 문의를 보내거나 내용을 비운 뒤 업데이트해주세요."; return; }
        var launcher = WindowsStartup.FindLauncher(AppContext.BaseDirectory);
        if (launcher is null) { UpdateStatusText.Text = "배포 폴더의 MapleDay.exe로 실행한 뒤 업데이트해주세요."; return; }
        _updateInstalling = true;
        Root.IsHitTestVisible = false;
        InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = AutomaticUpdatesToggle.IsEnabled = false;
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
            UpdateStatusText.Text = "변경된 파일을 적용하고 다시 시작합니다…";
            Root.IsHitTestVisible = false;
            StopUpdates();
            StopTelemetry();
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
            Root.IsHitTestVisible = true;
            InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = AutomaticUpdatesToggle.IsEnabled = true;
        }
    }
}
