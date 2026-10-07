using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
