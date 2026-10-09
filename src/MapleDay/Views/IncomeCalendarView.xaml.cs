using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay.Views;

public sealed record IncomePartySizeRequest(CalendarBossRecord Record, FrameworkElement Anchor);

public sealed partial class IncomeCalendarView : UserControl
{
    public event EventHandler<IncomePartySizeRequest>? PartySizeRequested;
    public event EventHandler<DateOnly>? BossAddRequested;
    public event EventHandler<CalendarBossRecord>? LootRequested;
    public event EventHandler<CalendarBossRecord>? BossEditRequested;
    public void ShowActionStatus(string message) => ActionStatus.Text = message;
    public ObservableCollection<IncomeCalendarDay> Days { get; } = [];
    public ObservableCollection<IncomeCalendarBoss> Bosses { get; } = [];
    public ObservableCollection<BossLootRow> Loot { get; } = [];
    private CalendarLootRecord[] _loot = [];
    private DateOnly _today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
    private DateOnly _selectedDate;
    private DateOnly _month;
    private CalendarBossRecord[] _records = [];
    private IReadOnlySet<DateOnly>[] _knownDates = [];
    private int _expectedCharacters;
    private IReadOnlyList<CalendarIncomeDay> _days = [];
    private IncomeDisplay _display = new();

    public IncomeCalendarView()
    {
        _selectedDate = _today;
        _month = FirstOfMonth(_today);
        InitializeComponent();
        Refresh();
    }

    public void SetCharacters(IEnumerable<SchedulerCharacter> characters, IncomeDisplay? display = null, IEnumerable<BossLootRecord>? loot = null)
    {
        _display = display ?? new();
        var selected = characters.ToArray();
        _expectedCharacters = selected.Length;
        _loot = (loot ?? []).Where(record => selected.Any(owner => owner.Ocid == record.Ocid) && BossLoot.Valid(record))
            .Select(record => new CalendarLootRecord(selected.First(owner => owner.Ocid == record.Ocid).Name, record)).ToArray();
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
        _days = IncomeCalendar.Month(_month, _records, _knownDates, _loot);
        MonthTitle.Text = $"{_month:yyyy년 M월}";
        PreviousButton.IsEnabled = _month > FirstOfMonth(_loot.Select(item => item.Loot.Date).Append(SchedulerBossHistory.FirstDate).Min());
        NextButton.IsEnabled = _month < FirstOfMonth(_today);
        Days.Clear();
        foreach (var day in _days) Days.Add(new(day, _selectedDate, _today, _expectedCharacters, _display));
        MonthAmount.Text = $"이 달의 결정·물욕템 수익 {_display.Format(_days.Where(day => day.InMonth).Sum(day => day.Meso))}";
        var chosen = _days.FirstOrDefault(day => day.Date == _selectedDate);
        SelectedTitle.Text = $"{_selectedDate:M월 d일} · 보스·물욕템 기록";
        SelectedAmount.Text = chosen is { } selectedDay && (selectedDay.Bosses.Count > 0 || selectedDay.KnownCharacters > 0 || selectedDay.Loot?.Count > 0) ? _display.Format(selectedDay.Meso) : "—";
        Bosses.Clear();
        foreach (var record in chosen?.Bosses ?? []) Bosses.Add(new(record, _display, _loot.Select(item => item.Loot)));
        Loot.Clear();
        foreach (var item in chosen?.Loot ?? [])
            if (!(chosen?.Bosses ?? []).Any(record => BossLoot.ForClear(item.Loot, record.Ocid, record.Boss)))
                Loot.Add(new(item.Loot, item.CharacterName, _display, _records.Any(record => BossLoot.ForClear(item.Loot, record.Ocid, record.Boss))));
        AddBossButton.IsEnabled = _expectedCharacters > 0 && _selectedDate >= SchedulerBossHistory.FirstDate && _selectedDate <= _today;
        ActionStatus.Text = "";
        var receipts = chosen?.Loot?.Count ?? 0;
        DayStatus.Text = chosen is null || chosen.KnownCharacters == 0 && chosen.Bosses.Count == 0 && receipts == 0
            ? "저장된 조회 기록이 없어요."
            : chosen.Bosses.Count == 0 && receipts == 0 ? "이 날짜에 확인된 주간·월간 보스 클리어가 없어요."
            : $"보스 {chosen.Bosses.Count}마리 · 물욕템 {receipts}건 · 조회 기록 {chosen.KnownCharacters}/{_expectedCharacters}캐릭터";
    }
    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateOnly date }) return;
        _selectedDate = date;
        _month = FirstOfMonth(date);
        Refresh();
    }
    private void PartySize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: IncomeCalendarBoss boss } anchor)
            PartySizeRequested?.Invoke(this, new(boss.Record, anchor));
    }
    private void AddBoss_Click(object sender, RoutedEventArgs e) => BossAddRequested?.Invoke(this, _selectedDate);
    private void EditBoss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: IncomeCalendarBoss boss }) BossEditRequested?.Invoke(this, boss.Record);
    }
    private void Loot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: IncomeCalendarBoss boss }) LootRequested?.Invoke(this, boss.Record);
        else if (sender is Button { Tag: BossLootRow loot })
        {
            var clear = _records.FirstOrDefault(record => BossLoot.ForClear(loot.Record, record.Ocid, record.Boss));
            if (clear is not null) LootRequested?.Invoke(this, clear);
        }
    }
    private void MoveMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        _selectedDate = _month == FirstOfMonth(_today) ? _today : _month;
        var first = _loot.Select(item => item.Loot.Date).Append(SchedulerBossHistory.FirstDate).Min();
        if (_selectedDate < first) _selectedDate = first;
        Refresh();
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => MoveMonth(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => MoveMonth(1);
    private void Today_Click(object sender, RoutedEventArgs e) { _selectedDate = _today; _month = FirstOfMonth(_today); Refresh(); }
}
