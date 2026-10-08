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
        RefreshIncomeOverview();
    }

    private void IncomeOpenScheduler_Click(object sender, RoutedEventArgs e) => NavigateTo("scheduler");

    private void IncomeCalendar_PartySizeRequested(object? sender, IncomePartySizeRequest request)
    {
        var record = request.Record;
        if (_dataDeleting || !_schedulerCharacters.TryGetValue(record.Ocid, out var owner) || owner.History is not { } history) return;
        _partyFlyout?.Hide();
        var maximum = BossParty.Maximum(record.Boss.Name);
        var number = new NumberBox { Header = $"파티 인원 (1~{maximum}인)", Minimum = 1, Maximum = maximum,
            Value = record.Boss.PartySize, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var all = new CheckBox { Content = new TextBlock { Text = "첫 기록부터 같은 보스의 전체 기록에 적용", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 } };
        var first = history.Snapshots.Where(day => day.Date >= SchedulerBossHistory.FirstDate && day.Date <= history.Today)
            .Select(day => day.Date).DefaultIfEmpty(record.Boss.Date).Min();
        var range = new TextBlock { Text = $"{owner.Name} · {first:yyyy.MM.dd} ~ {history.Today:yyyy.MM.dd}\n체크하지 않으면 {record.Boss.Date:yyyy.MM.dd} 기록만 변경합니다.",
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
            var ids = all.IsChecked == true && owner.History is { } latest
                ? BossParty.SavedRecordIds(latest.Snapshots, record.Boss.Name, record.Boss.Cycle, latest.Today).Append(record.Boss.Id).Distinct().ToArray()
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
        WeeklyIncomeAmount.Text = BossIncome.Money(loaded.Sum(character => character.Income!.Sum(weekStart, today)));
        MonthlyIncomeAmount.Text = BossIncome.Money(loaded.Sum(character => character.Income!.Sum(monthStart, today)));
        TotalIncomeAmount.Text = BossIncome.Money(loaded.Sum(character => character.Income!.Total));
        IncomeScope.Text = $"{(_incomeSelectedOcid is not null ? selected.FirstOrDefault()?.Name : "추가한 캐릭터 합산")} · {loaded.Length} / {selected.Length}캐릭터 조회 · 예상 결정 수익";
        IncomeOverviewStatus.Text = loaded.Length < selected.Length
            ? "보스 기록을 아직 불러오지 못한 캐릭터가 있어요. 새로고침으로 다시 조회할 수 있습니다."
            : loaded.Any(character => character.Income!.Unpriced > 0
                || !character.Income.CompleteRange(BossCycle.Monthly) || character.Income.Today != today)
                ? "일부 날짜의 기록이나 결정 가격을 확인하지 못했어요. 캘린더의 날짜를 눌러 상세 내역을 확인할 수 있습니다." : "";
        WeeklyCrystalCount.Text = $"이번 주 주간 보스 {loaded.Sum(character => character.Income!.Records.Count(record => record.Included && record.Cycle == BossCycle.Weekly && record.PeriodStart == weekStart))} / {selected.Length * BossIncome.WeeklyCap} · 캐릭터당 최대 12마리 기준";
        IncomeCalendarPanel.SetCharacters(selected);
        IncomeOverview.Visibility = selected.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        IncomeEmpty.Visibility = _hasLoadedCharacters && SchedulerAvatars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
