using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MapleDay;

public sealed partial class MainWindow
{
    private readonly AppDataStore _appData = new();
    private bool _dataDeleting;
    private readonly WindowsStartup _windowsStartup = new();
    private bool _windowsStartupReady;
    private bool _windowsStartupApplying;
    private bool _windowSizeSettingsReady;
    private bool _windowSizeUpdating;
    private bool _privacyPolicyOpen;

    private async void PrivacyPolicyButton_Click(object sender, RoutedEventArgs args)
    {
        if (_privacyPolicyOpen || _dataDeleting || _closed) return;
        _privacyPolicyOpen = true;
        try
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("MapleDay.PrivacyPolicy.txt")!;
            using var reader = new StreamReader(stream);
            var policy = await reader.ReadToEndAsync();
            await new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "개인정보처리방침", CloseButtonText = "닫기",
                Content = new ScrollViewer
                {
                    MaxHeight = Math.Max(180, Math.Min(480, Root.ActualHeight - 200)),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock { Text = policy, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
                }
            }.ShowAsync();
        }
        finally { _privacyPolicyOpen = false; }
    }

    private async void InitializeWindowsStartup()
    {
        WindowsStartupToggle.IsOn = _settings.AutoStartWindows;
        WindowsStartupBehavior.SelectedIndex = _settings.WindowsStartupToTray ? 0 : 1;
        await ApplyWindowsStartupAsync();
        _windowsStartupReady = true;
    }

    private void WindowsStartupBehavior_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_windowsStartupReady || _windowsStartupApplying || _dataDeleting) return;
        _settings.WindowsStartupToTray = WindowsStartupBehavior.SelectedIndex == 0;
        try
        {
            _settings.Save();
            UpdateWindowsStartupStatus();
        }
        catch (Exception error) when (IsStorageError(error))
        { WindowsStartupStatus.Text = "자동 실행 방식을 저장하지 못했어요. 다시 선택해주세요."; }
    }

    private void UpdateWindowsStartupStatus()
    {
        WindowsStartupStatus.Text = !_settings.AutoStartWindows ? "Windows 시작 시 자동 실행하지 않습니다."
            : _settings.WindowsStartupToTray ? "Windows에 로그인하면 트레이에 최소화된 상태로 실행합니다."
            : "Windows에 로그인하면 메요일 창을 엽니다.";
    }

    private async void WindowsStartupToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_windowsStartupReady || _windowsStartupApplying || _dataDeleting) return;
        _settings.AutoStartWindows = WindowsStartupToggle.IsOn;
        if (!await ApplyWindowsStartupAsync() || _closed || _dataDeleting) return;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        { WindowsStartupStatus.Text = "시작 항목은 변경했지만 설정을 저장하지 못했어요."; }
    }

    private async Task<bool> ApplyWindowsStartupAsync()
    {
        _windowsStartupApplying = true;
        WindowsStartupToggle.IsEnabled = false;
        WindowsStartupBehavior.IsEnabled = false;
        try
        {
            if (WindowsStartup.IsPackaged)
            {
                var state = await WindowsStartup.SetPackagedAsync(_settings.AutoStartWindows);
                if (_closed || _dataDeleting) return false;
                var enabled = state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                if (_settings.AutoStartWindows != enabled)
                {
                    WindowsStartupToggle.IsOn = enabled;
                    WindowsStartupStatus.Text = state == Windows.ApplicationModel.StartupTaskState.DisabledByUser
                        ? "Windows 설정에서 자동 실행을 껐어요. Windows의 시작 앱 설정에서 메요일을 켜주세요."
                        : "Windows 정책에 의해 자동 실행 설정을 변경할 수 없습니다.";
                    return false;
                }
            }
            else if (_settings.AutoStartWindows)
            {
                var launcher = WindowsStartup.FindLauncher(AppContext.BaseDirectory);
                if (launcher is null)
                { WindowsStartupStatus.Text = "배포 폴더의 MapleDay.exe로 실행하면 자동 실행을 등록합니다."; return false; }
                _windowsStartup.Enable(launcher);
            }
            else _windowsStartup.Disable();
            UpdateWindowsStartupStatus();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { WindowsStartupStatus.Text = "Windows 시작 항목을 변경하지 못했어요. 설정을 다시 시도해주세요."; return false; }
        finally
        {
            _windowsStartupApplying = false;
            WindowsStartupToggle.IsEnabled = true;
            WindowsStartupBehavior.IsEnabled = WindowsStartupToggle.IsOn;
        }
    }

    private void InitializeWindowSizeSettings()
    {
        WindowWidthInput.Value = _settings.ValidWindowWidth;
        WindowHeightInput.Value = _settings.ValidWindowHeight;
        _windowSizeSettingsReady = true;
    }

    private void WindowSize_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_windowSizeSettingsReady || _windowSizeUpdating || _dataDeleting) return;
        SaveWindowSize();
    }

    private void SaveWindowSize()
    {
        var width = WindowWidthInput.Value;
        var height = WindowHeightInput.Value;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width != Math.Truncate(width) || height != Math.Truncate(height)
            || width is < 420 or > 10000 || height is < 520 or > 10000)
        {
            WindowSizeStatus.Text = "가로 420~10000px, 세로 520~10000px 범위의 정수를 입력해주세요.";
            return;
        }
        _settings.WindowWidth = (int)width;
        _settings.WindowHeight = (int)height;
        try
        {
            _settings.Save();
            WindowSizeStatus.Text = $"기본 창 크기 {width:0} × {height:0}px · 다음 실행부터 적용됩니다.";
        }
        catch (Exception error) when (IsStorageError(error))
        { WindowSizeStatus.Text = "기본 창 크기를 저장하지 못했어요. 다시 설정해주세요."; }
    }

    private void SetWindowSizePreference(int width, int height)
    {
        if (_dataDeleting || _closed) return;
        _windowSizeUpdating = true;
        try
        {
            WindowWidthInput.Value = width;
            WindowHeightInput.Value = height;
        }
        finally { _windowSizeUpdating = false; }
        SaveWindowSize();
    }

    private void CurrentWindowSizeButton_Click(object sender, RoutedEventArgs args)
        => SetWindowSizePreference(AppWindow.Size.Width, AppWindow.Size.Height);

    private void ResetWindowSizeButton_Click(object sender, RoutedEventArgs args)
        => SetWindowSizePreference(1200, 800);

    private async void AppDataRefreshButton_Click(object sender, RoutedEventArgs args) => await RefreshAppDataSizeAsync();

    private async Task RefreshAppDataSizeAsync()
    {
        if (_dataDeleting || _closed) return;
        AppDataSize.Text = "확인 중…";
        try
        {
            var bytes = await _appData.SizeAsync();
            if (!_closed && !_dataDeleting) AppDataSize.Text = AppDataStore.FormatSize(bytes);
        }
        catch (Exception error) when (IsStorageError(error)) { AppDataSize.Text = "데이터 용량을 확인하지 못했어요."; }
    }

    private async void DeleteAppDataButton_Click(object sender, RoutedEventArgs args)
    {
        if (_dataDeleting || _closed) return;
        var input = new TextBox { PlaceholderText = "삭제" };
        // The exact word is required; whitespace and similar words are rejected.
        var contents = new StackPanel { Spacing = 12 };
        contents.Children.Add(new TextBlock
        {
            Text = "저장된 API 키, 캐릭터 목록, 스케줄러·경험치 기록, 설정, 문의 연결 정보와 작성 중인 내용을 삭제합니다. 문의 연결 정보를 삭제하면 기존 문의 답장을 이 앱에서 확인할 수 없어요.\n\n삭제 후 앱이 종료됩니다. 계속하려면 아래에 ‘삭제’를 정확히 입력해주세요.",
            TextWrapping = TextWrapping.Wrap
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, "삭제 확인 문구");
        contents.Children.Add(input);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "앱 데이터 삭제", Content = contents,
            PrimaryButtonText = "데이터 삭제", CloseButtonText = "취소", IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Close
        };
        var danger = new Style(typeof(Button));
        danger.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 196, 43, 28))));
        danger.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)));
        dialog.Resources["ContentDialogPrimaryButtonStyle"] = danger;
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = AppDataStore.IsDeleteConfirmation(input.Text);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || !AppDataStore.IsDeleteConfirmation(input.Text)) return;
        _dataDeleting = true;
        await StopWebHostAsync();
        await IncomeReplayPanel.StopAsync();
        StopUpdates();
        StopTelemetry();
        AppDataSize.Text = "데이터 삭제 중…";
        ShellNavigation.IsEnabled = false;
        Root.IsHitTestVisible = false;
        _replyTimer.Stop();
        _reminderTimer.Stop();
        _reminderLifetime.Cancel();
        _loadCts?.Cancel();
        CancelScheduler();
        CancelLevels();
        _supportLifetime.Cancel();
        // Wait for cancelled writers before removing files so no task recreates them.
        while (_characterActiveLoads > 0 || _schedulerActiveLoads > 0 || _levelActiveLoads > 0 || _profileWrites > 0 || _supportSubmitting || _supportRefreshing || _reminderChecking || _windowsStartupApplying || _updateChecking || _telemetryBusy)
            await Task.Delay(50);
        try
        {
            if (WindowsStartup.IsPackaged) await WindowsStartup.SetPackagedAsync(false);
            else _windowsStartup.Disable();
            await _appData.DeleteAsync(input.Text);
        }
        catch (Exception error) when (IsStorageError(error) || error is System.Security.SecurityException)
        {
            ShellNavigation.IsEnabled = true;
            Root.IsHitTestVisible = true;
            await new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "일부 데이터를 삭제하지 못했어요",
                Content = "파일 사용 상태나 폴더 권한을 확인한 뒤 앱을 다시 실행해서 시도해주세요.", CloseButtonText = "앱 종료"
            }.ShowAsync();
        }
        ExitApp();
    }
}
