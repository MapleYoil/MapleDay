using MapleDay.Core;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using MapleDay.Services;

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
            var bosses = new List<(string Name, CheckBox Check, ComboBox Difficulty, NumberBox Party, Border Card)>();
            var bossList = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            var search = new TextBox { PlaceholderText = "보스 이름 검색", HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(search, "추가할 보스 검색");
            var selectedOnly = new CheckBox { Content = "선택만 보기" };
            var registered = new Button { Content = "등록 보스 선택" };
            var clearSelection = new Button { Content = "선택 해제" };
            var empty = new TextBlock { Text = "검색한 보스가 없어요. 검색어를 지우거나 다른 이름을 입력하세요.", TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            var selectionStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            foreach (var group in BossRecordChoices.Weekly.GroupBy(choice => choice.Name))
            {
                var check = new CheckBox { Content = new TextBlock { Text = group.Key, FontSize = 13, TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
                var difficulty = new ComboBox { ItemsSource = group.ToArray(), DisplayMemberPath = "DifficultyLabel", SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
                var party = new NumberBox { Value = 1, Minimum = 1, Maximum = BossParty.Maximum(group.Key), Width = 64,
                    IsEnabled = false, Visibility = Visibility.Collapsed, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                AutomationProperties.SetName(party, group.Key + " 파티 인원 (인)");
                AutomationProperties.SetName(difficulty, group.Key + " 난이도");
                var options = new Grid { ColumnSpacing = 4 };
                options.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) });
                options.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                options.Children.Add(difficulty); options.Children.Add(party); Grid.SetColumn(party,1);
                var row = new StackPanel { Spacing = 4 }; row.Children.Add(check); row.Children.Add(options);
                var card = new Border { Child = row, Padding = new Thickness(8), CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1), BorderBrush = AppTheme.Brush("LineBrush"), Background = AppTheme.Brush("CardBrush") };
                bossList.Children.Add(card); bosses.Add((group.Key, check, difficulty, party, card));
            }
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var content = new StackPanel { Spacing = 10, MinWidth = 0 };
            var range = new Grid { ColumnSpacing = 8 };
            range.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) }); range.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) });
            start.HorizontalAlignment = end.HorizontalAlignment = HorizontalAlignment.Stretch;
            start.MinWidth = end.MinWidth = 0;
            range.Children.Add(start); range.Children.Add(end); Grid.SetColumn(end,1);
            var filters = new StackPanel { Spacing = 10 }; filters.Children.Add(range); filters.Children.Add(weekday);
            filters.Children.Add(new TextBlock { Text = "캐릭터", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            filters.Children.Add(new ScrollViewer { Content = characterList, MaxHeight = 190 });
            var chooser = new StackPanel { Spacing = 8 };
            chooser.Children.Add(new TextBlock { Text = "주간 보스 · 난이도 · 파티 인원 (인)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            chooser.Children.Add(search);
            var quickActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            quickActions.Children.Add(registered); quickActions.Children.Add(clearSelection);
            chooser.Children.Add(quickActions); chooser.Children.Add(selectedOnly); chooser.Children.Add(selectionStatus);
            chooser.Children.Add(new TextBlock { Text = "하드 스우 이상 체력 · 체력 낮은 순서", FontSize = 12 });
            var cards = new StackPanel { Spacing = 8 }; cards.Children.Add(bossList); cards.Children.Add(empty);
            var cardScroll = new ScrollViewer { Content = cards, MaxHeight = Math.Clamp(Root.ActualHeight - 300,220,440),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            chooser.Children.Add(cardScroll);
            var panes = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
            panes.ColumnDefinitions.Add(new() { Width = new GridLength(230) }); panes.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) });
            panes.RowDefinitions.Add(new() { Height = GridLength.Auto }); panes.RowDefinitions.Add(new() { Height = GridLength.Auto });
            panes.Children.Add(filters); panes.Children.Add(chooser); Grid.SetColumn(chooser,1); content.Children.Add(panes);
            content.Children.Add(new TextBlock { Text = "2026.06.25 이후의 수익 기록을 수동 추가합니다. 기존 기록은 유지하며 수익은 캐릭터당 주간 최대 12마리입니다.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            content.Children.Add(preview);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = "주간 보스 기록 일괄 추가",
                Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = "일괄 추가", CloseButtonText = "취소", IsPrimaryButtonEnabled = false };
            dialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(Root.ActualWidth - 80,300,1000);
            void LayoutCards()
            {
                var compact = content.ActualWidth < 660;
                panes.ColumnDefinitions[0].Width = compact ? new GridLength(1,GridUnitType.Star) : new GridLength(230);
                panes.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1,GridUnitType.Star);
                Grid.SetColumn(chooser,compact ? 0 : 1); Grid.SetRow(chooser,compact ? 1 : 0);
                var columns = chooser.ActualWidth >= 570 ? 3 : chooser.ActualWidth >= 350 ? 2 : 1;
                var visible = bosses.Where(boss => (selectedOnly.IsChecked != true || boss.Check.IsChecked == true)
                    && BossRecordChoices.MatchesSearch(boss.Name,search.Text))
                    .OrderBy(boss => ((BossRecordChoice)boss.Difficulty.SelectedItem).Health).ToArray();
                bossList.ColumnDefinitions.Clear(); bossList.RowDefinitions.Clear();
                for (var column=0; column<columns; column++) bossList.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) });
                for (var index=0; index<(visible.Length+columns-1)/columns; index++) bossList.RowDefinitions.Add(new() { Height = GridLength.Auto });
                foreach (var boss in bosses) boss.Card.Visibility = Visibility.Collapsed;
                for (var index=0; index<visible.Length; index++) { visible[index].Card.Visibility = Visibility.Visible; Grid.SetRow(visible[index].Card,index/columns); Grid.SetColumn(visible[index].Card,index%columns); }
                empty.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                selectionStatus.Text = $"선택 {bosses.Count(boss => boss.Check.IsChecked == true)}마리 / 보스 {bosses.Count}종";
            }
            content.SizeChanged += (_, _) => LayoutCards();
            chooser.SizeChanged += (_, _) => LayoutCards();
            search.TextChanged += (_, _) => LayoutCards(); selectedOnly.Checked += (_, _) => LayoutCards(); selectedOnly.Unchecked += (_, _) => LayoutCards();
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
                            var clear = new ManualWeeklyClear(target.Owner.Ocid, boss.Name, ((BossRecordChoice)boss.Difficulty.SelectedItem).Difficulty, date);
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
                boss.Check.Checked += (_, _) =>
                {
                    boss.Party.IsEnabled = true; boss.Party.Visibility = Visibility.Visible; boss.Card.Background = AppTheme.Brush("AccentSoftBrush");
                    boss.Card.BorderBrush = AppTheme.Brush("AccentBrush"); UpdatePreview(); LayoutCards();
                };
                boss.Check.Unchecked += (_, _) => { boss.Party.IsEnabled = false; boss.Party.Visibility = Visibility.Collapsed; boss.Card.Background = AppTheme.Brush("CardBrush"); boss.Card.BorderBrush = AppTheme.Brush("LineBrush"); UpdatePreview(); LayoutCards(); };
                boss.Difficulty.SelectionChanged += (_, _) => { UpdatePreview(); LayoutCards(); };
                boss.Party.ValueChanged += (_, _) => UpdatePreview();
            }
            clearSelection.Click += (_, _) => { foreach (var boss in bosses) boss.Check.IsChecked = false; };
            registered.Click += (_, _) =>
            {
                var targets = characters.Where(item => item.Check.IsChecked == true).Select(item => item.Owner).ToArray();
                foreach (var boss in bosses)
                {
                    var choices = boss.Difficulty.Items.OfType<BossRecordChoice>().ToArray();
                    var configured = targets.SelectMany(owner => owner.Bosses.Select(tile => new { Owner=owner, Tile=tile }))
                        .Where(item => SchedulerBossHistory.Cycle(item.Tile.Cycle) == BossCycle.Weekly && SchedulerBossHistory.BossKey(item.Tile.Name) == SchedulerBossHistory.BossKey(boss.Name))
                        .Select(item => new { Item=item, Choice=choices.FirstOrDefault(choice => choice.Difficulty == CrystalPrices.DifficultyKey(item.Tile.Difficulty)) })
                        .Where(item => item.Choice is not null).MaxBy(item => item.Choice!.Health);
                    boss.Check.IsChecked = configured is not null;
                    if (configured is not null) { boss.Difficulty.SelectedItem = configured.Choice; boss.Party.Value = configured.Item.Tile.PartySize; }
                }
                search.Text = ""; UpdatePreview(); LayoutCards();
                if (targets.Length == 0) preview.Text = "등록 보스를 선택하려면 캐릭터를 먼저 선택하세요.";
                else if (!bosses.Any(boss => boss.Check.IsChecked == true)) preview.Text = "불러온 등록 주간 보스가 없어요. 새로고침하거나 직접 선택하세요.";
            };
            var saved = false;
            dialog.PrimaryButtonClick += (_, e) =>
            {
                UpdatePreview();
                if (_closed || _dataDeleting || pending.Count == 0 || pending.Any(item => !_schedulerCharacters.ContainsKey(item.Clear.Ocid)))
                { e.Cancel = true; preview.Text = "캐릭터나 기간을 다시 확인하세요."; return; }
                var previous = _settings.ManualWeeklyClears;
                var previousChanges = _settings.BossClearChanges;
                var previousParties = _settings.BossPartySizes;
                _settings.ManualWeeklyClears = previous.Concat(pending.Select(item => item.Clear)).ToList();
                _settings.BossClearChanges = BossClearHistory.Restore(previousChanges, pending.Select(item => item.Clear));
                _settings.BossPartySizes = new(previousParties);
                foreach (var item in pending) _settings.BossPartySizes[ManualWeeklyHistory.Key(item.Clear)] = item.Party;
                try { _settings.Save(); }
                catch (Exception error) when (IsStorageError(error))
                {
                    _settings.ManualWeeklyClears = previous; _settings.BossPartySizes = previousParties; _settings.BossClearChanges = previousChanges;
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
        var previousChanges = _settings.BossClearChanges;
        _settings.ManualWeeklyClears = previous.Except(_lastBulkAdded).ToList();
        _settings.BossClearChanges = previousChanges.Select(change => change.Replacement is { } clear && _lastBulkAdded.Contains(clear)
            ? change with { Replacement = null } : change).ToList();
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error)) { _settings.ManualWeeklyClears = previous; _settings.BossClearChanges = previousChanges; IncomeBulkStatus.Text = "취소 내용을 저장하지 못했어요."; return; }
        _lastBulkAdded.Clear(); IncomeBulkUndoButton.Visibility = Visibility.Collapsed;
        IncomeBulkStatus.Text = "방금 추가한 수동 기록을 취소했습니다."; RefreshManualIncome();
    }
    private static int DifficultyOrder(string difficulty) => difficulty switch { "normal" => 0, "easy" => 1, "hard" => 2, "chaos" => 3, _ => 4 };
    private static string DifficultyLabel(string difficulty) => difficulty switch { "easy" => "이지", "normal" => "노멀", "hard" => "하드", "chaos" => "카오스", "extreme" => "익스트림", _ => difficulty };
}
