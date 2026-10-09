using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using MapleDay.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay;
public sealed partial class MainWindow
{
    private bool _lootDialogOpen;
    private void IncomeCalendar_BossAddRequested(object? sender, DateOnly date) => OpenLootDialog(null, addDate: date);
    private void IncomeCalendar_LootRequested(object? sender, CalendarBossRecord clear)
        => OpenLootDialog(StoredLoot.FirstOrDefault(loot => BossLoot.ForClear(loot, clear.Ocid, clear.Boss)), clear);
    private async void OpenLootDialog(BossLootRecord? existing, CalendarBossRecord? source = null, DateOnly? addDate = null)
    {
        if (_closed || _dataDeleting || _lootDialogOpen || SchedulerAvatars.Count == 0) return;
        _lootDialogOpen = true;
        try
        {
            var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
            DateTimeOffset Offset(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
            var character = new ComboBox { Header = "캐릭터", ItemsSource = SchedulerAvatars.ToArray(), DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
            character.SelectedItem = SchedulerAvatars.FirstOrDefault(owner => owner.Ocid == (source?.Ocid ?? existing?.Ocid ?? _incomeSelectedOcid)) ?? SchedulerAvatars.First();
            character.IsEnabled = source is null;
            var date = new CalendarDatePicker { Header = "획득·정산 날짜", Date = Offset(existing?.Date ?? today),
                MinDate = Offset(new(2003, 4, 29)), MaxDate = Offset(today), HorizontalAlignment = HorizontalAlignment.Stretch };
            if (addDate is { } selectedDay) { date.Header = "클리어 날짜"; date.Date = Offset(selectedDay); date.IsEnabled = false; date.MinDate = Offset(SchedulerBossHistory.FirstDate); }
            else if (existing is null && source is not null) date.Date = Offset(source.Boss.Date);
            var boss = new ComboBox { Header = "보스", HorizontalAlignment = HorizontalAlignment.Stretch };
            var bossNames = source is not null ? new[] { source.Boss.Name } : ManualWeeklyHistory.SingleChoices.Select(price => price.Name).Distinct().ToArray();
            foreach (var name in bossNames) boss.Items.Add(name);
            boss.IsEnabled = source is null;
            var difficulty = new ComboBox { Header = "난이도", HorizontalAlignment = HorizontalAlignment.Stretch };
            difficulty.IsEnabled = source is null;
            var item = new ComboBox { Header = "획득 아이템", HorizontalAlignment = HorizontalAlignment.Stretch };
            var mode = new ComboBox { Header = "수익 입력 방식", HorizontalAlignment = HorizontalAlignment.Stretch };
            mode.Items.Add("균등 분배"); mode.Items.Add("비율 분배"); mode.Items.Add("수령액 직접 입력");
            var party = new NumberBox { Header = "파티 인원 (인)", Value = existing?.PartySize ?? source?.Boss.PartySize ?? 1, Minimum = 1,
                Maximum = 6, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var amount = new NumberBox { Header = "분배할 총액 (메소)", Value = existing?.Amount ?? 0, Minimum = 0,
                Maximum = BossLoot.MaximumAmount, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
            var ratio = new TextBox { Header = "분배 비율", Text = existing?.Ratios ?? "1:1", PlaceholderText = "예: 2:1:1", MaxLength = 120 };
            var member = new NumberBox { Header = "내 순번 (비율의 왼쪽부터)", Minimum = 1, Maximum = 6,
                Value = existing?.OwnMember ?? 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var ratioHint = new TextBlock { Text = "예: 3인, 2:1:1, 내 순번 1 → 총액의 절반을 수령합니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16, Foreground = AppTheme.Brush("AccentBrush") };
            var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Brush("DangerTextBrush") };
            var lootEnabled = new CheckBox { Content = "물욕템 함께 기록", IsChecked = addDate is null, Visibility = addDate is null ? Visibility.Collapsed : Visibility.Visible };
            var recordChoice = new ComboBox { Header = "물욕템 기록", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = source is null ? Visibility.Collapsed : Visibility.Visible };
            recordChoice.Items.Add(new ComboBoxItem { Content = "새 아이템·정산 추가" });
            if (source is not null)
                foreach (var saved in StoredLoot.Where(loot => BossLoot.ForClear(loot, source.Ocid, source.Boss)).OrderBy(loot => loot.Date))
                    recordChoice.Items.Add(new ComboBoxItem { Content = $"{saved.ItemName} · {saved.Date:MM.dd} · {CurrentIncomeDisplay.Format(saved.Received)}", Tag = saved });
            recordChoice.SelectedItem = recordChoice.Items.Cast<ComboBoxItem>().FirstOrDefault(option => option.Tag is BossLootRecord saved && saved.Id == existing?.Id) ?? recordChoice.Items[0];
            var content = new StackPanel { Spacing = 12, MinWidth = 340 };
            foreach (var control in new FrameworkElement[] { character, date, boss, difficulty, party, recordChoice, lootEnabled, mode, item, amount, ratio, member, ratioHint, preview, errorText }) content.Children.Add(control);
            content.Children.Add(new TextBlock { Text = "금액은 직접 입력한 분배 기준 금액입니다. 정산 전에는 0으로 기록하고, 수령 후 수정할 수 있어요. 물욕템 수령액은 결정의 주간 12개 제한과 별도로 합산합니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = "컨티뉴어스 링·리스트레인트 링은 보스의 반지 상자를 열어 얻은 4레벨 반지를 기록합니다. 녹옥 상자만 주는 난이도에는 표시하지 않아요.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog { Title = addDate is not null ? "보스 기록 추가" : $"{source?.Boss.Name} · 물욕템 기록", XamlRoot = Content.XamlRoot, RequestedTheme = Root.RequestedTheme,
                PrimaryButtonText = "저장", CloseButtonText = "취소", DefaultButton = ContentDialogButton.Primary,
                SecondaryButtonText = source is null ? "" : "기록 삭제", IsSecondaryButtonEnabled = existing is not null,
                Content = new ScrollViewer { Content = content, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            ManualWeeklyClear ClearDraft()
            {
                if (character.SelectedItem is not SchedulerCharacter owner || boss.SelectedItem is not string name || date.Date is null)
                    throw new ArgumentException("캐릭터, 날짜, 보스를 선택하세요.");
                var key = (difficulty.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                var clear = new ManualWeeklyClear(owner.Ocid, name, key, DateOnly.FromDateTime(date.Date.Value.DateTime), ManualWeeklyHistory.CycleFor(name));
                if (!ManualWeeklyHistory.Valid(clear) || clear.Date < SchedulerBossHistory.FirstDate || clear.Date > today)
                    throw new ArgumentException("날짜와 보스 난이도를 확인해주세요.");
                if (!double.IsFinite(party.Value) || party.Value != Math.Truncate(party.Value) || party.Value < 1 || party.Value > BossParty.Maximum(name))
                    throw new ArgumentException($"파티 인원은 1~{BossParty.Maximum(name)}인으로 입력하세요.");
                if (_settings.ManualWeeklyClears.Any(saved => ManualWeeklyHistory.Key(saved) == ManualWeeklyHistory.Key(clear))
                    || owner.Income?.Records.Any(saved => saved.Id == ManualWeeklyHistory.Id(clear)) == true)
                    throw new ArgumentException("같은 초기화 기간에 이미 잡은 보스입니다. 기존 처치 기록에서 물욕템을 수정해주세요.");
                return clear;
            }
            BossLootRecord Draft()
            {
                if (character.SelectedItem is not SchedulerCharacter owner || boss.SelectedItem is not string name || date.Date is null)
                    throw new ArgumentException("캐릭터, 날짜, 보스를 선택하세요.");
                var receiptOnly = mode.SelectedIndex == 2;
                var chosen = receiptOnly ? null : (item.SelectedItem as ComboBoxItem)?.Tag as BossLootItem;
                var selectedDifficulty = (difficulty.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (selectedDifficulty.Length == 0) throw new ArgumentException("난이도를 선택하세요.");
                if (!receiptOnly && (chosen is null || !BossLootCatalog.ForBoss(name, selectedDifficulty).Any(reward => reward.Id == chosen.Id)))
                    throw new ArgumentException("선택한 난이도의 보상 아이템을 골라주세요. 목록이 없으면 수령액 직접 입력으로 기록할 수 있어요.");
                var title = receiptOnly ? "물욕템 정산" : chosen!.Name;
                if (!double.IsFinite(amount.Value) || amount.Value != Math.Truncate(amount.Value) || amount.Value < 0 || amount.Value > BossLoot.MaximumAmount)
                    throw new ArgumentException("금액은 0~1000조 메소의 정수로 입력하세요.");
                if (!double.IsFinite(party.Value) || party.Value != Math.Truncate(party.Value) || party.Value < 1 || party.Value > BossParty.Maximum(name))
                    throw new ArgumentException($"파티 인원은 1~{BossParty.Maximum(name)}인으로 입력하세요.");
                var selectedMode = mode.SelectedIndex switch { 0 => "equal", 1 => "ratio", 2 => "received", _ => "" };
                if (selectedMode == "ratio" && (!double.IsFinite(member.Value) || member.Value != Math.Truncate(member.Value) || member.Value < 1 || member.Value > party.Value))
                    throw new ArgumentException("내 순번은 파티 인원 범위의 정수로 입력하세요.");
                var day = DateOnly.FromDateTime(date.Date.Value.DateTime);
                if (day > today) throw new ArgumentException("오늘 이후 날짜는 입력할 수 없어요.");
                var record = new BossLootRecord(existing?.Id ?? Guid.NewGuid().ToString("N"), owner.Ocid, day, name,
                    chosen?.Id ?? "", title, selectedMode, (long)amount.Value, (int)party.Value, ratio.Text.Trim(),
                    selectedMode == "ratio" ? (int)member.Value : 1, selectedDifficulty,
                    source?.Boss.Id ?? (addDate is not null ? ManualWeeklyHistory.Id(ClearDraft()) : ""));
                _ = record.Received;
                return record;
            }
            void UpdatePreview()
            {
                try
                {
                    var clear = addDate is not null ? ClearDraft() : null;
                    var receipt = lootEnabled.IsChecked == true ? Draft() : null;
                    preview.Text = (clear is null ? "" : $"{(clear.Cycle == BossCycle.Weekly ? "주간" : "월간")} · 결정 수익 " + (CrystalPrices.Find(clear.Name, clear.Difficulty, clear.Date) is { } price ? CurrentIncomeDisplay.Format(price.Meso / (int)party.Value) : "가격 확인 필요"))
                        + (receipt is null ? "" : (clear is null ? "" : "\n") + "물욕템 내 수령액 · " + CurrentIncomeDisplay.Format(receipt.Received));
                    errorText.Text = ""; dialog.IsPrimaryButtonEnabled = true;
                }
                catch (ArgumentException error) { preview.Text = ""; errorText.Text = error.Message; dialog.IsPrimaryButtonEnabled = false; }
            }
            void UpdateMode()
            {
                var proportional = mode.SelectedIndex == 1;
                var enabled = lootEnabled.IsChecked == true;
                mode.Visibility = amount.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                ratio.Visibility = member.Visibility = ratioHint.Visibility = enabled && proportional ? Visibility.Visible : Visibility.Collapsed;
                item.Visibility = enabled && mode.SelectedIndex != 2 ? Visibility.Visible : Visibility.Collapsed;
                amount.Header = mode.SelectedIndex == 2 ? "실제 받은 금액 (메소)" : "분배할 총액 (메소)";
                UpdatePreview();
            }
            void ReloadItems()
            {
                item.Items.Clear();
                var name = boss.SelectedItem as string ?? "";
                var key = (difficulty.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                foreach (var reward in BossLootCatalog.ForBoss(name, key))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                    var image = new Image { Width = 32, Height = 32, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
                    CachedImage.SetFile(image, reward.Icon); row.Children.Add(image);
                    row.Children.Add(new TextBlock { Text = reward.Name, VerticalAlignment = VerticalAlignment.Center });
                    item.Items.Add(new ComboBoxItem { Content = row, Tag = reward });
                }
                item.PlaceholderText = item.Items.Count == 0 ? "이 난이도에 등록된 물욕템이 없습니다" : "보상 아이템 선택";
                item.SelectedItem = item.Items.Cast<ComboBoxItem>().FirstOrDefault(option => option.Tag is BossLootItem reward && reward.Id == existing?.ItemId)
                    ?? item.Items.FirstOrDefault();
                UpdateMode();
            }
            void ReloadDifficulty()
            {
                var name = boss.SelectedItem as string ?? "";
                difficulty.Items.Clear();
                IReadOnlyList<string> keys = source is not null ? [source.Boss.Difficulty] : ManualWeeklyHistory.SingleChoices.Where(price => SchedulerBossHistory.BossKey(price.Name) == SchedulerBossHistory.BossKey(name)).Select(price => price.Difficulty).ToArray();
                if (keys.Count == 0) keys = CrystalPrices.All.Where(price => SchedulerBossHistory.BossKey(price.Name) == SchedulerBossHistory.BossKey(name))
                    .Select(price => price.Difficulty).Distinct().ToArray();
                if (keys.Count == 0) keys = ["normal"];
                foreach (var key in keys) difficulty.Items.Add(new ComboBoxItem { Content = BossLootCatalog.DifficultyLabel(key), Tag = key });
                var owner = character.SelectedItem as SchedulerCharacter;
                var chosenDate = date.Date is { } value ? DateOnly.FromDateTime(value.DateTime) : today;
                var savedDifficulty = existing is not null && SchedulerBossHistory.BossKey(existing.Boss) == SchedulerBossHistory.BossKey(name) ? existing.Difficulty : null;
                var preferred = savedDifficulty is { Length: > 0 } ? savedDifficulty
                    : owner?.Income?.Records.FirstOrDefault(record => record.Date == chosenDate && SchedulerBossHistory.BossKey(record.Name) == SchedulerBossHistory.BossKey(name))?.Difficulty
                    ?? owner?.Bosses.FirstOrDefault(tile => SchedulerBossHistory.BossKey(tile.Name) == SchedulerBossHistory.BossKey(name))?.Difficulty;
                difficulty.SelectedItem = difficulty.Items.Cast<ComboBoxItem>().FirstOrDefault(option => (string?)option.Tag == CrystalPrices.DifficultyKey(preferred)) ?? difficulty.Items[0];
                party.Maximum = BossParty.Maximum(name); party.Value = double.IsFinite(party.Value) ? Math.Clamp(party.Value, 1, party.Maximum) : 1;
                if (existing is null && owner is not null && source is null)
                {
                    var tile = owner.Bosses.FirstOrDefault(tile => SchedulerBossHistory.BossKey(tile.Name) == SchedulerBossHistory.BossKey(name));
                    party.Value = PartySize(owner.Ocid, BossParty.RecordId(name, SchedulerBossHistory.Cycle(tile?.Cycle) ?? BossCycle.Weekly, chosenDate));
                }
                UpdatePreview();
            }
            boss.SelectionChanged += (_, _) => ReloadDifficulty();
            difficulty.SelectionChanged += (_, _) => ReloadItems();
            item.SelectionChanged += (_, _) => UpdateMode();
            character.SelectionChanged += (_, _) => ReloadDifficulty(); date.DateChanged += (_, _) => UpdatePreview();
            ratio.TextChanged += (_, _) => UpdatePreview();
            amount.ValueChanged += (_, _) => UpdatePreview(); party.ValueChanged += (_, _) => { if (double.IsFinite(party.Value)) member.Maximum = Math.Max(1, party.Value); UpdatePreview(); };
            member.ValueChanged += (_, _) => UpdatePreview(); mode.SelectionChanged += (_, _) => UpdateMode();
            lootEnabled.Checked += (_, _) => UpdateMode(); lootEnabled.Unchecked += (_, _) => UpdateMode();
            recordChoice.SelectionChanged += (_, _) =>
            {
                if (source is null) return;
                existing = (recordChoice.SelectedItem as ComboBoxItem)?.Tag as BossLootRecord;
                date.Date = Offset(existing?.Date ?? source.Boss.Date);
                party.Value = existing?.PartySize ?? source.Boss.PartySize;
                amount.Value = existing?.Amount ?? 0; ratio.Text = existing?.Ratios ?? "1:1"; member.Value = existing?.OwnMember ?? 1;
                mode.SelectedIndex = existing?.Mode switch { "ratio" => 1, "received" => 2, _ => 0 };
                dialog.IsSecondaryButtonEnabled = existing is not null;
                ReloadItems();
            };
            mode.SelectedIndex = existing?.Mode switch { "ratio" => 1, "received" => 2, _ => 0 };
            boss.SelectedItem = bossNames.FirstOrDefault(name => SchedulerBossHistory.BossKey(name) == SchedulerBossHistory.BossKey(existing?.Boss ?? "")) ?? bossNames.First();
            UpdateMode();
            dialog.PrimaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                if (_closed || _dataDeleting) { errorText.Text = "앱 상태가 바뀌었어요. 창을 다시 열어주세요."; return; }
                try
                {
                    var clear = addDate is not null ? ClearDraft() : null;
                    var record = lootEnabled.IsChecked == true ? Draft() : null;
                    var ocid = clear?.Ocid ?? record!.Ocid;
                    if (!_schedulerCharacters.ContainsKey(ocid)) throw new ArgumentException("스케줄러에 등록한 캐릭터를 선택해주세요.");
                    var previous = _settings.BossLootRecords ?? [];
                    var previousClears = _settings.ManualWeeklyClears;
                    var previousParties = _settings.BossPartySizes;
                    if (record is not null) _settings.BossLootRecords = previous.Where(item => item.Id != record.Id).Append(record).ToList();
                    if (clear is not null)
                    {
                        _settings.ManualWeeklyClears = previousClears.Append(clear).ToList();
                        _settings.BossPartySizes = new(previousParties) { [ManualWeeklyHistory.Key(clear)] = (int)party.Value };
                    }
                    try { _settings.Save(); } catch { _settings.BossLootRecords = previous; _settings.ManualWeeklyClears = previousClears; _settings.BossPartySizes = previousParties; throw; }
                    RefreshManualIncome(); IncomeCalendarPanel.ShowActionStatus(clear is null ? "물욕템 기록을 저장했어요." : "선택한 날짜에 보스 기록을 추가했어요."); args.Cancel = false;
                }
                catch (ArgumentException error) { errorText.Text = error.Message; }
                catch (Exception error) when (IsStorageError(error)) { errorText.Text = "기록을 저장하지 못했어요. 저장 공간과 접근 권한을 확인한 뒤 다시 시도해주세요."; }
            };
            dialog.SecondaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                if (_closed || _dataDeleting || existing is null) return;
                var previous = _settings.BossLootRecords;
                _settings.BossLootRecords = previous.Where(saved => saved.Id != existing.Id).ToList();
                try { _settings.Save(); RefreshIncomeOverview(); PublishWebSnapshot(); IncomeCalendarPanel.ShowActionStatus("물욕템 기록을 삭제했어요."); args.Cancel = false; }
                catch (Exception error) when (IsStorageError(error)) { _settings.BossLootRecords = previous; errorText.Text = "기록을 삭제하지 못했어요. 다시 시도해주세요."; }
            };
            await dialog.ShowAsync();
        }
        finally { _lootDialogOpen = false; }
    }
}
