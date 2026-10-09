using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using MapleDay.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MapleDay;

public sealed partial class MainWindow
{
    public ObservableCollection<SchedulerCharacter> SchedulerGroups { get; } = [];
    public ObservableCollection<SchedulerCharacter> SchedulerAvatars { get; } = [];
    private readonly Dictionary<string, SchedulerCharacter> _schedulerCharacters = new(StringComparer.Ordinal);
    private CancellationTokenSource? _schedulerCts;
    private string? _schedulerSelectedOcid;
    private readonly SchedulerHistoryStore _schedulerHistory = new();
    private int _schedulerActiveLoads;
    private readonly DispatcherTimer _schedulerDayTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    private void InitializeScheduler()
    {
        SchedulerAvatarList.AddHandler(UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(SchedulerAvatarScroller_PointerWheelChanged), true);
        _settings.SchedulerOcids ??= [];
        _settings.BossPartySizes ??= [];
        _settings.ManualWeeklyClears ??= [];
        UpdateSchedulerCharacters();
        _schedulerDayTimer.Tick += async (_, _) =>
        {
            var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
            if ((_currentPage is "scheduler" or "income") && _schedulerCharacters.Values.Any(character => character.HistoryDate is { } date && date != today))
                await LoadSchedulerAsync();
        };
        _schedulerDayTimer.Start();
        Closed += (_, _) => { _schedulerCts?.Cancel(); _schedulerDayTimer.Stop(); };
    }

    private void UpdateSchedulerCharacters()
    {
        CancelScheduler();
        _schedulerCharacters.Clear();
        foreach (var character in Characters.Where(character => _settings.SchedulerOcids.Contains(character.Ocid)))
            _schedulerCharacters[character.Ocid] = new(character, () => _settings.ManualWeeklyClears.Where(clear => clear.Ocid == character.Ocid));
        foreach (var character in Characters) character.SetSchedulerAdded(_schedulerCharacters.ContainsKey(character.Ocid));
        RebuildSchedulerViews();
        UpdateSchedulerLogin();
    }

    private void UpdateSchedulerLogin()
    {
        SchedulerLogin.Visibility = _hasLoadedCharacters ? Visibility.Collapsed : Visibility.Visible;
        SchedulerControls.Visibility = _hasLoadedCharacters ? Visibility.Visible : Visibility.Collapsed;
        IncomeLogin.Visibility = _hasLoadedCharacters ? Visibility.Collapsed : Visibility.Visible;
        IncomeControls.Visibility = _hasLoadedCharacters ? Visibility.Visible : Visibility.Collapsed;
        ApplySchedulerView();
        UpdateSchedulerButtons();
        RefreshIncomeOverview();
    }

    private void RebuildSchedulerViews()
    {
        SchedulerAvatars.Clear();
        foreach (var ocid in SchedulerSelection.Visible(_settings.SchedulerOcids, Characters.Select(character => character.Ocid), null))
            if (_schedulerCharacters.TryGetValue(ocid, out var character)) SchedulerAvatars.Add(character);
        if (_schedulerSelectedOcid is not null && !_schedulerCharacters.ContainsKey(_schedulerSelectedOcid))
            _schedulerSelectedOcid = null;
        RebuildIncomeCharacters();
        RebuildLevelCharacters();
        ApplySchedulerView();
        RefreshIncomeOverview();
    }

