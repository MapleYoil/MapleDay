using System.Collections.ObjectModel;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MapleDay.Views;

public sealed record ScheduleCharacter(string Ocid, string Name, string Label);
public sealed partial class ScheduleCalendarView : UserControl
{
    public ObservableCollection<ScheduleDay> Days { get; } = [];
    public ObservableCollection<ScheduleRow> Rows { get; } = [];
    public event EventHandler? NoticeRefreshRequested;
    public event EventHandler<DateOnly>? SundaySelected;
    public DateOnly SelectedDate => _selected;
    public bool ShowingSunday => _settings?.CalendarHideSunday != true && Filter != ScheduleFilter.Boss && _selected.DayOfWeek == DayOfWeek.Sunday;
    private AppSettings? _settings;
    private ScheduleCharacter[] _characters = [];
    private DateOnly _today = ScheduleCalendar.KoreanDate(DateTimeOffset.UtcNow);
    private DateOnly _selected;
    private DateOnly _month;
    private bool _dialogOpen;
    private bool _syncSundayVisibility;
    private DateOnly? _imageDate;
    private IReadOnlyList<ImageSource> _sundayImages = [];
    private ScheduleFilter Filter => (ScheduleFilter)Math.Max(0, FilterTabs.SelectedIndex);
    public ScheduleCalendarView()
    {
        _selected = _today; _month = new(_today.Year, _today.Month, 1);
        InitializeComponent(); Refresh();
    }
    public void SetContext(AppSettings settings, IEnumerable<SchedulerCharacter> characters)
    {
        _settings = settings;
        _syncSundayVisibility = true;
        try { HideSundayCheckBox.IsChecked = settings.CalendarHideSunday; }
        finally { _syncSundayVisibility = false; }
        _characters = characters.Select(item => new ScheduleCharacter(item.Ocid, item.Name, item.Name + " · " + item.Character.World)).ToArray();
        var today = ScheduleCalendar.KoreanDate(DateTimeOffset.UtcNow);
        if (today != _today && _selected == _today) { _selected = today; _month = new(today.Year, today.Month, 1); }
        _today = today; Refresh();
    }
    public void SetNoticeStatus(string text) => NoticeStatus.Text = text;
    public void SetSundayImages(DateOnly date, IReadOnlyList<ImageSource> images)
    { _imageDate = date; _sundayImages = images; Refresh(); }
    private void Refresh()
    {
        if (MonthTitle is null) return;
        var month = ScheduleCalendar.Month(_month, _settings?.CalendarSchedules ?? [],
            (_settings?.SundayNotices ?? []).Where(item => item.End >= _today.AddMonths(-6)), Filter, includeSunday: _settings?.CalendarHideSunday != true);
        NoticeStatus.Visibility = NoticeRefreshButton.Visibility = _settings?.CalendarHideSunday == true ? Visibility.Collapsed : Visibility.Visible;
        MonthTitle.Text = $"{_month:yyyy년 M월}";
        PreviousButton.IsEnabled = _month.Year > 2003 || _month.Month > 5;
        NextButton.IsEnabled = _month.Year < 9998;
        Days.Clear(); foreach (var day in month) Days.Add(new(day, _selected, _today));
        SelectedTitle.Text = $"{_selected:M월 d일} 일정";
        Rows.Clear(); foreach (var item in (month.FirstOrDefault(day => day.Date == _selected)?.Items ?? []).OrderBy(item => item.IsSunday))
            Rows.Add(new(item, _today, item.IsSunday && _imageDate == _selected ? _sundayImages : null));
        EmptyStatus.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RequestSunday() { if (_settings is not null && ShowingSunday) SundaySelected?.Invoke(this, _selected); }
    private void HideSunday_Changed(object sender, RoutedEventArgs args)
    {
        if (_syncSundayVisibility || _settings is null) return;
        var previous = _settings.CalendarHideSunday;
        _settings.CalendarHideSunday = HideSundayCheckBox.IsChecked == true;
        try { _settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _settings.CalendarHideSunday = previous;
            _syncSundayVisibility = true;
            try { HideSundayCheckBox.IsChecked = previous; }
            finally { _syncSundayVisibility = false; }
            ActionStatus.Text = "썬데이 표시 설정을 저장하지 못했어요. 데이터 폴더 권한을 확인해주세요.";
            Refresh(); return;
        }
        ActionStatus.Text = _settings.CalendarHideSunday ? "썬데이를 숨겼어요." : "썬데이를 표시합니다.";
        Refresh(); RequestSunday();
    }
    private void Filter_Changed(object sender, SelectionChangedEventArgs args) { Refresh(); RequestSunday(); }
    private void DayGrid_SizeChanged(object sender, SizeChangedEventArgs args)
    { if (DayLayout is not null) DayLayout.MinItemWidth = Math.Max(44, Math.Floor(args.NewSize.Width / 7)); }
    private void Day_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: DateOnly date }) return;
        _selected = date; _month = new(date.Year, date.Month, 1); ActionStatus.Text = ""; Refresh();
        RequestSunday();
    }
    private void MoveMonth(int delta)
    { _month = _month.AddMonths(delta); _selected = new(_month.Year, _month.Month, Math.Min(_selected.Day, DateTime.DaysInMonth(_month.Year, _month.Month))); Refresh(); RequestSunday(); }
    private void Previous_Click(object sender, RoutedEventArgs args) => MoveMonth(-1);
    private void Next_Click(object sender, RoutedEventArgs args) => MoveMonth(1);
    private void Today_Click(object sender, RoutedEventArgs args)
    { _selected = ScheduleCalendar.KoreanDate(DateTimeOffset.UtcNow); _today = _selected; _month = new(_selected.Year, _selected.Month, 1); Refresh(); RequestSunday(); }
    private void RefreshNotices_Click(object sender, RoutedEventArgs args) => NoticeRefreshRequested?.Invoke(this, EventArgs.Empty);
    private void NoticeImage_Failed(object sender, ExceptionRoutedEventArgs args)
    { NoticeStatus.Text = "공지 이미지를 불러오지 못했어요. 인터넷 연결을 확인하거나 공식 공지를 열어주세요."; }
    private async void SundayImage_Click(object sender, RoutedEventArgs args)
    {
        if (_dialogOpen || sender is not Button { Content: Image { Source: { } source } }) return;
        _dialogOpen = true;
        try
        {
            await new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "썬데이 메이플 혜택", CloseButtonText = "닫기",
                Content = new ScrollViewer { MaxHeight = Math.Max(180, (XamlRoot?.Size.Height ?? 800) - 240),
                    Content = new Image { Source = source, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch } } }.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }
    private bool Save(List<CalendarSchedule> items)
    {
        if (_settings is null) return false;
        var previous = _settings.CalendarSchedules; _settings.CalendarSchedules = items;
        try { _settings.Save(); Refresh(); ActionStatus.Text = "저장했습니다."; return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { _settings.CalendarSchedules = previous; ActionStatus.Text = "일정을 저장하지 못했어요. 데이터 폴더 권한을 확인해주세요."; return false; }
    }
    private async void Add_Click(object sender, RoutedEventArgs args) => await EditAsync(null);
    private async void Edit_Click(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: ScheduleRow row } && row.Occurrence.Schedule is { } item) await EditAsync(item); }
    private void Complete_Click(object sender, RoutedEventArgs args)
    {
        if (_settings is null || sender is not Button { Tag: ScheduleRow row } || row.Occurrence.Schedule is not { } item) return;
        var done = new HashSet<DateOnly>(item.CompletedDates);
        if (!done.Add(row.Occurrence.Date)) done.Remove(row.Occurrence.Date);
        Save(_settings.CalendarSchedules.Select(value => value.Id == item.Id ? item with { CompletedDates = done } : value).ToList());
    }
    private async void Delete_Click(object sender, RoutedEventArgs args)
    {
        if (_dialogOpen || _settings is null || sender is not Button { Tag: ScheduleRow row } || row.Occurrence.Schedule is not { } item) return;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "일정 삭제",
                Content = item.Weekly ? $"‘{item.Title}’의 모든 반복 일정을 삭제할까요?" : $"‘{item.Title}’ 일정을 삭제할까요?",
                PrimaryButtonText = "삭제", CloseButtonText = "취소", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                Save(_settings.CalendarSchedules.Where(value => value.Id != item.Id).ToList());
        }
        finally { _dialogOpen = false; }
    }
    private static DateTimeOffset DateValue(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
    private async Task EditAsync(CalendarSchedule? existing)
    {
        if (_dialogOpen || _settings is null) return;
        _dialogOpen = true;
        try
        {
            var kind = new ComboBox { Header = "일정 종류", ItemsSource = new[] { "일반 일정", "보스 일정" }, SelectedIndex = existing is null ? Filter == ScheduleFilter.Boss ? 1 : 0 : (int)existing.Kind, HorizontalAlignment = HorizontalAlignment.Stretch };
            var title = new TextBox { Header = "제목", MaxLength = 100, Text = existing?.Title ?? "" };
            var date = new CalendarDatePicker { Header = "날짜", Date = DateValue(existing?.Date ?? _selected), MinDate = DateValue(new(2003, 5, 1)) };
            var allDay = new CheckBox { Content = "하루 종일", IsChecked = existing?.Time is null };
            var time = new TimePicker { Header = "시간", ClockIdentifier = "24HourClock", Time = (existing?.Time ?? new TimeOnly(20, 0)).ToTimeSpan() };
            time.Visibility = allDay.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            allDay.Checked += (_, _) => time.Visibility = Visibility.Collapsed;
            allDay.Unchecked += (_, _) => time.Visibility = Visibility.Visible;
            var choices = new[] { new ScheduleCharacter("", "", "캐릭터 선택 안 함") }.Concat(_characters).ToList();
            if (existing is { Ocid.Length: > 0 } && choices.All(item => item.Ocid != existing.Ocid))
                choices.Add(new(existing.Ocid, existing.CharacterName, existing.CharacterName));
            var character = new ComboBox { Header = "캐릭터", ItemsSource = choices, DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch };
            character.SelectedItem = choices.FirstOrDefault(item => item.Ocid == existing?.Ocid)
                ?? (kind.SelectedIndex == 1 ? choices.FirstOrDefault(item => item.Ocid.Length > 0) : null) ?? choices[0];
            var bossNames = BossRecordChoices.Single.Select(item => item.Name).Distinct().ToArray();
            var boss = new ComboBox { Header = "보스", ItemsSource = bossNames, HorizontalAlignment = HorizontalAlignment.Stretch };
            var difficulty = new ComboBox { Header = "난이도", DisplayMemberPath = "DifficultyLabel", HorizontalAlignment = HorizontalAlignment.Stretch };
            var party = new NumberBox { Header = "인원 (인)", Minimum = 1, Maximum = 6, Value = existing?.PartySize ?? 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            void SetPartyLimit()
            {
                party.Maximum = BossParty.Maximum(boss.SelectedItem as string ?? existing?.Boss ?? "");
                party.Value = Math.Clamp(double.IsFinite(party.Value) ? Math.Floor(party.Value) : 1, 1, party.Maximum);
            }
            void SetDifficulties()
            {
                var selected = difficulty.SelectedItem as BossRecordChoice;
                var modes = BossRecordChoices.Single.Where(item => item.Name == boss.SelectedItem as string).ToArray();
                difficulty.ItemsSource = modes;
                difficulty.SelectedItem = modes.FirstOrDefault(item => item.Difficulty == (selected?.Difficulty ?? existing?.Difficulty)) ?? modes.FirstOrDefault();
                difficulty.PlaceholderText = existing is { Kind: ScheduleKind.Boss } && modes.All(item => item.Difficulty != existing.Difficulty) && boss.SelectedItem as string == existing.Boss
                    ? $"기존: {BossLootCatalog.DifficultyLabel(existing.Difficulty)}" : "난이도를 선택해주세요";
                // Keep an older, lower-difficulty schedule until the user chooses another difficulty.
                if (selected is null && existing is { Kind: ScheduleKind.Boss } && boss.SelectedItem as string == existing.Boss
                    && modes.All(item => item.Difficulty != existing.Difficulty)) difficulty.SelectedItem = null;
                SetPartyLimit();
            }
            boss.SelectionChanged += (_, _) => SetDifficulties();
            boss.SelectedItem = existing is { Kind: ScheduleKind.Boss }
                ? bossNames.FirstOrDefault(item => item == existing.Boss) : bossNames.FirstOrDefault();
            boss.PlaceholderText = existing is { Kind: ScheduleKind.Boss } && boss.SelectedItem is null ? $"기존: {existing.Boss}" : "보스를 선택해주세요";
            SetPartyLimit();
            var bossFields = new StackPanel { Spacing = 10 };
            bossFields.Children.Add(character); bossFields.Children.Add(boss); bossFields.Children.Add(difficulty);
            bossFields.Children.Add(new TextBlock { Text = "하드 스우 이상 체력 · 체력이 낮은 순서", TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Brush("MutedBrush"), FontSize = 12 });
            bossFields.Children.Add(party);
            bossFields.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            kind.SelectionChanged += (_, _) =>
            {
                bossFields.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
                if (kind.SelectedIndex == 1 && character.SelectedItem is ScheduleCharacter { Ocid.Length: 0 })
                    character.SelectedItem = choices.FirstOrDefault(item => item.Ocid.Length > 0) ?? choices[0];
            };
            var weekly = new CheckBox { Content = "매주 같은 요일에 반복", IsChecked = existing?.Weekly ?? false };
            var until = new CalendarDatePicker { Header = "반복 종료일 (선택하지 않으면 계속 반복)", Date = existing?.RepeatUntil is { } end ? DateValue(end) : null };
            until.Visibility = weekly.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            weekly.Checked += (_, _) => until.Visibility = Visibility.Visible;
            weekly.Unchecked += (_, _) => until.Visibility = Visibility.Collapsed;
            var note = new TextBox { Header = "메모", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 2000, Text = existing?.Note ?? "", MinHeight = 80 };
            var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Brush("DangerTextBrush") };
            var form = new StackPanel { Spacing = 12 };
            foreach (var field in new FrameworkElement[] { kind, title, date, allDay, time, bossFields, weekly, until, note, validation }) form.Children.Add(field);
            if (existing?.Weekly == true) form.Children.Insert(0, new TextBlock { Text = "수정한 내용은 이 반복 일정 전체에 적용합니다.", TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = existing is null ? "일정 추가" : "일정 수정",
                PrimaryButtonText = "저장", CloseButtonText = "취소", DefaultButton = ContentDialogButton.Primary,
                Content = new ScrollViewer { Content = form, MaxHeight = Math.Max(180, (XamlRoot?.Size.Height ?? 800) - 240), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (date.Date is null) { validation.Text = "날짜를 선택해주세요."; args.Cancel = true; return; }
                var owner = kind.SelectedIndex == 1 ? character.SelectedItem as ScheduleCharacter : null;
                if (kind.SelectedIndex == 1 && string.IsNullOrEmpty(owner?.Ocid) && existing?.Kind != ScheduleKind.Boss)
                { validation.Text = "보스 일정을 표시할 캐릭터를 선택해주세요. 스케줄러에 등록한 캐릭터를 선택할 수 있어요."; args.Cancel = true; return; }
                var selectedMode = difficulty.SelectedItem as BossRecordChoice;
                var bossName = kind.SelectedIndex == 1 ? boss.SelectedItem as string ?? existing?.Boss ?? "" : "";
                var mode = kind.SelectedIndex == 1 ? selectedMode?.Difficulty ?? (bossName == existing?.Boss ? existing.Difficulty : "") : "";
                if (kind.SelectedIndex == 1 && (!double.IsFinite(party.Value) || party.Value != Math.Floor(party.Value)
                    || party.Value < 1 || party.Value > BossParty.Maximum(bossName)))
                { validation.Text = $"인원은 1~{BossParty.Maximum(bossName)}인의 정수로 입력해주세요."; args.Cancel = true; return; }
                var label = string.IsNullOrWhiteSpace(title.Text) && kind.SelectedIndex == 1 ? $"{BossLootCatalog.DifficultyLabel(mode)} {bossName}" : title.Text.Trim();
                var item = new CalendarSchedule(existing?.Id ?? Guid.NewGuid().ToString("N"), (ScheduleKind)kind.SelectedIndex, label,
                    DateOnly.FromDateTime(date.Date.Value.DateTime), allDay.IsChecked == true ? null : TimeOnly.FromTimeSpan(time.Time), note.Text.Trim(),
                    owner?.Ocid ?? "", owner?.Name ?? "", bossName, mode, weekly.IsChecked == true,
                    weekly.IsChecked == true && until.Date is { } end ? DateOnly.FromDateTime(end.DateTime) : null,
                    kind.SelectedIndex == 1 ? (int)party.Value : 1)
                    { CompletedDates = existing?.CompletedDates ?? [] };
                if (!ScheduleCalendar.Valid(item)) { validation.Text = "제목·날짜·보스 정보를 확인해주세요. 반복 종료일은 시작일 이후여야 합니다."; args.Cancel = true; return; }
                if (!Save(_settings.CalendarSchedules.Where(value => value.Id != item.Id).Append(item).ToList()))
                { validation.Text = ActionStatus.Text; args.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }
}
