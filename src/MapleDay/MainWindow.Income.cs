using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using MapleDay.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MapleDay;

public sealed record IncomeMonthChoice(DateOnly Month, string Label);
public sealed record IncomeWeekChoice(IncomeWeekRange Range, string Label);

public sealed partial class MainWindow
{
    public ObservableCollection<IncomeCharacterChoice> IncomeCharacters { get; } = [];
    public ObservableCollection<IncomeLootSummaryRow> IncomeLootRows { get; } = [];
    private readonly ObservableCollection<IncomeMonthChoice> _incomeMonths = [];
    private readonly ObservableCollection<IncomeWeekChoice> _incomeWeeks = [];
    private string? _incomeSelectedOcid;
    private bool _rebuildingIncomeCharacters;
    private bool _rebuildingIncomePeriods;
    private bool _incomeDisplayReady;
    private int _incomeMode;
    private int _incomeLootRange;
    private DateOnly _incomeSelectedWeekStart = IncomeHistory.Week(SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow)).Start;
    private void IncomeTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (IncomeTabs is null || IncomeHeading is null) return;
        IncomeReplayPanel.Pause();
        IncomeReplayPanel.Visibility = Visibility.Collapsed;
        _incomeMode = Math.Max(0, IncomeTabs.SelectedIndex);
        IncomeBulkAddButton.Visibility = _incomeMode is 2 or 3 or 4 ? Visibility.Collapsed : Visibility.Visible;
        if (IncomePeriodControls is not null)
            IncomePeriodControls.Visibility = _incomeMode == 4 ? Visibility.Collapsed : Visibility.Visible;
        RefreshIncomeOverview();
    }
    private void HuntingIncome_RecordsChanged(object? sender, EventArgs args)
    { RebuildIncomePeriods(); RefreshIncomeOverview(); PublishWebSnapshot(); }
    private void IncomeExpense_RecordsChanged(object? sender, EventArgs args)
    { RebuildIncomePeriods(); RefreshIncomeOverview(); PublishWebSnapshot(); }
    private IEnumerable<BossLootRecord> StoredLoot => (_settings.BossLootRecords ?? []).Where(BossLoot.Valid);
    private WeeklyIncomeForecast? ForecastFor(SchedulerCharacter character, DateOnly today)
    {
        if (character.History is not { } history) return null;
        var state = history.Today == today ? history.Current : SchedulerLastState.Project(new(history.Today, history.Current, false, DateTimeOffset.UtcNow), today);
        return WeeklyIncomeForecast.Calculate(state, history.Snapshots, character.Income?.Records ?? [], today,
            id => PartySize(character.Ocid, id), character.Character.Level);
    }
    private IncomeDisplay CurrentIncomeDisplay => new(_settings.IncomeDisplayMode, _settings.MesoCashRate);
    private DateOnly IncomeCumulativeStart(DateOnly today) => _settings.IncomeCumulativeStartDate is { } start
        ? start > today ? today : start : DateOnly.MinValue;
    private string IncomeSettingsStatus()
        => "현금 금액은 설정한 환산가로 계산한 값입니다.\n" + (_settings.IncomeCumulativeStartDate is { } start
            ? $"누적 수익과 집계 재생은 {start:yyyy.MM.dd}부터 계산합니다." : "누적 수익과 집계 재생은 저장된 전체 기록을 계산합니다.");
    private void InitializeIncomeDisplay()
    {
        IncomeDisplaySelector.SelectedIndex = CurrentIncomeDisplay.ValidMode switch { "cash" => 1, "both" => 2, "fragments" => 3, _ => 0 };
        IncomeCashRateInput.Value = (double)CurrentIncomeDisplay.Rate;
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        DateTimeOffset Offset(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        IncomeCumulativeStartDateInput.MinDate = Offset(new(2003, 4, 29));
        IncomeCumulativeStartDateInput.MaxDate = Offset(today);
        if (_settings.IncomeCumulativeStartDate is { } start)
            IncomeCumulativeStartDateInput.Date = Offset(start > today ? today : start);
        IncomeCurrencyStatus.Text = IncomeSettingsStatus();
        IncomeMonthSelector.ItemsSource = _incomeMonths;
        IncomeWeekSelector.ItemsSource = _incomeWeeks;
        IncomeLootRangeSelector.SelectedIndex = 0;
        RebuildIncomePeriods();
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
    private void IncomeCumulativeStartDateInput_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_incomeDisplayReady || _dataDeleting) return;
        _settings.IncomeCumulativeStartDate = sender.Date is { } value ? DateOnly.FromDateTime(value.DateTime) : null;
        SaveIncomeDisplay();
    }
    private void IncomeCumulativeAll_Click(object sender, RoutedEventArgs args)
    {
        if (!_incomeDisplayReady || _dataDeleting) return;
        _incomeDisplayReady = false;
        IncomeCumulativeStartDateInput.Date = null;
        _settings.IncomeCumulativeStartDate = null;
        _incomeDisplayReady = true;
        SaveIncomeDisplay();
    }
    private void SaveIncomeDisplay()
    {
        try { _settings.Save(); IncomeCurrencyStatus.Text = IncomeSettingsStatus(); }
        catch (Exception error) when (IsStorageError(error)) { IncomeCurrencyStatus.Text = "수익 설정을 저장하지 못했어요. 이번 실행에서만 적용합니다.\n" + IncomeSettingsStatus(); }
        foreach (var character in SchedulerAvatars) character.RefreshIncome(id => PartySize(character.Ocid, id));
        RefreshIncomeOverview();
        IncomeReplayPanel.SetDisplay(CurrentIncomeDisplay);
        PublishWebSnapshot();
    }
    private async void HuntingIncomeReplayOpen_Click(object sender, RoutedEventArgs args)
    {
        var owners = SchedulerAvatars.Where(owner => _incomeSelectedOcid is null || owner.Ocid == _incomeSelectedOcid).ToDictionary(owner => owner.Ocid);
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        var start = IncomeCumulativeStart(today);
        var records = (_settings.HuntingIncomeRecords ?? []).Where(row => owners.ContainsKey(row.Ocid) && row.Date >= start && row.Date <= today);
        await RevealIncomeReplayAsync();
        IncomeReplayPanel.SetRecords(HuntingIncome.Replay(records, ocid => owners[ocid].Name), CurrentIncomeDisplay,
            _incomeSelectedOcid is null ? "전체 캐릭터 사냥 기록" : owners.Values.FirstOrDefault()?.Name ?? "사냥 기록", _settings);
    }
    private async void IncomeReplayOpen_Click(object sender, RoutedEventArgs args)
    {
        var characters = SchedulerAvatars.Where(character => _incomeSelectedOcid is null || character.Ocid == _incomeSelectedOcid).ToArray();
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        var start = IncomeCumulativeStart(today);
        var records = characters.SelectMany(character => (character.Income?.Records ?? []).Where(clear => clear.Date >= start && clear.Date <= today)
            .Select(clear => (character.Ocid, Clear: clear))).ToArray();
        var ocids = characters.Select(character => character.Ocid).ToHashSet(StringComparer.Ordinal);
        await RevealIncomeReplayAsync();
        IncomeReplayPanel.SetRecords(new IncomeReplay(records, StoredLoot.Where(item => ocids.Contains(item.Ocid) && item.Date >= start && item.Date <= today)), CurrentIncomeDisplay, _incomeSelectedOcid is null ? "전체 캐릭터" : characters.FirstOrDefault()?.Name ?? "캐릭터", _settings);
    }

    private async Task RevealIncomeReplayAsync()
    {
        IncomeReplayPanel.ShowPreparing();
        IncomeReplayPanel.Visibility = Visibility.Visible;
        await Task.Yield();
        IncomeReplayPanel.UpdateLayout();
        IncomeReplayPanel.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0
        });
        await Task.Yield();
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

    private DateOnly EarliestIncomeDate(DateOnly today)
    {
        var dates = SchedulerAvatars.SelectMany(character => character.Income?.Records.Select(record => record.Date) ?? [])
            .Concat((_settings.BossLootRecords ?? []).Where(BossLoot.Valid).Select(record => record.Date))
            .Concat(HuntingIncome.Normalize(_settings.HuntingIncomeRecords ?? []).Select(record => record.Date))
            .Concat((_settings.IncomeExpenseRecords ?? []).Where(IncomeExpenses.Valid).Select(record => record.Date))
            .Append(today).ToArray();
        return dates.Min();
    }

    private void RebuildIncomePeriods()
    {
        if (IncomeMonthSelector is null || IncomeWeekSelector is null) return;
        _rebuildingIncomePeriods = true;
        try
        {
            var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
            var current = IncomeHistory.Week(today);
            if (_incomeSelectedWeekStart > current.Start) _incomeSelectedWeekStart = current.Start;
            var selectedWeek = IncomeHistory.Week(_incomeSelectedWeekStart);
            var selectedMonth = new DateOnly(selectedWeek.Anchor.Year, selectedWeek.Anchor.Month, 1);
            var first = new DateOnly(EarliestIncomeDate(today).Year, EarliestIncomeDate(today).Month, 1);
            _incomeMonths.Clear();
            for (var month = first; month <= new DateOnly(today.Year, today.Month, 1); month = month.AddMonths(1))
                _incomeMonths.Add(new(month, $"{month:yyyy년 M월}"));
            IncomeMonthSelector.SelectedItem = _incomeMonths.FirstOrDefault(choice => choice.Month == selectedMonth) ?? _incomeMonths.LastOrDefault();
            RebuildIncomeWeeks(selectedMonth, current.Start);
        }
        finally { _rebuildingIncomePeriods = false; }
    }

    private void RebuildIncomeWeeks(DateOnly month, DateOnly currentWeekStart)
    {
        _incomeWeeks.Clear();
        foreach (var week in IncomeHistory.Month(month).Where(week => week.Start <= currentWeekStart))
            _incomeWeeks.Add(new(week, $"{week.WeekOfMonth}주차 · {week.Start:MM.dd}~{week.End:MM.dd}"));
        var selected = _incomeWeeks.FirstOrDefault(choice => choice.Range.Start == _incomeSelectedWeekStart) ?? _incomeWeeks.LastOrDefault();
        if (selected is not null) { _incomeSelectedWeekStart = selected.Range.Start; IncomeWeekSelector.SelectedItem = selected; }
    }

    private void IncomeMonthSelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_rebuildingIncomePeriods || IncomeMonthSelector.SelectedItem is not IncomeMonthChoice month) return;
        _rebuildingIncomePeriods = true;
        try
        {
            var current = IncomeHistory.Week(SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow));
            RebuildIncomeWeeks(month.Month, current.Start);
        }
        finally { _rebuildingIncomePeriods = false; }
        RefreshIncomeOverview();
    }

    private void IncomeWeekSelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_rebuildingIncomePeriods || IncomeWeekSelector.SelectedItem is not IncomeWeekChoice week) return;
        _incomeSelectedWeekStart = week.Range.Start; RefreshIncomeOverview();
    }

    private void MoveIncomeWeek(int days)
    {
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        var current = IncomeHistory.Week(today).Start;
        var first = IncomeHistory.Week(EarliestIncomeDate(today)).Start;
        _incomeSelectedWeekStart = _incomeSelectedWeekStart.AddDays(days);
        if (_incomeSelectedWeekStart < first) _incomeSelectedWeekStart = first;
        if (_incomeSelectedWeekStart > current) _incomeSelectedWeekStart = current;
        RebuildIncomePeriods(); RefreshIncomeOverview();
    }

    private void IncomePreviousWeek_Click(object sender, RoutedEventArgs args) => MoveIncomeWeek(-7);
    private void IncomeNextWeek_Click(object sender, RoutedEventArgs args) => MoveIncomeWeek(7);
    private void IncomeCurrentWeek_Click(object sender, RoutedEventArgs args)
    {
        _incomeSelectedWeekStart = IncomeHistory.Week(SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow)).Start;
        RebuildIncomePeriods(); RefreshIncomeOverview();
    }

    private void IncomeLootRangeSelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        _incomeLootRange = Math.Max(0, IncomeLootRangeSelector.SelectedIndex);
        if (WeeklyIncomeAmount is not null) RefreshIncomeOverview();
    }

    private void IncomeLootSummary_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: IncomeLootSummaryRow row }) return;
        IncomeLootDetail.Text = $"{row.Name} · {row.CountText} · {row.Amount}\n{row.Detail}";
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
            foreach (var id in ids) BossParty.SetCurrentPreference(updated, record.Ocid, id, (int)number.Value, today);
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
        var currentWeek = IncomeHistory.Week(today);
        var selectedWeek = IncomeHistory.Week(_incomeSelectedWeekStart);
        var weekStart = selectedWeek.Start;
        var weekEnd = selectedWeek.End > today ? today : selectedWeek.End;
        var month = (IncomeMonthSelector?.SelectedItem as IncomeMonthChoice)?.Month
            ?? new DateOnly(selectedWeek.Anchor.Year, selectedWeek.Anchor.Month, 1);
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        if (monthEnd > today) monthEnd = today;
        var cumulativeStart = IncomeCumulativeStart(today);
        var selectedOcids = selected.Select(character => character.Ocid).ToHashSet(StringComparer.Ordinal);
        var loot = StoredLoot.Where(record => selectedOcids.Contains(record.Ocid)).ToArray();
        var hunting = HuntingIncome.Normalize(_settings.HuntingIncomeRecords ?? []).Where(row => selectedOcids.Contains(row.Ocid)).ToArray();
        var expenses = (_settings.IncomeExpenseRecords ?? []).Where(IncomeExpenses.Valid)
            .Where(row => _incomeSelectedOcid is null ? row.Ocid.Length == 0 || selectedOcids.Contains(row.Ocid) : selectedOcids.Contains(row.Ocid))
            .DistinctBy(row => row.Id).ToArray();
        var incomes = loaded.Select(character => character.Income!).ToArray();
        var weeklyBoss = BossIncomeBreakdowns.Calculate(incomes, loot, weekStart, weekEnd);
        var monthlyBoss = BossIncomeBreakdowns.Calculate(incomes, loot, monthStart, monthEnd);
        var totalBoss = BossIncomeBreakdowns.Calculate(incomes, loot, cumulativeStart, today);
        var bossWeekly = weeklyBoss.Total;
        var bossMonthly = monthlyBoss.Total;
        var bossTotal = totalBoss.Total;
        var huntWeekly = HuntingIncome.Sum(hunting, weekStart, weekEnd);
        var huntMonthly = HuntingIncome.Sum(hunting, monthStart, monthEnd);
        var huntTotal = HuntingIncome.Sum(hunting, cumulativeStart, today);
        var expenseWeekly = IncomeExpenses.Sum(expenses, weekStart, weekEnd);
        var expenseMonthly = IncomeExpenses.Sum(expenses, monthStart, monthEnd);
        var expenseTotal = IncomeExpenses.Sum(expenses, cumulativeStart, today);

        long Amount(long boss, long hunt, long expense) => _incomeMode switch
        {
            1 => boss,
            2 => hunt,
            3 => expense,
            _ => boss + hunt - expense
        };
        string Display(long boss, long hunt, long expense, DateOnly start, DateOnly end)
        {
            if (_incomeMode == 1) return CurrentIncomeDisplay.Format(boss);
            if (_incomeMode == 3) return CurrentIncomeDisplay.Format(expense);
            var rows = hunting.Where(row => row.Date >= start && row.Date <= end).ToArray();
            var meso = rows.Sum(row => row.Meso) + (_incomeMode == 0 ? boss - expense : 0);
            var fragments = rows.Sum(row => (long)row.Fragments);
            return CurrentIncomeDisplay.Hunting(meso, fragments, Amount(boss, hunt, expense));
        }
        WeeklyIncomeAmount.Text = Display(bossWeekly, huntWeekly, expenseWeekly, weekStart, weekEnd);
        MonthlyIncomeAmount.Text = Display(bossMonthly, huntMonthly, expenseMonthly, monthStart, monthEnd);
        TotalIncomeAmount.Text = Display(bossTotal, huntTotal, expenseTotal, cumulativeStart, today);
        WeeklyIncomeLabel.Text = selectedWeek.Label;
        MonthlyIncomeLabel.Text = $"{monthStart:yyyy년 M월}";
        TotalIncomeLabel.Text = _settings.IncomeCumulativeStartDate is { } ? $"누적 ({cumulativeStart:yy.MM.dd}~)" : "누적";
        BossCrystalWeeklyAmount.Text = CurrentIncomeDisplay.Format(weeklyBoss.Crystal);
        BossCrystalMonthlyAmount.Text = CurrentIncomeDisplay.Format(monthlyBoss.Crystal);
        BossCrystalTotalAmount.Text = CurrentIncomeDisplay.Format(totalBoss.Crystal);
        BossLootWeeklyAmount.Text = CurrentIncomeDisplay.Format(weeklyBoss.Loot);
        BossLootMonthlyAmount.Text = CurrentIncomeDisplay.Format(monthlyBoss.Loot);
        BossLootTotalAmount.Text = CurrentIncomeDisplay.Format(totalBoss.Loot);
        BossCombinedWeeklyAmount.Text = CurrentIncomeDisplay.Format(weeklyBoss.Total);
        BossCombinedMonthlyAmount.Text = CurrentIncomeDisplay.Format(monthlyBoss.Total);
        BossCombinedTotalAmount.Text = CurrentIncomeDisplay.Format(totalBoss.Total);
        IncomeHeading.Text = _incomeMode switch { 1 => "보스 수익", 2 => "사냥 수익", 3 => "지출", 4 => "수익 집계", _ => "전체 순수익" };
        IncomeBreakdown.Text = _incomeMode == 0
            ? $"선택 주차 · 보스 {CurrentIncomeDisplay.Format(bossWeekly)} + 사냥 {Display(0, huntWeekly, 0, weekStart, weekEnd)} - 지출 {CurrentIncomeDisplay.Format(expenseWeekly)}\n"
                + $"선택 월 · 보스 {CurrentIncomeDisplay.Format(bossMonthly)} + 사냥 {Display(0, huntMonthly, 0, monthStart, monthEnd)} - 지출 {CurrentIncomeDisplay.Format(expenseMonthly)}\n"
                + $"누적 · 보스 {CurrentIncomeDisplay.Format(bossTotal)} + 사냥 {Display(0, huntTotal, 0, cumulativeStart, today)} - 지출 {CurrentIncomeDisplay.Format(expenseTotal)}"
            : "";
        IncomeSummaryGrid.Visibility = _incomeMode == 4 ? Visibility.Collapsed : Visibility.Visible;
        IncomeBreakdown.Visibility = _incomeMode == 0 ? Visibility.Visible : Visibility.Collapsed;
        IncomeReplayDetails.Visibility = _incomeMode == 4 ? Visibility.Visible : Visibility.Collapsed;
        BossIncomeDetails.Visibility = _incomeMode is 2 or 3 or 4 ? Visibility.Collapsed : Visibility.Visible;
        BossIncomeSplitGrid.Visibility = _incomeMode == 1 ? Visibility.Visible : Visibility.Collapsed;
        BossLootSummaryPanel.Visibility = _incomeMode == 1 ? Visibility.Visible : Visibility.Collapsed;
        HuntingIncomePanel.Visibility = _incomeMode == 2 ? Visibility.Visible : Visibility.Collapsed;
        HuntingIncomePanel.SetContext(_settings, selected, _incomeSelectedOcid, CurrentIncomeDisplay);
        IncomeExpensePanel.Visibility = _incomeMode == 3 ? Visibility.Visible : Visibility.Collapsed;
        IncomeExpensePanel.SetContext(_settings, selected, _incomeSelectedOcid, CurrentIncomeDisplay);

        var lootStart = _incomeLootRange switch { 1 => monthStart, 2 => cumulativeStart, _ => weekStart };
        var lootEnd = _incomeLootRange switch { 1 => monthEnd, 2 => today, _ => weekEnd };
        IncomeLootRows.Clear();
        foreach (var row in loot.Where(record => record.Date >= lootStart && record.Date <= lootEnd)
            .GroupBy(record => record.ItemId.Length > 0 ? record.ItemId : record.ItemName, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.First().ItemName, StringComparer.CurrentCulture)
            .Select(group => new IncomeLootSummaryRow(group.OrderByDescending(record => record.Date).ToArray(), CurrentIncomeDisplay)))
            IncomeLootRows.Add(row);
        IncomeLootEmpty.Visibility = IncomeLootRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IncomeLootRows.Count == 0) IncomeLootDetail.Text = "";

        var forecasts = selected.Select(character => (Character: character, Forecast: ForecastFor(character, today))).ToArray();
        RemainingWeeklyIncomeAmount.Text = CurrentIncomeDisplay.Format(forecasts.Sum(item => item.Forecast?.Meso ?? 0));
        RemainingWeeklyIncomeStatus.Text = "등록된 미완료 주간 보스 · 설정한 파티 인원 · 캐릭터당 주간 12마리 제한 기준\n"
            + string.Join(" · ", forecasts.Select(item => item.Forecast is { } value
                ? $"{item.Character.Name}: {CurrentIncomeDisplay.Format(value.Meso)}{(value.Unpriced > 0 ? $" (가격 미확인 {value.Unpriced}종)" : "")}" : item.Character.Name + ": 스케줄러 조회 필요"));
        ToolTipService.SetToolTip(RemainingWeeklyIncomeAmount, string.Join("\n", forecasts.SelectMany(item => item.Forecast?.Bosses.Select(boss =>
            $"{item.Character.Name} · {boss.Name} · {boss.PartySize}인 · {(boss.Included ? boss.Meso is { } amount ? CurrentIncomeDisplay.Format(amount) : "가격 확인 필요" : "주간 제한에서 제외")}") ?? [])));
        RemainingWeeklyIncomePanel.Visibility = (_incomeMode is 0 or 1) && weekStart == currentWeek.Start ? Visibility.Visible : Visibility.Collapsed;
        IncomeScope.Text = $"{(_incomeSelectedOcid is not null ? selected.FirstOrDefault()?.Name : "추가한 캐릭터 합산")} · {loaded.Length} / {selected.Length}캐릭터 조회 · 결정 + 물욕템 수령액";
        IncomeOverviewStatus.Text = _incomeMode is 2 or 3 or 4 ? "" : loaded.Length < selected.Length
            ? "보스 기록을 아직 불러오지 못한 캐릭터가 있어요. 새로고침으로 다시 조회할 수 있습니다."
            : loaded.Any(character => character.Income!.Unpriced > 0
                || !character.Income.CompleteRange(BossCycle.Monthly) || character.Income.Today != today)
                ? "일부 날짜의 기록이나 결정 가격을 확인하지 못했어요. 캘린더의 날짜를 눌러 상세 내역을 확인할 수 있습니다." : "";
        WeeklyCrystalCount.Text = $"선택 주차 주간 보스 {loaded.Sum(character => character.Income!.Records.Count(record => record.Included && record.Cycle == BossCycle.Weekly && record.Date >= weekStart && record.Date <= weekEnd))}건";
        IncomeBulkAddButton.Visibility = _incomeMode is 2 or 3 or 4 ? Visibility.Collapsed : Visibility.Visible;
        IncomeCalendarPanel.Visibility = _incomeMode == 4 ? Visibility.Collapsed : Visibility.Visible;
        if (_incomeMode != 4)
        {
            var category = _incomeMode switch { 1 => IncomeCalendarCategory.Boss, 2 => IncomeCalendarCategory.Hunting, 3 => IncomeCalendarCategory.Expense, _ => IncomeCalendarCategory.All };
            IncomeCalendarPanel.SetCharacters(selected, CurrentIncomeDisplay, loot, hunting, category, expenses);
            IncomeCalendarPanel.ShowDate(selectedWeek.Anchor > today ? today : selectedWeek.Anchor);
        }
        IncomeOverview.Visibility = selected.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        IncomeEmpty.Visibility = _hasLoadedCharacters && SchedulerAvatars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
