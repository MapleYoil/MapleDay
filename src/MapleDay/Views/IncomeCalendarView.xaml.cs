using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay.Views;

public sealed partial class IncomeCalendarView : UserControl
{
    public ObservableCollection<IncomeCalendarDay> Days { get; } = [];
    public ObservableCollection<IncomeCalendarBoss> Bosses { get; } = [];
    private DateOnly _today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
    private DateOnly _selectedDate;
    private DateOnly _month;
    private CalendarBossRecord[] _records = [];
    private IReadOnlySet<DateOnly>[] _knownDates = [];
    private int _expectedCharacters;
    private IReadOnlyList<CalendarIncomeDay> _days = [];

    public IncomeCalendarView()
    {
        _selectedDate = _today;
        _month = FirstOfMonth(_today);
        InitializeComponent();
        Refresh();
    }

    public void SetCharacters(IEnumerable<SchedulerCharacter> characters)
    {
        var selected = characters.ToArray();
        _expectedCharacters = selected.Length;
        _records = selected.Where(character => character.Income is not null)
            .SelectMany(character => character.Income!.Records.Select(record => new CalendarBossRecord(character.Ocid, character.Name, record))).ToArray();
        _knownDates = selected.Where(character => character.Income is not null).Select(character => character.Income!.KnownDates).ToArray();
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        if (_selectedDate == _today && today != _today) { _selectedDate = today; _month = FirstOfMonth(today); }
        _today = today;
        Refresh();
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
    private void Refresh()
    {
        _days = IncomeCalendar.Month(_month, _records, _knownDates);
        MonthTitle.Text = $"{_month:yyyy년 M월}";
        PreviousButton.IsEnabled = _month > FirstOfMonth(SchedulerBossHistory.FirstDate);
        NextButton.IsEnabled = _month < FirstOfMonth(_today);
        Days.Clear();
        foreach (var day in _days) Days.Add(new(day, _selectedDate, _today, _expectedCharacters));
        MonthAmount.Text = $"이 달의 예상 수익 {BossIncome.Money(_days.Where(day => day.InMonth).Sum(day => day.Meso))}";
        var chosen = _days.FirstOrDefault(day => day.Date == _selectedDate);
        SelectedTitle.Text = $"{_selectedDate:M월 d일} · 잡은 보스";
        SelectedAmount.Text = chosen is { } selectedDay && (selectedDay.Bosses.Count > 0 || selectedDay.KnownCharacters > 0) ? IncomeCalendar.CompactMoney(selectedDay.Meso) : "—";
        Bosses.Clear();
        foreach (var record in chosen?.Bosses ?? []) Bosses.Add(new(record));
        DayStatus.Text = chosen is null || chosen.KnownCharacters == 0 && chosen.Bosses.Count == 0
            ? "저장된 조회 기록이 없어요."
            : chosen.Bosses.Count == 0 ? "이 날짜에 확인된 주간·월간 보스 클리어가 없어요."
            : $"보스 {chosen.Bosses.Count}마리 · 조회 기록 {chosen.KnownCharacters}/{_expectedCharacters}캐릭터";
    }
    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly date }) return;
        _selectedDate = date;
        _month = FirstOfMonth(date);
        Refresh();
    }
    private void MoveMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        _selectedDate = _month == FirstOfMonth(_today) ? _today : _month;
        if (_selectedDate < SchedulerBossHistory.FirstDate) _selectedDate = SchedulerBossHistory.FirstDate;
        Refresh();
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => MoveMonth(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => MoveMonth(1);
    private void Today_Click(object sender, RoutedEventArgs e) { _selectedDate = _today; _month = FirstOfMonth(_today); Refresh(); }
}
