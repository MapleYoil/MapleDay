using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay;

public sealed partial class MainWindow
{
    public ObservableCollection<LevelCharacter> LevelCharacters { get; } = [];
    public ObservableCollection<LevelHistoryRow> LevelHistoryRows { get; } = [];
    public ObservableCollection<LevelGainBar> LevelGainBars { get; } = [];
    private readonly ExperienceHistoryStore _experienceHistory = new();
    private CancellationTokenSource? _levelCts;
    private int _levelActiveLoads;
    private string? _levelSelectedOcid, _levelAccount;
    private string? _levelDetailOcid;
    private ExperienceTrend? _levelDetailTrend;
    private bool _levelReloadPending;
    private readonly DispatcherTimer _levelDayTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    private void InitializeLevels()
    {
        _levelDayTimer.Tick += async (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            if (_currentPage == "level" && LevelCharacters.Any(character => character.History is { } history
                && (history.Today != ExperienceHistory.Today(now) || history.FinalDate != ExperienceHistory.LatestFinalDate(now))))
                await LoadLevelsAsync();
        };
        _levelDayTimer.Start();
        Closed += (_, _) => { CancelLevels(); _levelDayTimer.Stop(); };
        RebuildLevelCharacters();
    }

    private void RebuildLevelCharacters()
    {
        if (LevelCharacterList is null) return;
        var sameAccount = _levelAccount == _apiKey;
        var old = sameAccount ? LevelCharacters.ToDictionary(character => character.Ocid) : [];
        var selected = SchedulerAvatars.Select(character => character.Character).ToArray();
        if (!sameAccount || LevelCharacters.Select(character => character.Ocid).ToHashSet().SetEquals(selected.Select(character => character.Ocid)) == false)
            CancelLevels();
        _levelAccount = _apiKey;
        foreach (var removed in LevelCharacters.Where(character => !sameAccount || !selected.Any(item => item.Ocid == character.Ocid))) removed.Dispose();
        LevelCharacters.Clear();
        foreach (var character in selected)
        {
            var row = old.GetValueOrDefault(character.Ocid) ?? new LevelCharacter(character);
            row.Rebind(character); LevelCharacters.Add(row);
        }
        if (!sameAccount || !LevelCharacters.Any(character => character.Ocid == _levelSelectedOcid))
            _levelSelectedOcid = LevelCharacters.FirstOrDefault()?.Ocid;
        foreach (var character in LevelCharacters) character.Select(character.Ocid == _levelSelectedOcid);
        LevelCharacterList.SelectedItem = LevelCharacters.FirstOrDefault(character => character.Selected);
        UpdateLevelPage();
        if (_currentPage == "level")
        {
            _levelReloadPending = _levelActiveLoads > 0 && _levelCts is null;
            _ = LoadLevelsAsync();
        }
    }

    private void UpdateLevelPage()
    {
        LevelLogin.Visibility = _hasLoadedCharacters ? Visibility.Collapsed : Visibility.Visible;
        LevelControls.Visibility = _hasLoadedCharacters ? Visibility.Visible : Visibility.Collapsed;
        LevelEmpty.Visibility = _hasLoadedCharacters && LevelCharacters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LevelRefreshButton.IsEnabled = _hasLoadedCharacters && LevelCharacters.Count > 0 && _levelCts is null && _levelActiveLoads == 0;
        LevelCancelButton.Visibility = _levelCts is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshLevelDetails();
    }