    private void ApplySchedulerView()
    {
        var visible = _hasLoadedCharacters
            ? SchedulerSelection.Visible(_settings.SchedulerOcids, Characters.Select(character => character.Ocid), _schedulerSelectedOcid)
            : [];
        SchedulerGroups.Clear();
        foreach (var ocid in visible)
            if (_schedulerCharacters.TryGetValue(ocid, out var character)) SchedulerGroups.Add(character);
        SchedulerEmpty.Visibility = _hasLoadedCharacters && SchedulerGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var visibleSet = visible.ToHashSet(StringComparer.Ordinal);
        foreach (var character in SchedulerAvatars)
        {
            character.SetSelected(character.Ocid == _schedulerSelectedOcid);
            character.SetViewVisible(visibleSet.Contains(character.Ocid));
        }
        SchedulerAvatarList.Visibility = _hasLoadedCharacters && SchedulerAvatars.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_currentPage == "scheduler") BackButton.IsEnabled = _backStack.Count > 0 || _schedulerSelectedOcid is not null;
        UpdateSchedulerButtons();
    }

    private void UpdateSchedulerButtons()
    {
        if (SchedulerRefreshButton is null) return;
        SchedulerRefreshButton.IsEnabled = _hasLoadedCharacters && SchedulerGroups.Count > 0;
        SchedulerCancelButton.Visibility = _schedulerCts is null ? Visibility.Collapsed : Visibility.Visible;
        IncomeRefreshButton.IsEnabled = _hasLoadedCharacters && SchedulerAvatars.Count > 0;
        IncomeCancelButton.Visibility = _schedulerCts is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool AddSchedulerCharacter(CharacterCard character)
    {
        if (!_hasLoadedCharacters || !Characters.Contains(character) || _schedulerCharacters.ContainsKey(character.Ocid)) return false;
        CancelScheduler();
        if (!_settings.SchedulerOcids.Contains(character.Ocid)) _settings.SchedulerOcids.Add(character.Ocid);
        _schedulerCharacters[character.Ocid] = new(character, () => _settings.ManualWeeklyClears.Where(clear => clear.Ocid == character.Ocid));
        character.SetSchedulerAdded(true);
        SaveSchedulerSelection();
        RebuildSchedulerViews();
        UpdateSchedulerButtons();
        return true;
    }

    private async void SchedulerRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string ocid }) return;
        CancelScheduler();
        _settings.SchedulerOcids.RemoveAll(value => value == ocid);
        _schedulerCharacters.Remove(ocid);
        Characters.FirstOrDefault(character => character.Ocid == ocid)?.SetSchedulerAdded(false);
        SaveSchedulerSelection();
        RebuildSchedulerViews();
        await LoadSchedulerAsync();
    }

    private void SaveSchedulerSelection()
    {
        var message = _currentPage == "characters" ? ListMessage : SchedulerMessage;
        message.IsOpen = false;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            message.Title = "캐릭터 선택을 저장하지 못했어요";
            message.Message = "이번 실행에는 적용됩니다. 다음 실행 때 캐릭터를 다시 추가해주세요.";
            message.Severity = InfoBarSeverity.Warning;
            message.IsOpen = true;
        }
    }

    private void ShowAllSchedulerCharacters()
    {
        _schedulerSelectedOcid = null;
        ApplySchedulerView();
        SchedulerProgressView.ChangeView(0, 0, null, disableAnimation: true);
    }

    private void SchedulerBoard_ChooseCharacterRequested(object? sender, EventArgs e)
    {
        ShowAllSchedulerCharacters();
        SchedulerAvatarList.StartBringIntoView();
    }
    private void SchedulerBoard_NextCharacterRequested(object? sender, EventArgs e)
    {
        if (sender is not Views.SchedulerBoard { DataContext: SchedulerCharacter current } || SchedulerAvatars.Count == 0) return;
        var index = SchedulerAvatars.ToList().FindIndex(character => character.Ocid == current.Ocid);
        _schedulerSelectedOcid = SchedulerAvatars[(index + 1) % SchedulerAvatars.Count].Ocid;
        ApplySchedulerView();
        SchedulerAvatarList.ScrollIntoView(SchedulerAvatars[(index + 1) % SchedulerAvatars.Count]);
        SchedulerProgressView.ChangeView(0, 0, null, disableAnimation: true);
    }

    private bool _schedulerAvatarDragging;
    private void SchedulerAvatar_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_schedulerAvatarDragging || e.ClickedItem is not SchedulerCharacter character || !_schedulerCharacters.ContainsKey(character.Ocid)) return;
        var ocid = character.Ocid;
        _schedulerSelectedOcid = _schedulerSelectedOcid == ocid ? null : ocid;
        ApplySchedulerView();
        SchedulerProgressView.ChangeView(0, 0, null, disableAnimation: true);
    }

    private void SchedulerAvatar_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        e.Cancel = _dataDeleting || SchedulerAvatars.Count < 2;
        _schedulerAvatarDragging = !e.Cancel;
    }

    private void SchedulerAvatar_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        // Keep click suppression through the release event that ends the drag.
        DispatcherQueue.TryEnqueue(() => _schedulerAvatarDragging = false);
        if (!_dataDeleting && e.DropResult == Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move)
            CommitSchedulerAvatarOrder();
    }

    private void SchedulerAvatar_MoveUp(object sender, RoutedEventArgs e) => MoveSchedulerAvatar(sender, -1);
    private void SchedulerAvatar_MoveDown(object sender, RoutedEventArgs e) => MoveSchedulerAvatar(sender, 1);
    private void MoveSchedulerAvatar(object sender, int direction)
    {
        if (_dataDeleting || _schedulerAvatarDragging || sender is not MenuFlyoutItem { Tag: string ocid }) return;
        var index = SchedulerAvatars.ToList().FindIndex(character => character.Ocid == ocid);
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= SchedulerAvatars.Count) return;
        SchedulerAvatars.Move(index, destination);
        CommitSchedulerAvatarOrder();
    }

    private void CommitSchedulerAvatarOrder()
    {
        var order = SchedulerSelection.Reorder(_settings.SchedulerOcids, SchedulerAvatars.Select(character => character.Ocid));
        if (_settings.SchedulerOcids.SequenceEqual(order)) return;
        _settings.SchedulerOcids = order.ToList();
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            SchedulerMessage.Title = "캐릭터 순서를 저장하지 못했어요";
            SchedulerMessage.Message = "이번 실행에는 적용됩니다. 다시 순서를 변경하면 저장을 재시도합니다.";
            SchedulerMessage.Severity = InfoBarSeverity.Warning;
            SchedulerMessage.IsOpen = true;
        }
        ApplySchedulerView();
        RebuildIncomeCharacters();
        RebuildLevelCharacters();
    }

    private void SchedulerAvatarScroller_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var scroller = FindAvatarScrollViewer(SchedulerAvatarList);
        if (_schedulerAvatarDragging || scroller is null || scroller.ScrollableHeight <= 0) return;
        var properties = e.GetCurrentPoint(scroller).Properties;
        var delta = properties.MouseWheelDelta;
        if (properties.IsHorizontalMouseWheel) return;
        var offset = scroller.VerticalOffset - delta * .8;
        scroller.ChangeView(null, Math.Clamp(offset, 0, scroller.ScrollableHeight), null);
        e.Handled = true;
    }

    private static ScrollViewer? FindAvatarScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scroller) return scroller;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindAvatarScrollViewer(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }

    private async void SchedulerRefreshButton_Click(object sender, RoutedEventArgs e) => await LoadSchedulerAsync(force: true);
    private void SchedulerCancelButton_Click(object sender, RoutedEventArgs e) => CancelScheduler();

    private void CancelScheduler()
    {
        _schedulerCts?.Cancel();
        _schedulerCts = null;
        foreach (var character in _schedulerCharacters.Values.Where(character => character.Loading))
            character.Failed("조회를 중단했어요. 새로고침으로 다시 조회해주세요.");
        UpdateSchedulerButtons();
    }

    private async Task LoadSchedulerAsync(bool force = false)
    {
        if (_closed || _dataDeleting || !_hasLoadedCharacters || string.IsNullOrEmpty(_apiKey)) return;
        if (!force && _schedulerCts is not null) return;
        if (force) CancelScheduler();
        var targets = _schedulerCharacters.Values.Where(character => force || character.UpdatedAt is null
            || character.HistoryDate != SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow)
            || DateTimeOffset.Now - character.UpdatedAt > TimeSpan.FromMinutes(1)).ToArray();
        if (targets.Length == 0) return;
        _schedulerActiveLoads++;
        var cts = new CancellationTokenSource();
        _schedulerCts = cts;
        var key = _apiKey;
        foreach (var character in targets) character.BeginLoad();
        UpdateSchedulerButtons();
        try
        {
            await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = cts.Token },
                async (character, token) =>
                {
                    SchedulerHistoryResult? result = null;
                    string? error = null;
                    try { result = await new SchedulerHistoryLoader(_api, _schedulerHistory).LoadAsync(character.Ocid, key, DateTimeOffset.UtcNow, token); }
                    catch (NexonApiException exception) { error = exception.Message; if (!exception.IsAuthenticationError) ReportDiagnostic(exception, "scheduler"); }
                    catch (HttpRequestException exception) { error = "서버에 연결할 수 없어요. 잠시 후 새로고침해주세요."; ReportDiagnostic(exception, "scheduler"); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { error = "응답 시간이 초과됐어요. 새로고침해주세요."; }
                    token.ThrowIfCancellationRequested();
                    await OnUiAsync(() =>
                    {
                        if (_closed || _schedulerCts != cts || !_schedulerCharacters.TryGetValue(character.Ocid, out var current)
                            || !ReferenceEquals(current, character)) return;
                        if (result is not null) character.Apply(result, id => PartySize(character.Ocid, id));
                        else character.Failed(error ?? "스케줄러를 불러오지 못했어요.");
                        RefreshIncomeOverview();
                    });
                });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested || _closed) { }
        finally
        {
            _schedulerActiveLoads--;
            if (!_closed && _schedulerCts == cts)
            {
                _schedulerCts = null;
                UpdateSchedulerButtons();
            }
            cts.Dispose();
        }
    }

    private int PartySize(string ocid, string id) => _settings.BossPartySizes.GetValueOrDefault(ocid + "|" + id, 1);

    private Flyout? _partyFlyout;
    private void SchedulerBoard_PartySizeRequested(object? sender, PartySizeRequest request)
    {
        var row = request.Tile;
        if (_dataDeleting || !row.CanSetPartySize || !_schedulerCharacters.TryGetValue(row.Ocid, out var owner)) return;
        _partyFlyout?.Hide();
        var maximum = row.MaximumPartySize;
        var number = new NumberBox { Minimum = 1, Maximum = maximum, Value = row.PartySize,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Header = $"결정 가격을 나눌 파티 인원 (1~{maximum}인)" };
        var apply = new Button { Content = "적용", HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var content = new StackPanel { Spacing = 12, MinWidth = 260 };
        content.Children.Add(new TextBlock { Text = row.Name, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        content.Children.Add(number);
        content.Children.Add(apply);
        // Flyout provides light dismissal for clicks outside and Escape.
        var flyout = new Flyout { Content = content, Placement = FlyoutPlacementMode.Left };
        _partyFlyout = flyout;
        void Cleanup()
        {
            request.Anchor.Unloaded -= AnchorUnloaded;
            if (ReferenceEquals(_partyFlyout, flyout)) _partyFlyout = null;
        }
        void AnchorUnloaded(object sender, RoutedEventArgs e) { flyout.Hide(); Cleanup(); }
        request.Anchor.Unloaded += AnchorUnloaded;
        flyout.Closed += (_, _) => Cleanup();
        number.ValueChanged += (_, _) => apply.IsEnabled = double.IsFinite(number.Value)
            && number.Value == Math.Truncate(number.Value) && number.Value >= 1 && number.Value <= maximum;
        apply.Click += (_, _) =>
        {
            if (_dataDeleting || !apply.IsEnabled || !_schedulerCharacters.TryGetValue(row.Ocid, out var active)
                || !ReferenceEquals(owner, active)) { flyout.Hide(); return; }
            var prefKey = row.Ocid + "|" + row.PartyId;
            var previous = _settings.BossPartySizes.GetValueOrDefault(prefKey);
            _settings.BossPartySizes[prefKey] = (int)number.Value;
            try { _settings.Save(); }
            catch (Exception error) when (IsStorageError(error))
            {
                if (previous == 0) _settings.BossPartySizes.Remove(prefKey); else _settings.BossPartySizes[prefKey] = previous;
                SchedulerMessage.Title = "파티 인원을 저장하지 못했어요";
                SchedulerMessage.Message = "저장 공간과 접근 권한을 확인한 뒤 다시 시도해주세요.";
                SchedulerMessage.Severity = InfoBarSeverity.Warning; SchedulerMessage.IsOpen = true;
                return;
            }
            if (_schedulerCharacters.TryGetValue(row.Ocid, out var character)) character.RefreshIncome(id => PartySize(row.Ocid, id));
            RefreshIncomeOverview();
            flyout.Hide();
        };
        // Finish the button's pointer release and drag cleanup before opening the
        // light-dismiss popup, so that same input cannot dismiss it immediately.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _dataDeleting || !request.Anchor.IsLoaded || !ReferenceEquals(_partyFlyout, flyout)) { Cleanup(); return; }
            flyout.ShowAt(request.Anchor);
        });
    }
}
