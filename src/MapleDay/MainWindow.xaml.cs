using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MapleDay;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<CharacterCard> Characters { get; } = [];
    private readonly NexonApiClient _api = new();
    private readonly ApiKeyStore _keyStore = new();
    private readonly CharacterCacheStore _characterCache = new();
    private readonly CharacterImageLoader _images = new();
    private CancellationTokenSource? _loadCts;
    private string _apiKey = "";
    private bool _busy;
    private bool _closed;
    private bool _hasLoadedCharacters;
    private bool _startupInitialized;
    private int _characterActiveLoads;
    private string _currentPage = "key";
    private readonly Stack<string> _backStack = new();

    public MainWindow()
    {
        InitializeComponent();
        CollapseClosedMessage(ListMessage);
        CollapseClosedMessage(ReminderMessage);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        ApplyTheme();
        TitleBar.SizeChanged += (_, _) => UpdateCaptionInset();
        ShellNavigation.SelectedItem = KeyNavigationItem;
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "mapleday.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(_settings.ValidWindowWidth, _settings.ValidWindowHeight));
        InitializeSupport();
        UpdateCharacterWorlds();
        InitializeScheduler();
        InitializeReminders();
        InitializeLevels();
        InitializeWindowsStartup();
        InitializeWindowSizeSettings();
        InitializeUpdates();
        InitializeTelemetry();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 420;
            presenter.PreferredMinimumHeight = 520;
        }
        Root.Loaded += async (_, _) => await InitializeForLaunchAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _loadCts?.Cancel();
            _apiKey = "";
            ApiKeyInput.Password = "";
        };
    }

    public void Start(bool automatic)
    {
        if (WindowsStartup.ShouldStartInTray(automatic, _settings.WindowsStartupToTray, _tray?.Available == true))
            AppWindow.Hide();
        else Activate();
        // A hidden window may not raise Loaded until it is first opened.
        // Start background work independently and only once for this process.
        _ = InitializeForLaunchAsync();
    }

    private async Task InitializeForLaunchAsync()
    {
        if (_startupInitialized || _closed || _dataDeleting) return;
        _startupInitialized = true;
        _replyTimer.Start();
        _reminderTimer.Start();
        StartAutomaticUpdates();
        StartTelemetry();
        await Task.WhenAll(InitializeCharactersForLaunchAsync(), InitializeSupportForLaunchAsync());
        _ = SendTelemetryAsync();
    }

    private async Task InitializeCharactersForLaunchAsync()
    {
        ApiKeyInput.IsEnabled = false;
        try
        {
            var saved = await _keyStore.LoadAsync();
            await LoadKeyProfilesAsync(saved);
            if (!_closed && !_dataDeleting && !string.IsNullOrWhiteSpace(saved))
            {
                _apiKey = saved;
                await RestoreCachedCharactersAsync(saved);
                if (_closed) return;
                _ = CheckRemindersAsync();
                NavigateTo(_notificationLandingPage ?? _settings.ValidStartPage, remember: false);
                await LoadAsync(navigateToCharacters: false, registeredOnly: _hasLoadedCharacters);
            }
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            if (!_closed) ShowError("저장된 키를 읽을 수 없어요", "API 키를 다시 입력하면 새 키를 저장합니다.", false);
        }
        finally
        {
            if (!_closed)
            {
                ApiKeyInput.IsEnabled = true;
                if (AppWindow.IsVisible && _currentPage == "key") ApiKeyInput.Focus(FocusState.Programmatic);
            }
        }
    }
    private void UpdateCaptionInset()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        CaptionInset.Width = new GridLength(Math.Max(144, AppWindow.TitleBar.RightInset / scale));
    }

    private static void CollapseClosedMessage(InfoBar message)
    {
        message.Visibility = message.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        message.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (sender, _) =>
        {
            var bar = (InfoBar)sender;
            bar.Visibility = bar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void NavigateTo(string page, bool remember = true)
    {
        // Opening the notification menu always returns to its content list,
        // including clicking the same sidebar item or activating a toast.
        if (page == "notifications") ShowNotificationSettings(false);
        if (page == _currentPage) return;
        if (remember) _backStack.Push(_currentPage);
        _currentPage = page;
        var isKeyPage = page == "key";
        KeyPage.Visibility = isKeyPage ? Visibility.Visible : Visibility.Collapsed;
        CharacterPage.Visibility = page == "characters" ? Visibility.Visible : Visibility.Collapsed;
        SupportPage.Visibility = page == "support" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        SchedulerPage.Visibility = page == "scheduler" ? Visibility.Visible : Visibility.Collapsed;
        IncomePage.Visibility = page == "income" ? Visibility.Visible : Visibility.Collapsed;
        LevelPage.Visibility = page == "level" ? Visibility.Visible : Visibility.Collapsed;
        NotificationsPage.Visibility = page == "notifications" ? Visibility.Visible : Visibility.Collapsed;
        ConnectState.Visibility = !_hasLoadedCharacters && string.IsNullOrEmpty(_apiKey) ? Visibility.Visible : Visibility.Collapsed;
        ShellNavigation.SelectedItem = page switch { "key" => KeyNavigationItem, "support" => SupportNavigationItem, "settings" => SettingsNavigationItem, "scheduler" => SchedulerNavigationItem, "income" => IncomeNavigationItem, "level" => LevelNavigationItem, "notifications" => NotificationsNavigationItem, _ => CharactersNavigationItem };
        BackButton.IsEnabled = _backStack.Count > 0;
        if (isKeyPage)
        {
            RevealKeyButton.IsChecked = false;
            if (string.IsNullOrEmpty(ApiKeyInput.Password)) ApiKeyInput.Password = _apiKey;
            ApiKeyInput.Focus(FocusState.Programmatic);
        }
        else if (page == "characters")
        {
            if (!_hasLoadedCharacters) CharacterCount.Text = string.IsNullOrEmpty(_apiKey) ? "API 키를 연결하면 내 캐릭터를 볼 수 있어요." : "캐릭터 목록을 불러오는 중…";
            CharacterGrid.Focus(FocusState.Programmatic);
        }
        else if (page == "support")
        {
            UpdateSupportLogin();
            _ = RefreshSupportAsync(showErrors: true);
        }
        else if (page == "scheduler")
        {
            UpdateSchedulerLogin();
            BackButton.IsEnabled = _backStack.Count > 0 || _schedulerSelectedOcid is not null;
            _ = LoadSchedulerAsync();
        }
        else if (page == "income")
        {
            UpdateSchedulerLogin();
            _ = LoadSchedulerAsync();
        }
        else if (page == "settings") _ = RefreshAppDataSizeAsync();
        else if (page == "level") { UpdateLevelPage(); _ = LoadLevelsAsync(); }
        else if (page == "notifications") { RefreshReminderList(); UpdateReminderSchedule(); _ = CheckRemindersAsync(); }
    }

    private void ShellNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == ThemeNavigationItem) { ToggleDarkMode(); return; }
        if (args.InvokedItemContainer?.Tag is string page) NavigateTo(page);
    }

    private void PaneToggleButton_Click(object sender, RoutedEventArgs e) =>
        ShellNavigation.IsPaneOpen = !ShellNavigation.IsPaneOpen;

    private void ShellNavigation_PaneChanged(NavigationView sender, object args)
    {
        PaneToggleButton.IsChecked = sender.IsPaneOpen;
        var label = sender.IsPaneOpen ? "메뉴 접기" : "메뉴 펼치기";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PaneToggleButton, label);
        ToolTipService.SetToolTip(PaneToggleButton, label);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        if (_currentPage == "notifications" && _notificationSettingsOpen)
        {
            ShowNotificationSettings(false);
            return;
        }
        if (_currentPage == "scheduler" && _schedulerSelectedOcid is not null)
        {
            ShowAllSchedulerCharacters();
            return;
        }
        if (_backStack.TryPop(out var page)) NavigateTo(page, remember: false);
    }

    private void OpenKeyPageButton_Click(object sender, RoutedEventArgs e) => NavigateTo("key");

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Left && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
        {
            GoBack();
            e.Handled = true;
        }
    }

    private void ApiKeyInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ConnectButton is not null)
            ConnectButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(ApiKeyInput.Password);
        if (KeyError is not null) KeyError.IsOpen = false;
    }

    private async void ApiKeyInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ConnectButton.IsEnabled)
        {
            e.Handled = true;
            await ConnectAsync();
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private void RevealKeyButton_Changed(object sender, RoutedEventArgs e)
    {
        var visible = RevealKeyButton.IsChecked == true;
        ApiKeyInput.PasswordRevealMode = visible ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RevealKeyButton,
            visible ? "API 키 숨기기" : "API 키 표시");
    }

    private async Task ConnectAsync()
    {
        if (_busy || _dataDeleting) return;
        var key = ApiKeyInput.Password.Trim();
        if (!key.StartsWith("live_", StringComparison.Ordinal) || key.Length <= 5
            || key.Any(c => c > 127 || char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            KeyError.Title = "API 키를 확인해주세요";
            KeyError.Message = "live_로 시작하는 API 키 전체를 입력해주세요.";
            KeyError.IsOpen = true;
            return;
        }
        if (_hasLoadedCharacters && _apiKey == key)
        {
            RevealKeyButton.IsChecked = false;
            ApiKeyInput.Password = "";
            NavigateTo("characters");
            return;
        }
        if (_apiKey != key)
        {
            CancelScheduler();
            Characters.Clear();
            _cachedCharacters.Clear();
            _characterAccountCount = 0;
            _hasLoadedCharacters = false;
            _apiKey = key;
            UpdateCharacterWorlds();
            UpdateSchedulerCharacters();
            UpdateSupportLogin();
            _characterActiveLoads++;
            SetBusy(true);
            try
            {
                await RestoreCachedCharactersAsync(key);
                if (_dataDeleting || _closed) return;
                if (_hasLoadedCharacters)
                {
                    try { await _keyStore.SaveAsync(key, _supportLifetime.Token); }
                    catch (Exception error) when (IsStorageError(error)) { ShowError("API 키를 저장하지 못했어요", "이번 실행에는 적용됩니다.", false, characterListOnly: true); }
                    await SaveKeyProfilesAsync();
                    NavigateTo("characters");
                    return;
                }
            }
            catch (OperationCanceledException) when (_supportLifetime.IsCancellationRequested) { return; }
            finally { _characterActiveLoads--; if (!_closed) SetBusy(false); }
        }
        await LoadAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void ChangeKeyButton_Click(object sender, RoutedEventArgs e)
    {
        RevealKeyButton.IsChecked = false;
        ApiKeyInput.Password = _apiKey;
        UpdateSavedKeySelector();
        NavigateTo("key");
        KeyError.IsOpen = false;
        ApiKeyInput.Focus(FocusState.Programmatic);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _loadCts?.Cancel();

    private async Task LoadAsync(bool navigateToCharacters = true, bool registeredOnly = false)
    {
        if (_busy || _dataDeleting || string.IsNullOrEmpty(_apiKey)) return;
        if (registeredOnly && !_hasLoadedCharacters) registeredOnly = false;
        if (registeredOnly && !Characters.Any(character => _settings.SchedulerOcids.Contains(character.Ocid))) return;
        _characterActiveLoads++;
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;
        var key = _apiKey;
        SetBusy(true);
        KeyError.IsOpen = ListMessage.IsOpen = false;
        try
        {
            if (!registeredOnly)
            {
                var list = await _api.GetCharactersAsync(key, token);
                token.ThrowIfCancellationRequested();
                var summaries = list.Accounts.SelectMany(account => account.Characters)
                    .Where(character => !string.IsNullOrWhiteSpace(character.Ocid))
                    .DistinctBy(character => character.Ocid)
                    .OrderByDescending(character => character.Level)
                    .ThenBy(character => character.World).ThenBy(character => character.Name).ToList();
                var previous = _cachedCharacters;
                _cachedCharacters = summaries.ToDictionary(summary => summary.Ocid,
                    summary => previous.TryGetValue(summary.Ocid, out var saved) ? saved with { Summary = summary } : new CachedCharacter(summary));
                _characterAccountCount = list.Accounts.Count;
                Characters.Clear();
                foreach (var summary in summaries)
                {
                    var card = new CharacterCard(summary);
                    var saved = _cachedCharacters[summary.Ocid];
                    if (saved.Basic is not null) card.Apply(saved.Basic, saved.Image, saved.ImageFailed, saved.UpdatedAt);
                    Characters.Add(card);
                }
                _hasLoadedCharacters = true;
                UpdateCharacterWorlds();
                UpdateSchedulerCharacters();
                UpdateRepresentativeCharacter();
                UpdateSupportLogin();
            }
            var cached = _cachedCharacters;
            var keySaved = true;
            try { await _keyStore.SaveAsync(key, token); }
            catch (Exception ex) when (IsStorageError(ex)) { keySaved = false; }
            token.ThrowIfCancellationRequested();
            CharacterCount.Text = $"계정 {_characterAccountCount}개 · 캐릭터 {Characters.Count}개";
            if (navigateToCharacters && _currentPage == "key") NavigateTo("characters");
            _ = CheckRemindersAsync();
            if (_currentPage is "scheduler" or "income") _ = LoadSchedulerAsync();
            ConnectState.Visibility = Visibility.Collapsed;
            if (!keySaved)
                ShowError("API 키를 저장하지 못했어요", "이번 조회는 계속됩니다. 다음 실행 때 키를 다시 입력해주세요.", false);
            ApiKeyInput.Password = "";
            RevealKeyButton.IsChecked = false;
            var registered = registeredOnly ? _settings.SchedulerOcids.ToArray() : null;
            var cards = Characters.Where(character => registered is null || registered.Contains(character.Ocid)).ToArray();
            foreach (var card in cards) card.BeginLoad();
            DetailLoading.Visibility = cards.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            DetailProgress.Maximum = Math.Max(cards.Length, 1);
            DetailProgress.Value = 0;
            LoadingText.Text = $"캐릭터 정보 불러오는 중 · 0 / {cards.Length}";
            var completed = 0;
            var failed = 0;
            var failedImages = 0;
            var cardsByOcid = cards.ToDictionary(card => card.Ocid);
            await new CharacterDetailsLoader(_api, _images).RefreshAsync(key, Characters.Select(card => card.Ocid), registered,
                update => OnUiAsync(() =>
                    {
                        if (_loadCts != cts || _closed || _apiKey != key) return;
                        var card = cardsByOcid[update.Ocid];
                        var basic = update.Basic;
                        if (basic is not null)
                        {
                            var previousWorld = card.World;
                            card.Apply(basic, update.Image, update.ImageFailed, update.UpdatedAt);
                            var saved = cached[card.Ocid];
                            cached[card.Ocid] = update.Merge(saved);
                            if (previousWorld != card.World) UpdateCharacterWorlds();
                            if (_schedulerCharacters.TryGetValue(card.Ocid, out var scheduled))
                                scheduled.RefreshCharacter(id => PartySize(card.Ocid, id));
                            if (update.ImageFailed) failedImages++;
                        }
                        else { card.MarkFailed(update.Error ?? "기본 정보를 불러오지 못했습니다"); failed++; }
                        completed++;
                        DetailProgress.Value = completed;
                        LoadingText.Text = $"캐릭터 정보 불러오는 중 · {completed} / {cards.Length}";
                    }), token);
            token.ThrowIfCancellationRequested();
            try { await _characterCache.SaveAsync(key, _characterAccountCount, cached.Values.ToList(), token); }
            catch (Exception error) when (IsStorageError(error))
            {
                if (!_closed && _loadCts == cts)
                    ShowError("캐릭터 정보를 저장하지 못했어요", "현재 목록은 유지됩니다. 저장 공간을 확인한 뒤 새로고침해주세요.", false);
            }
            token.ThrowIfCancellationRequested();
            UpdateRepresentativeCharacter();
            await SaveKeyProfilesAsync();
            _ = SendTelemetryAsync();
            if (failed > 0)
            {
                ShowError($"캐릭터 {failed}개의 기본 정보를 불러오지 못했어요", "목록 정보는 그대로 표시했습니다. 새로고침으로 다시 조회할 수 있어요.", false, characterListOnly: true);
            }
            else if (failedImages > 0)
                ShowError($"캐릭터 {failedImages}개의 이미지를 불러오지 못했어요", "캐릭터 정보는 그대로 표시했습니다. 새로고침으로 다시 조회할 수 있어요.", false, characterListOnly: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (_loadCts == cts && !_closed)
            {
                foreach (var card in Characters) card.MarkCanceled();
                ShowError("조회를 중단했어요", "새로고침을 누르면 다시 조회할 수 있어요.", false);
            }
        }
        catch (NexonApiException ex)
        {
            if (!ex.IsAuthenticationError) ReportDiagnostic(ex, "characters");
            if (_loadCts == cts && !_closed)
            {
                foreach (var card in Characters) card.MarkCanceled();
                if (ex.IsAuthenticationError)
                {
                    _hasLoadedCharacters = false;
                    UpdateSupportLogin();
                    UpdateSchedulerLogin();
                    ChangeKeyButton_Click(this, new RoutedEventArgs());
                    ShowError("API 키를 확인해주세요", ex.Message, true);
                }
                else ShowError("캐릭터를 불러오지 못했어요", ex.Message, true);
            }
        }
        catch (HttpRequestException error)
        {
            ReportDiagnostic(error, "characters");
            if (_loadCts == cts && !_closed)
                ShowError("서버에 연결할 수 없어요", "인터넷 연결을 확인한 뒤 다시 조회해주세요.", true);
        }
        catch (OperationCanceledException)
        {
            if (_loadCts == cts && !_closed)
                ShowError("서버 응답이 늦어지고 있어요", "잠시 후 다시 조회해주세요.", true);
        }
        finally
        {
            _characterActiveLoads--;
            if (_loadCts == cts && !_closed)
            {
                _loadCts = null;
                SetBusy(false);
                DetailLoading.Visibility = Visibility.Collapsed;
            }
            cts.Dispose();
        }
    }

    private Task OnUiAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        })) completion.SetCanceled();
        return completion.Task;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ConnectButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(ApiKeyInput.Password);
        ApiKeyInput.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        KeyLoading.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string title, string message, bool isError, bool characterListOnly = false)
    {
        // Character refreshes can finish on another page; keep their feedback with the character list.
        var bar = !characterListOnly && _currentPage == "key" ? KeyError : ListMessage;
        bar.Title = title;
        bar.Message = message;
        bar.Severity = isError ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
        bar.IsOpen = true;
    }

    private static bool IsStorageError(Exception exception) => exception is IOException
        or UnauthorizedAccessException or System.Runtime.InteropServices.COMException
        or System.Security.Cryptography.CryptographicException;
}