    private void LevelCharacterList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (LevelCharacterList.SelectedItem is not LevelCharacter chosen) return;
        _levelSelectedOcid = chosen.Ocid;
        foreach (var character in LevelCharacters) character.Select(character == chosen);
        RefreshLevelDetails();
    }
    private void RefreshLevelDetails()
    {
        var character = LevelCharacters.FirstOrDefault(character => character.Ocid == _levelSelectedOcid);
        LevelDetail.Visibility = character is null ? Visibility.Collapsed : Visibility.Visible;
        if (character is null)
        {
            LevelHistoryRows.Clear(); LevelGainBars.Clear();
            LevelGainLineChart.SetSamples([]);
            _levelDetailOcid = null; _levelDetailTrend = null;
            return;
        }
        LevelDetailTitle.Text = character.Name;
        LevelCurrent.Text = character.LevelText;
        LevelProgress.Value = character.Percent;
        LevelTodayAmount.Text = character.TodayAmount;
        LevelTodayCaption.Text = character.TodayCaption;
        LevelAverage.Text = character.Average;
        LevelAverageCaption.Text = character.AverageCaption;
        LevelEta.Text = character.LevelUp;
        LevelEtaDate.Text = character.LevelUpDate;
        if (_levelDetailOcid == character.Ocid && ReferenceEquals(_levelDetailTrend, character.Trend)) return;
        _levelDetailOcid = character.Ocid; _levelDetailTrend = character.Trend;
        LevelHistoryRows.Clear(); LevelGainBars.Clear();
        LevelGainLineChart.SetSamples([]);
        if (character.Trend is not { } trend) { LevelHistoryStatus.Text = "경험치 기록을 불러오는 중…"; return; }
        var visibleDays = trend.Days.Where(day => day.Value is not null).OrderByDescending(day => day.Date).ToArray();
        LevelHistoryStatus.Text = visibleDays.Length > 0 ? $"{visibleDays.Length}일 저장 · 최신 날짜부터 표시" : "표시할 경험치 기록이 없어요.";
        foreach (var day in visibleDays) LevelHistoryRows.Add(new(day));
        var recent = trend.Days.Where(day => day.Date >= trend.EndDate.AddDays(-6) && day.Date <= trend.EndDate).ToDictionary(day => day.Date);
        var maximum = recent.Values.Select(day => Math.Abs(day.Gained ?? 0)).DefaultIfEmpty(0).Max();
        for (var date = trend.EndDate.AddDays(-6); date <= trend.EndDate; date = date.AddDays(1))
            LevelGainBars.Add(new(recent.GetValueOrDefault(date), date, maximum));
        LevelGainLineChart.SetSamples(Enumerable.Range(0, 7).Select(index =>
        {
            var date = trend.EndDate.AddDays(-6 + index);
            var value = recent.GetValueOrDefault(date)?.Value;
            return new ExperienceChartSample(date, value?.Exp, value?.Level, value?.Percent);
        }).ToArray());
    }

    private async void LevelRefreshButton_Click(object sender, RoutedEventArgs args) => await LoadLevelsAsync(force: true);
    private void LevelCancelButton_Click(object sender, RoutedEventArgs args) => CancelLevels();
    private void CancelLevels()
    {
        _levelCts?.Cancel(); _levelCts = null; _levelReloadPending = false;
        foreach (var character in LevelCharacters.Where(character => character.Loading)) character.Failed("수집을 중단했어요. 저장된 기록은 유지됩니다.");
        if (LevelRefreshButton is not null) UpdateLevelPage();
    }
    private async Task LoadLevelsAsync(bool force = false)
    {
        if (_closed || _dataDeleting || !_hasLoadedCharacters || string.IsNullOrEmpty(_apiKey) || _levelCts is not null || _levelActiveLoads > 0) return;
        var now = DateTimeOffset.UtcNow;
        var targets = LevelCharacters.Where(character => force || character.UpdatedAt is null || character.History is not { } history
            || history.Today != ExperienceHistory.Today(now) || history.FinalDate != ExperienceHistory.LatestFinalDate(now)
            || now - character.UpdatedAt > TimeSpan.FromMinutes(1)).ToArray();
        if (targets.Length == 0) return;
        var cts = new CancellationTokenSource(); _levelCts = cts; _levelActiveLoads++;
        var key = _apiKey;
        foreach (var character in targets) character.Begin();
        UpdateLevelPage();
        try
        {
            await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token }, async (character, token) =>
            {
                var progress = new Progress<ExperienceHistoryResult>(result => DispatcherQueue.TryEnqueue(() =>
                {
                    if (!Current(character) || !character.Loading) return;
                    character.Apply(result, done: false);
                    if (character.Ocid == _levelSelectedOcid) RefreshLevelDetails();
                }));
                try
                {
                    var result = await new ExperienceHistoryLoader(_api, _experienceHistory).LoadAsync(character.Ocid, key, now, token, progress);
                    token.ThrowIfCancellationRequested();
                    await OnUiAsync(() =>
                    {
                        if (!Current(character)) return;
                        character.Apply(result, done: true);
                        if (character.Ocid == _levelSelectedOcid) RefreshLevelDetails();
                    });
                }
                catch (Exception error) when (error is NexonApiException or HttpRequestException or IOException or UnauthorizedAccessException
                    || error is OperationCanceledException && !token.IsCancellationRequested)
                {
                    await OnUiAsync(() =>
                    {
                        if (!Current(character)) return;
                        character.Failed(error is NexonApiException apiError ? apiError.Message : "경험치 기록을 불러오지 못했어요. 새로고침으로 다시 조회해주세요.");
                        if (character.Ocid == _levelSelectedOcid) RefreshLevelDetails();
                    });
                }
            });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested || _closed) { }
        finally
        {
            _levelActiveLoads--;
            if (_levelCts == cts) _levelCts = null;
            if (!_closed) UpdateLevelPage();
            cts.Dispose();
            if (_levelReloadPending && !_closed && !_dataDeleting)
            { _levelReloadPending = false; _ = LoadLevelsAsync(); }
        }
        bool Current(LevelCharacter character) => !_closed && !_dataDeleting && _levelCts == cts && _apiKey == key && LevelCharacters.Contains(character);
    }
}
