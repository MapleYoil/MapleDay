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
    public ObservableCollection<IncomeCharacterChoice> IncomeCharacters { get; } = [];
    private string? _incomeSelectedOcid;
    private bool _rebuildingIncomeCharacters;
    private bool _incomeDisplayReady;
    private int _incomeMode;
    private void IncomeTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (IncomeTabs is null || IncomeHeading is null) return;
        IncomeReplayPanel.Pause();
        IncomeReplayPanel.Visibility = Visibility.Collapsed;
        _incomeMode = Math.Max(0, IncomeTabs.SelectedIndex);
        IncomeBulkAddButton.Visibility = _incomeMode == 2 ? Visibility.Collapsed : Visibility.Visible;
        RefreshIncomeOverview();
    }
    private void HuntingIncome_RecordsChanged(object? sender, EventArgs args)
    { RefreshIncomeOverview(); PublishWebSnapshot(); }
    private IEnumerable<BossLootRecord> StoredLoot => (_settings.BossLootRecords ?? []).Where(BossLoot.Valid);
    private WeeklyIncomeForecast? ForecastFor(SchedulerCharacter character, DateOnly today)
    {
        if (character.History is not { } history) return null;
        var state = history.Today == today ? history.Current : SchedulerLastState.Project(new(history.Today, history.Current, false, DateTimeOffset.UtcNow), today);
        return WeeklyIncomeForecast.Calculate(state, history.Snapshots, character.Income?.Records ?? [], today,
            id => PartySize(character.Ocid, id), character.Character.Level);
    }
    private IncomeDisplay CurrentIncomeDisplay => new(_settings.IncomeDisplayMode, _settings.MesoCashRate);
    private void InitializeIncomeDisplay()
    {
        IncomeDisplaySelector.SelectedIndex = CurrentIncomeDisplay.ValidMode switch { "cash" => 1, "both" => 2, _ => 0 };
        IncomeCashRateInput.Value = (double)CurrentIncomeDisplay.Rate;
        IncomeReplayPanel.OwnerWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Closed += (_, _) => { IncomeReplayPanel.Pause(); _ = IncomeReplayPanel.StopAsync(); };
        _incomeDisplayReady = true;
    }
    private void IncomeDisplaySelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_incomeDisplayReady || _dataDeleting) return;
        _settings.IncomeDisplayMode = (IncomeDisplaySelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "meso";
        SaveIncomeDisplay();
    }
    private void IncomeCashRateInput_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_incomeDisplayReady || _dataDeleting || !double.IsFinite(sender.Value) || sender.Value is < 1 or > 1_000_000_000) return;
        _settings.MesoCashRate = (decimal)sender.Value;
        SaveIncomeDisplay();
    }
    private void SaveIncomeDisplay()
    {
        try { _settings.Save(); IncomeCurrencyStatus.Text = "현금 금액은 설정한 환산가로 계산한 값입니다."; }
        catch (Exception error) when (IsStorageError(error)) { IncomeCurrencyStatus.Text = "환산 설정을 저장하지 못했어요. 이번 실행에서만 적용합니다."; }
        RefreshIncomeOverview();
        IncomeReplayPanel.SetDisplay(CurrentIncomeDisplay);
        PublishWebSnapshot();
    }
    private void HuntingIncome_ReplayRequested(object? sender, EventArgs args)
    {
        var owners = SchedulerAvatars.Where(owner => _incomeSelectedOcid is null || owner.Ocid == _incomeSelectedOcid).ToDictionary(owner => owner.Ocid);
        var records = (_settings.HuntingIncomeRecords ?? []).Where(row => owners.ContainsKey(row.Ocid));
        IncomeReplayPanel.Visibility = Visibility.Visible;
        IncomeReplayPanel.SetRecords(HuntingIncome.Replay(records, ocid => owners[ocid].Name), CurrentIncomeDisplay,
            _incomeSelectedOcid is null ? "전체 캐릭터 사냥 기록" : owners.Values.FirstOrDefault()?.Name ?? "사냥 기록", _settings);
    }
    private void IncomeReplayOpen_Click(object sender, RoutedEventArgs args)
    {
        var characters = SchedulerAvatars.Where(character => _incomeSelectedOcid is null || character.Ocid == _incomeSelectedOcid).ToArray();
        var records = characters.SelectMany(character => (character.Income?.Records ?? []).Select(clear => (character.Ocid, Clear: clear))).ToArray();
        var ocids = characters.Select(character => character.Ocid).ToHashSet(StringComparer.Ordinal);
        IncomeReplayPanel.Visibility = Visibility.Visible;
        IncomeReplayPanel.SetRecords(new IncomeReplay(records, StoredLoot.Where(item => ocids.Contains(item.Ocid))), CurrentIncomeDisplay, _incomeSelectedOcid is null ? "전체 캐릭터" : characters.FirstOrDefault()?.Name ?? "캐릭터", _settings);
    }

    public ObservableCollection<string> CrystalPriceRows { get; } = new(CrystalPrices.All
        .OrderByDescending(price => price.EffectiveFrom).ThenBy(price => price.Name).ThenBy(price => price.Difficulty)
        .Select(price => $"{price.EffectiveFrom:yyyy-MM-dd}부터 · {price.Name} ({price.Difficulty}) · {BossIncome.Money(price.Meso)}"));

    private void RebuildIncomeCharacters()
    {
        _rebuildingIncomeCharacters = true;
        try
        {
            if (_incomeSelectedOcid is not null && !_schedulerCharacters.ContainsKey(_incomeSelectedOcid))
                _incomeSelectedOcid = null;
            IncomeCharacters.Clear();
            IncomeCharacters.Add(new(null, "전체 캐릭터"));
            foreach (var character in SchedulerAvatars)
                IncomeCharacters.Add(new(character.Ocid, $"{character.Name} · {character.Character.World}"));
            IncomeCharacterSelector.SelectedItem = IncomeCharacters.First(choice => choice.Ocid == _incomeSelectedOcid);
        }
        finally { _rebuildingIncomeCharacters = false; }
    }

    private void IncomeCharacterSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuildingIncomeCharacters || IncomeCharacterSelector.SelectedItem is not IncomeCharacterChoice choice) return;
        _incomeSelectedOcid = choice.Ocid;
        IncomeReplayPanel.Pause(); IncomeReplayPanel.Visibility = Visibility.Collapsed;
        RefreshIncomeOverview();
    }

    private void IncomeOpenScheduler_Click(object sender, RoutedEventArgs e) => NavigateTo("scheduler");

    private void IncomeCalendar_PartySizeRequested(object? sender, IncomePartySizeRequest request)
    {
        var record = request.Record;
        if (_dataDeleting || !_schedulerCharacters.TryGetValue(record.Ocid, out var owner)) return;
        var history = owner.History;
        var today = history?.Today ?? SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        _partyFlyout?.Hide();
        var maximum = BossParty.Maximum(record.Boss.Name);
        var number = new NumberBox { Header = $"파티 인원 (1~{maximum}인)", Minimum = 1, Maximum = maximum,
            Value = record.Boss.PartySize, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var all = new CheckBox { Content = new TextBlock { Text = "첫 기록부터 같은 보스의 전체 기록에 적용", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 } };
        var first = owner.Income?.FirstDate ?? record.Boss.Date;
        var range = new TextBlock { Text = $"{owner.Name} · {first:yyyy.MM.dd} ~ {today:yyyy.MM.dd}\n체크하지 않으면 {record.Boss.Date:yyyy.MM.dd} 기록만 변경합니다.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Foreground = AppTheme.Brush("DangerTextBrush") };
        var apply = new Button { Content = "적용", HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var content = new StackPanel { Spacing = 12, Width = 350 };
        content.Children.Add(new TextBlock { Text = $"{record.Boss.Name} · {record.Boss.DifficultyLabel}", FontSize = 18, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(number); content.Children.Add(all); content.Children.Add(range); content.Children.Add(errorText); content.Children.Add(apply);
        var flyout = new Flyout { Content = content, Placement = FlyoutPlacementMode.Top };
        _partyFlyout = flyout;
        void Cleanup()
        {
            request.Anchor.Unloaded -= AnchorUnloaded;
            if (ReferenceEquals(_partyFlyout, flyout)) _partyFlyout = null;
        }
        void AnchorUnloaded(object sender, RoutedEventArgs args) { flyout.Hide(); Cleanup(); }
        request.Anchor.Unloaded += AnchorUnloaded;
        flyout.Closed += (_, _) => Cleanup();
        number.ValueChanged += (_, _) => apply.IsEnabled = double.IsFinite(number.Value)
            && number.Value == Math.Truncate(number.Value) && number.Value >= 1 && number.Value <= maximum;
        apply.Click += (_, _) =>
        {
            if (_closed || _dataDeleting || !apply.IsEnabled || !_schedulerCharacters.TryGetValue(record.Ocid, out var active)
                || !ReferenceEquals(active, owner)) { flyout.Hide(); return; }
            var ids = all.IsChecked == true
                ? BossParty.SavedRecordIds(owner.History?.Snapshots ?? [], record.Boss.Name, record.Boss.Cycle, today)
                    .Concat((owner.Income?.Records ?? []).Where(item => item.Cycle == record.Boss.Cycle
                        && SchedulerBossHistory.BossKey(item.Name) == SchedulerBossHistory.BossKey(record.Boss.Name)).Select(item => item.Id))
                    .Append(record.Boss.Id).Distinct().ToArray()
                : [record.Boss.Id];
            var previous = _settings.BossPartySizes;
            var updated = new Dictionary<string, int>(previous);
            foreach (var id in ids) updated[record.Ocid + "|" + id] = (int)number.Value;
            _settings.BossPartySizes = updated;
            try { _settings.Save(); }
            catch (Exception error) when (IsStorageError(error))
            {
                _settings.BossPartySizes = previous;
                errorText.Text = "인원을 저장하지 못했어요. 저장 공간과 접근 권한을 확인한 뒤 다시 시도해주세요.";
                errorText.Visibility = Visibility.Visible;
                return;
            }
            owner.RefreshIncome(id => PartySize(owner.Ocid, id));
            flyout.Hide();
            RefreshIncomeOverview();
        };
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _dataDeleting || !request.Anchor.IsLoaded || !ReferenceEquals(_partyFlyout, flyout)) { Cleanup(); return; }
            flyout.ShowAt(request.Anchor);
        });
    }

    private void RefreshIncomeOverview()
    {
        if (WeeklyIncomeAmount is null) return;
        var selected = SchedulerAvatars.Where(character => _hasLoadedCharacters
            && (_incomeSelectedOcid is null || character.Ocid == _incomeSelectedOcid)).ToArray();
        var loaded = selected.Where(character => character.Income is not null).ToArray();
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        var weekStart = SchedulerBossHistory.Start(BossCycle.Weekly, today);
        var monthStart = SchedulerBossHistory.Start(BossCycle.Monthly, today);
        var loot = StoredLoot.Where(record => selected.Any(character => character.Ocid == record.Ocid)).ToArray();
        var hunting = (_settings.HuntingIncomeRecords ?? []).Where(row => selected.Any(owner => owner.Ocid == row.Ocid)).ToArray();
        var bossWeekly = loaded.Sum(character => character.Income!.Sum(weekStart, today)) + BossLoot.Sum(loot, weekStart, today);
        var bossMonthly = loaded.Sum(character => character.Income!.Sum(monthStart, today)) + BossLoot.Sum(loot, monthStart, today);
        var bossTotal = loaded.Sum(character => character.Income!.Total) + BossLoot.Sum(loot, DateOnly.MinValue, today);
        var huntWeekly = HuntingIncome.Sum(hunting, weekStart, today);
        var huntMonthly = HuntingIncome.Sum(hunting, monthStart, today);
        var huntTotal = HuntingIncome.Sum(hunting, DateOnly.MinValue, today);
        long Amount(long boss, long hunt) => _incomeMode == 1 ? boss : _incomeMode == 2 ? hunt : boss + hunt;
        WeeklyIncomeAmount.Text = CurrentIncomeDisplay.Format(Amount(bossWeekly, huntWeekly));
        MonthlyIncomeAmount.Text = CurrentIncomeDisplay.Format(Amount(bossMonthly, huntMonthly));
        TotalIncomeAmount.Text = CurrentIncomeDisplay.Format(Amount(bossTotal, huntTotal));
        IncomeHeading.Text = _incomeMode == 1 ? "보스 수익" : _incomeMode == 2 ? "사냥 수익" : "전체 수익";
        IncomeBreakdown.Text = _incomeMode == 0 ? $"이번 주 · 보스 {CurrentIncomeDisplay.Format(bossWeekly)} + 사냥 {CurrentIncomeDisplay.Format(huntWeekly)}\n이번 달 · 보스 {CurrentIncomeDisplay.Format(bossMonthly)} + 사냥 {CurrentIncomeDisplay.Format(huntMonthly)}\n누적 · 보스 {CurrentIncomeDisplay.Format(bossTotal)} + 사냥 {CurrentIncomeDisplay.Format(huntTotal)}" : "";
        BossIncomeDetails.Visibility = _incomeMode == 2 ? Visibility.Collapsed : Visibility.Visible;
        HuntingIncomePanel.Visibility = _incomeMode == 1 ? Visibility.Collapsed : Visibility.Visible;
        HuntingIncomePanel.SetContext(_settings, selected, _incomeSelectedOcid, CurrentIncomeDisplay);
        var forecasts = selected.Select(character => (Character: character, Forecast: ForecastFor(character, today))).ToArray();
        RemainingWeeklyIncomeAmount.Text = CurrentIncomeDisplay.Format(forecasts.Sum(item => item.Forecast?.Meso ?? 0));
        RemainingWeeklyIncomeStatus.Text = "등록된 미완료 주간 보스 · 설정한 파티 인원 · 캐릭터당 주간 12마리 제한 기준\n"
            + string.Join(" · ", forecasts.Select(item => item.Forecast is { } value
                ? $"{item.Character.Name}: {CurrentIncomeDisplay.Format(value.Meso)}{(value.Unpriced > 0 ? $" (가격 미확인 {value.Unpriced}종)" : "")}" : item.Character.Name + ": 스케줄러 조회 필요"));
        ToolTipService.SetToolTip(RemainingWeeklyIncomeAmount, string.Join("\n", forecasts.SelectMany(item => item.Forecast?.Bosses.Select(boss =>
            $"{item.Character.Name} · {boss.Name} · {boss.PartySize}인 · {(boss.Included ? boss.Meso is { } amount ? CurrentIncomeDisplay.Format(amount) : "가격 확인 필요" : "주간 제한에서 제외")}") ?? [])));
        IncomeScope.Text = $"{(_incomeSelectedOcid is not null ? selected.FirstOrDefault()?.Name : "추가한 캐릭터 합산")} · {loaded.Length} / {selected.Length}캐릭터 조회 · 결정 + 물욕템 수령액";
        IncomeOverviewStatus.Text = loaded.Length < selected.Length
            ? "보스 기록을 아직 불러오지 못한 캐릭터가 있어요. 새로고침으로 다시 조회할 수 있습니다."
            : loaded.Any(character => character.Income!.Unpriced > 0
                || !character.Income.CompleteRange(BossCycle.Monthly) || character.Income.Today != today)
                ? "일부 날짜의 기록이나 결정 가격을 확인하지 못했어요. 캘린더의 날짜를 눌러 상세 내역을 확인할 수 있습니다." : "";
        WeeklyCrystalCount.Text = $"이번 주 주간 보스 {loaded.Sum(character => character.Income!.Records.Count(record => record.Included && record.Cycle == BossCycle.Weekly && record.PeriodStart == weekStart))} / {selected.Length * BossIncome.WeeklyCap} · 캐릭터당 최대 12마리 기준";
        IncomeCalendarPanel.SetCharacters(selected, CurrentIncomeDisplay, loot);
        IncomeOverview.Visibility = selected.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        IncomeEmpty.Visibility = _hasLoadedCharacters && SchedulerAvatars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
