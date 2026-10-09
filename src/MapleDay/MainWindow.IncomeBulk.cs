using MapleDay.Core;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace MapleDay;

public sealed partial class MainWindow
{
    private readonly List<ManualWeeklyClear> _lastBulkAdded = [];
    private bool _bulkDialogOpen;
    private async void IncomeBulkAdd_Click(object sender, RoutedEventArgs args)
    {
        if (_closed || _dataDeleting || _bulkDialogOpen || SchedulerAvatars.Count == 0) return;
        _bulkDialogOpen = true;
        try
        {
            var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
            DateTimeOffset Offset(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
            var start = new CalendarDatePicker { Header = "시작일", MinDate = Offset(SchedulerBossHistory.FirstDate), MaxDate = Offset(today),
                Date = Offset(today.AddDays(-28) < SchedulerBossHistory.FirstDate ? SchedulerBossHistory.FirstDate : today.AddDays(-28)) };
            var end = new CalendarDatePicker { Header = "종료일", MinDate = start.MinDate, MaxDate = start.MaxDate, Date = Offset(today) };
            var weekday = new ComboBox { Header = "클리어 요일", SelectedIndex = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var label in new[] { "일요일", "월요일", "화요일", "수요일", "목요일 (기본)", "금요일", "토요일" }) weekday.Items.Add(label);
            var characters = new List<(SchedulerCharacter Owner, CheckBox Check)>();
            var characterList = new StackPanel { Spacing = 4 };
            foreach (var owner in SchedulerAvatars)
            {
                var check = new CheckBox { Content = owner.Name + " · " + owner.Character.World,
                    IsChecked = _incomeSelectedOcid is null || _incomeSelectedOcid == owner.Ocid };
                characterList.Children.Add(check); characters.Add((owner, check));
            }
            var bosses = new List<(string Name, CheckBox Check, ComboBox Difficulty, NumberBox Party)>();
            var bossList = new StackPanel { Spacing = 10 };
            foreach (var group in ManualWeeklyHistory.Choices.GroupBy(price => price.Name).OrderBy(group => group.Key))
            {
                var check = new CheckBox { Content = group.Key, MinWidth = 160 };
                var difficulty = new ComboBox { MinWidth = 100, IsEnabled = false };
                foreach (var price in group.OrderBy(price => DifficultyOrder(price.Difficulty)))
                    difficulty.Items.Add(new ComboBoxItem { Content = DifficultyLabel(price.Difficulty), Tag = price.Difficulty });
                difficulty.SelectedIndex = 0;
                AutomationProperties.SetName(difficulty, group.Key + " 난이도");
                var party = new NumberBox { Value = 1, Minimum = 1, Maximum = BossParty.Maximum(group.Key), Width = 100,
                    IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                AutomationProperties.SetName(party, group.Key + " 파티 인원");
                var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                options.Children.Add(difficulty); options.Children.Add(party); options.Children.Add(new TextBlock { Text = "인", VerticalAlignment = VerticalAlignment.Center });
                var row = new StackPanel { Spacing = 4 }; row.Children.Add(check); row.Children.Add(options);
                bossList.Children.Add(row); bosses.Add((group.Key, check, difficulty, party));
            }
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var content = new StackPanel { Spacing = 12, MinWidth = 380 };
            var range = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; range.Children.Add(start); range.Children.Add(end);
            content.Children.Add(range); content.Children.Add(weekday);
            content.Children.Add(new TextBlock { Text = "캐릭터", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            content.Children.Add(new ScrollViewer { Content = characterList, MaxHeight = 110 });
            content.Children.Add(new TextBlock { Text = "주간 보스 · 난이도 · 파티 인원", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            content.Children.Add(new ScrollViewer { Content = bossList, MaxHeight = 220 });
            content.Children.Add(new TextBlock { Text = "2026.06.25 이후의 수익 기록을 수동 추가합니다. 기존 기록은 유지하며 수익은 캐릭터당 주간 최대 12마리입니다.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            content.Children.Add(preview);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "주간 보스 기록 일괄 추가",
                Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = "일괄 추가", CloseButtonText = "취소", IsPrimaryButtonEnabled = false };
            List<(ManualWeeklyClear Clear, int Party)> pending = [];
            void UpdatePreview()
            {
                pending.Clear(); dialog.IsPrimaryButtonEnabled = false;
                if (start.Date is not { } from || end.Date is not { } through) { preview.Text = "시작일과 종료일을 선택하세요."; return; }
                IReadOnlyList<DateOnly> dates;
                try { dates = ManualWeeklyHistory.Dates(DateOnly.FromDateTime(from.DateTime), DateOnly.FromDateTime(through.DateTime), today,
                    weekday.SelectedIndex < 0 ? null : (DayOfWeek)weekday.SelectedIndex); }
                catch (ArgumentException error) { preview.Text = error.Message; return; }
                var targets = characters.Where(item => item.Check.IsChecked == true).ToArray();
                var selected = bosses.Where(item => item.Check.IsChecked == true).ToArray();
                if (targets.Length == 0 || selected.Length == 0) { preview.Text = "캐릭터와 보스를 선택하세요."; return; }
                if (selected.Any(item => !double.IsFinite(item.Party.Value) || item.Party.Value != Math.Truncate(item.Party.Value)
                    || item.Party.Value < 1 || item.Party.Value > BossParty.Maximum(item.Name))) { preview.Text = "파티 인원을 확인하세요."; return; }
                var existing = _settings.ManualWeeklyClears.Select(ManualWeeklyHistory.Key).ToHashSet();
                foreach (var target in targets)
                    foreach (var record in target.Owner.Income?.Records ?? []) existing.Add(target.Owner.Ocid + "|" + record.Id);
                var duplicates = 0; var unpriced = 0;
                foreach (var date in dates)
                    foreach (var target in targets)
                        foreach (var boss in selected)
                        {
                            var clear = new ManualWeeklyClear(target.Owner.Ocid, boss.Name, ((ComboBoxItem)boss.Difficulty.SelectedItem).Tag.ToString()!, date);
                            if (!existing.Add(ManualWeeklyHistory.Key(clear))) { duplicates++; continue; }
                            if (CrystalPrices.Find(clear.Name, clear.Difficulty, date) is null) unpriced++;
                            pending.Add((clear, (int)boss.Party.Value));
                        }
                preview.Text = $"{targets.Length}캐릭터 · {dates.Count}일 · 새 기록 {pending.Count}건\n기존 기록 {duplicates}건 중복 제외"
                    + (unpriced > 0 ? $" · 가격 미확인 {unpriced}건은 수익 합산 제외" : "");
                dialog.IsPrimaryButtonEnabled = pending.Count > 0;
            }
            start.DateChanged += (_, _) => UpdatePreview(); end.DateChanged += (_, _) => UpdatePreview(); weekday.SelectionChanged += (_, _) => UpdatePreview();
            foreach (var target in characters) { target.Check.Checked += (_, _) => UpdatePreview(); target.Check.Unchecked += (_, _) => UpdatePreview(); }
            foreach (var boss in bosses)
            {
                boss.Check.Checked += (_, _) => { boss.Difficulty.IsEnabled = boss.Party.IsEnabled = true; UpdatePreview(); };
                boss.Check.Unchecked += (_, _) => { boss.Difficulty.IsEnabled = boss.Party.IsEnabled = false; UpdatePreview(); };
                boss.Difficulty.SelectionChanged += (_, _) => UpdatePreview(); boss.Party.ValueChanged += (_, _) => UpdatePreview();
            }
            var saved = false;
            dialog.PrimaryButtonClick += (_, e) =>
            {
                UpdatePreview();
                if (_closed || _dataDeleting || pending.Count == 0 || pending.Any(item => !_schedulerCharacters.ContainsKey(item.Clear.Ocid)))
                { e.Cancel = true; preview.Text = "캐릭터나 기간을 다시 확인하세요."; return; }
                var previous = _settings.ManualWeeklyClears;
                var previousParties = _settings.BossPartySizes;
                _settings.ManualWeeklyClears = previous.Concat(pending.Select(item => item.Clear)).ToList();
                _settings.BossPartySizes = new(previousParties);
                foreach (var item in pending) _settings.BossPartySizes[ManualWeeklyHistory.Key(item.Clear)] = item.Party;
                try { _settings.Save(); }
                catch (Exception error) when (IsStorageError(error))
                {
                    _settings.ManualWeeklyClears = previous; _settings.BossPartySizes = previousParties;
                    preview.Text = "기록을 저장하지 못했어요. 저장 공간과 접근 권한을 확인하세요."; e.Cancel = true; return;
                }
                _lastBulkAdded.Clear(); _lastBulkAdded.AddRange(pending.Select(item => item.Clear)); saved = true;
            };
            UpdatePreview(); await dialog.ShowAsync();
            if (!saved || _closed) return;
            RefreshManualIncome();
            IncomeBulkStatus.Text = $"주간 보스 기록 {_lastBulkAdded.Count}건을 추가했습니다.";
            IncomeBulkUndoButton.Visibility = Visibility.Visible;
        }
        finally { _bulkDialogOpen = false; }
    }
    private void RefreshManualIncome()
    {
        foreach (var owner in SchedulerAvatars) owner.RefreshIncome(id => PartySize(owner.Ocid, id));
        IncomeReplayPanel.Pause(); IncomeReplayPanel.Visibility = Visibility.Collapsed;
        RefreshIncomeOverview(); PublishWebSnapshot();
    }
    private void IncomeBulkUndo_Click(object sender, RoutedEventArgs args)
    {
        if (_closed || _dataDeleting || _lastBulkAdded.Count == 0) return;
        var previous = _settings.ManualWeeklyClears;
        _settings.ManualWeeklyClears = previous.Except(_lastBulkAdded).ToList();
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error)) { _settings.ManualWeeklyClears = previous; IncomeBulkStatus.Text = "취소 내용을 저장하지 못했어요."; return; }
        _lastBulkAdded.Clear(); IncomeBulkUndoButton.Visibility = Visibility.Collapsed;
        IncomeBulkStatus.Text = "방금 추가한 수동 기록을 취소했습니다."; RefreshManualIncome();
    }
    private static int DifficultyOrder(string difficulty) => difficulty switch { "normal" => 0, "easy" => 1, "hard" => 2, "chaos" => 3, _ => 4 };
    private static string DifficultyLabel(string difficulty) => difficulty switch { "easy" => "이지", "normal" => "노멀", "hard" => "하드", "chaos" => "카오스", "extreme" => "익스트림", _ => difficulty };
}
