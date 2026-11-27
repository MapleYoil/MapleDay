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
    private Action? _closeLootEditor;
    private readonly LootMarketClient _lootMarket = new();
    private void IncomeCalendar_BossAddRequested(object? sender, DateOnly date) => OpenLootDialog(null, addDate: date);
    private void IncomeCalendar_LootRequested(object? sender, CalendarBossRecord clear)
        => OpenLootDialog(null, clear);
    private void OpenLootDialog(BossLootRecord? existing, CalendarBossRecord? source = null, DateOnly? addDate = null)
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
            var bossNames = source is not null ? new[] { source.Boss.Name } : addDate is not null
                ? BossRecordChoices.Single.Select(choice => choice.Name).Distinct().ToArray()
                : ManualWeeklyHistory.SingleChoices.Select(price => price.Name).Distinct().ToArray();
            foreach (var name in bossNames) boss.Items.Add(name);
            boss.IsEnabled = source is null;
            var difficulty = new ComboBox { Header = "난이도", HorizontalAlignment = HorizontalAlignment.Stretch };
            difficulty.IsEnabled = source is null;
            var clearChoice = new ComboBox { Header = "보스·난이도", ItemsSource = BossRecordChoices.Single,
                DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch };
            var item = new ComboBox { Header = "획득 아이템", HorizontalAlignment = HorizontalAlignment.Stretch };
            var mode = new ComboBox { Header = "수익 입력 방식", HorizontalAlignment = HorizontalAlignment.Stretch };
            mode.Items.Add("균등 분배"); mode.Items.Add("비율 분배"); mode.Items.Add("수령액 직접 입력"); mode.Items.Add("수수료 반영 정산");
            var settlement = new PartySettlementView();
            var preset = new ComboBox { Header = "저장한 파티 불러오기", DisplayMemberPath = "Name", ItemsSource = _settings.Parties.Where(PartySettlement.Valid).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
            var sharedPartyId = existing?.SharedPartyId ?? "";
            preset.SelectionChanged += (_, _) =>
            {
                if (preset.SelectedItem is not PartyPreset selected) return;
                sharedPartyId = selected.Id;
                settlement.Apply(PartySettlement.ForBoss(selected, boss.SelectedItem as string ?? "", (difficulty.SelectedItem as ComboBoxItem)?.Tag as string ?? ""), (character.SelectedItem as SchedulerCharacter)?.Ocid);
            };
            var settlementPanel = new StackPanel { Spacing = 12 }; settlementPanel.Children.Add(preset); settlementPanel.Children.Add(settlement);
            var party = new NumberBox { Header = "파티 인원 (인)", Value = existing?.PartySize ?? source?.Boss.PartySize ?? 1, Minimum = 1,
                Maximum = 6, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var amount = new TextBox { Header = "분배할 총액 (메소)", Text = (existing?.Amount ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                PlaceholderText = "예: 2000000000", MaxLength = 24 };
            var amountHint = new TextBlock { FontSize = 14, Foreground = AppTheme.Brush("AccentBrush"), TextWrapping = TextWrapping.Wrap };
            var marketChoice = new ComboBox { Header = "참고 시세", DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch };
            var marketHint = new TextBlock { Text = "시세를 불러오는 중…", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            var marketApply = new Button { Content = "시세를 총액으로 입력", IsEnabled = false };
            var marketPanel = new StackPanel { Spacing = 6 };
            foreach (var control in new FrameworkElement[] { marketChoice, marketHint, marketApply }) marketPanel.Children.Add(control);
            LootMarketSnapshot? market = null;
            var marketFailed = false;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(amount, "메소 정수를 입력하면 아래에 억 단위로 즉시 표시합니다.");
            var ratio = new TextBox { Header = "분배 비율", Text = existing?.Ratios ?? "1:1", PlaceholderText = "예: 2:1:1", MaxLength = 120 };
            var member = new NumberBox { Header = "내 순번 (비율의 왼쪽부터)", Minimum = 1, Maximum = 6,
                Value = existing?.OwnMember ?? 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var ratioHint = new TextBlock { Text = "예: 3인, 2:1:1, 내 순번 1 → 총액의 절반을 수령합니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            var soloReceipt = new Button { Content = "내 몫만 1로 설정", HorizontalAlignment = HorizontalAlignment.Left };
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16, Foreground = AppTheme.Brush("AccentBrush") };
            var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Brush("DangerTextBrush") };
            var lootEnabled = new CheckBox { Content = "물욕템 함께 기록", IsChecked = addDate is null, Visibility = addDate is null ? Visibility.Collapsed : Visibility.Visible };
            var recordChoice = new ComboBox { Header = "물욕템 기록 선택", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = source is null ? Visibility.Collapsed : Visibility.Visible };
            recordChoice.Items.Add(new ComboBoxItem { Content = "새 물욕템 추가" });
            if (source is not null)
                foreach (var saved in StoredLoot.Where(loot => BossLoot.ForClear(loot, source.Ocid, source.Boss)).OrderBy(loot => loot.Date))
                    recordChoice.Items.Add(new ComboBoxItem { Content = $"{saved.ItemName} · {saved.Date:MM.dd} · {CurrentIncomeDisplay.Format(saved.Received)}", Tag = saved });
            recordChoice.SelectedItem = recordChoice.Items.Cast<ComboBoxItem>().FirstOrDefault(option => option.Tag is BossLootRecord saved && saved.Id == existing?.Id) ?? recordChoice.Items[0];
            var recordHint = new TextBlock
            {
                Text = source is null ? "" : $"저장된 물욕템 {recordChoice.Items.Count - 1}건 · 새 항목을 추가하거나 기존 기록을 선택해 수정할 수 있어요.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Visibility = source is null ? Visibility.Collapsed : Visibility.Visible
            };
            var content = new StackPanel { Spacing = 12 };
            foreach (var control in new FrameworkElement[] { recordChoice, recordHint, lootEnabled, mode, item, marketPanel, amount, amountHint, ratio, member, soloReceipt, ratioHint, preview, errorText }) content.Children.Add(control);
            if (source is not null) content.Children.Insert(0, date);
            content.Children.Add(new TextBlock { Text = "금액은 직접 입력한 분배 기준 금액입니다. 정산 전에는 0으로 기록하고, 수령 후 수정할 수 있어요. 물욕템 수령액은 결정 수익과 별도로 합산합니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["SecondaryText"] });
            content.Children.Add(new TextBlock { Text = "컨티뉴어스 링·리스트레인트 링은 보스의 반지 상자를 열어 얻은 4레벨 반지를 기록합니다. 녹옥 상자만 주는 난이도에는 표시하지 않아요.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["SecondaryText"] });

            var leftContent = new StackPanel { Spacing = 12 };
            if (source is not null)
            {
                var owner = SchedulerAvatars.FirstOrDefault(value => value.Ocid == source.Ocid);
                leftContent.Children.Add(new TextBlock { Text = owner?.Name ?? "캐릭터", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                leftContent.Children.Add(new TextBlock { Text = $"{source.Boss.Name} · {source.Boss.DifficultyLabel}", FontSize = 16, TextWrapping = TextWrapping.Wrap });
                leftContent.Children.Add(new TextBlock { Text = $"처치일 {source.Boss.Date:yyyy.MM.dd}", Style = (Style)Application.Current.Resources["SecondaryText"] });
                leftContent.Children.Add(new TextBlock { Text = "기존 처치 기록에 연결된 물욕템을 추가하거나 수정합니다.", TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["SecondaryText"] });
            }
            else
            {
                leftContent.Children.Add(character);
                leftContent.Children.Add(date);
                if (addDate is not null)
                {
                    leftContent.Children.Add(clearChoice);
                    leftContent.Children.Add(new TextBlock { Text = "하드 스우 이상 · 체력이 낮은 보스부터 표시", FontSize = 12, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["SecondaryText"] });
                }
                else
                {
                    leftContent.Children.Add(boss);
                    leftContent.Children.Add(difficulty);
                }
            }
            leftContent.Children.Add(party);

            Border Section(string title, string description, UIElement body)
            {
                var panel = new StackPanel { Spacing = 14 };
                panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["SecondaryText"] });
                panel.Children.Add(body);
                return new Border { Padding = new Thickness(20), CornerRadius = new CornerRadius(16), Background = AppTheme.Brush("CardBrush"), BorderBrush = AppTheme.Brush("LineBrush"), BorderThickness = new Thickness(1), Child = panel };
            }

            var saveButton = new Button { Content = existing is null && source is not null ? "물욕템 추가" : "저장", Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 110 };
            var cancelButton = new Button { Content = "취소", MinWidth = 90 };
            var deleteButton = new Button { Content = "기록 삭제", MinWidth = 100, Visibility = source is null || existing is null ? Visibility.Collapsed : Visibility.Visible };

            var bodyGrid = new Grid { ColumnSpacing = 18, MinWidth = 740 };
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.86, GridUnitType.Star) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.14, GridUnitType.Star) });
            var clearSection = Section(source is null ? "보스 처치 기록" : "연결된 보스 기록", source is null
                ? "캐릭터와 날짜, 보스 난이도 및 파티 인원을 한 번에 확인하세요."
                : "물욕템은 이 처치 기록에 연결되어 수익 캘린더에 함께 표시됩니다.", leftContent);
            var lootSection = Section("물욕템 및 정산", addDate is not null
                ? "물욕템을 함께 기록하려면 선택한 뒤 판매·분배 정보를 입력하세요."
                : "획득 아이템과 실제 분배 방식을 입력하면 내 수령액을 바로 계산합니다.", content);
            bodyGrid.Children.Add(clearSection); Grid.SetColumn(lootSection, 1); bodyGrid.Children.Add(lootSection);

            var heading = new StackPanel { Spacing = 5 };
            heading.Children.Add(new TextBlock { Text = addDate is not null ? "보스 기록 추가" : $"{source?.Boss.Name} · 물욕템 관리", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            heading.Children.Add(new TextBlock { Text = "처치 기록과 물욕템 정산을 확인한 뒤 저장하세요.", TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["SecondaryText"] });
            var footer = new Grid { ColumnSpacing = 8 };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.Children.Add(deleteButton); Grid.SetColumn(deleteButton, 1); footer.Children.Add(cancelButton); Grid.SetColumn(cancelButton, 2); footer.Children.Add(saveButton); Grid.SetColumn(saveButton, 3);
            var page = new Grid { Padding = new Thickness(24, 24, 24, 20), RowSpacing = 16, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Stretch };
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.Children.Add(heading);
            var scroller = new ScrollViewer { Content = bodyGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Auto, Padding = new Thickness(0, 0, 6, 8) };
            Grid.SetRow(scroller, 1); page.Children.Add(scroller); Grid.SetRow(footer, 2); page.Children.Add(footer);

            void CloseEditor()
            {
                if (!_lootDialogOpen) return;
                _lootDialogOpen = false;
                _closeLootEditor = null;
                BossRecordEditorHost.Content = null;
            }
            _closeLootEditor = CloseEditor;
            ManualWeeklyClear ClearDraft()
            {
                if (character.SelectedItem is not SchedulerCharacter owner || boss.SelectedItem is not string name || date.Date is null)
                    throw new ArgumentException("캐릭터, 날짜, 보스를 선택하세요.");
                var key = (difficulty.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                var clear = new ManualWeeklyClear(owner.Ocid, name, key, DateOnly.FromDateTime(date.Date.Value.DateTime), ManualWeeklyHistory.CycleFor(name));
                if (!ManualWeeklyHistory.Valid(clear) || !BossRecordChoices.Allows(name, key)
                    || clear.Date < SchedulerBossHistory.FirstDate || clear.Date > today)
                    throw new ArgumentException("날짜와 보스 난이도를 확인해주세요.");
                if (!double.IsFinite(party.Value) || party.Value != Math.Truncate(party.Value) || party.Value < 1 || party.Value > BossParty.Maximum(name))
                    throw new ArgumentException($"파티 인원은 1~{BossParty.Maximum(name)}인으로 입력하세요.");
                if (_settings.ManualWeeklyClears.Any(saved => ManualWeeklyHistory.Key(saved) == ManualWeeklyHistory.Key(clear))
                    || owner.Income?.Records.Any(saved => saved.Id == ManualWeeklyHistory.Id(clear)) == true)
                    throw new ArgumentException("같은 초기화 기간에 이미 잡은 보스입니다. 기존 처치 기록에서 물욕템을 수정해주세요.");
                return clear;
            }
            var previewUpdating = false;
            settlement.Changed += (_, _) => { if (mode.SelectedIndex == 3) { try { amount.Text = settlement.Sale.ToString(); party.Value = settlement.Snapshot().Members.Length; } catch (ArgumentException) { } UpdatePreview(); } };
            content.Children.Insert(content.Children.IndexOf(mode) + 1, settlementPanel);
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
                if (!BossLoot.TryParseAmount(amount.Text, out var enteredAmount))
                    throw new ArgumentException("금액은 0~1000조 메소의 정수로 입력하세요.");
                if (!double.IsFinite(party.Value) || party.Value != Math.Truncate(party.Value) || party.Value < 1 || party.Value > BossParty.Maximum(name))
                    throw new ArgumentException($"파티 인원은 1~{BossParty.Maximum(name)}인으로 입력하세요.");
                var selectedMode = mode.SelectedIndex switch { 0 => "equal", 1 => "ratio", 2 => "received", 3 => "settlement", _ => "" };
                var snapshot = selectedMode == "settlement" ? settlement.Snapshot() : null;
                if (snapshot is not null) { enteredAmount = settlement.Sale; party.Value = snapshot.Members.Length; }
                if (selectedMode == "ratio" && (!double.IsFinite(member.Value) || member.Value != Math.Truncate(member.Value) || member.Value < 1 || member.Value > party.Value))
                    throw new ArgumentException("내 순번은 파티 인원 범위의 정수로 입력하세요.");
                var day = DateOnly.FromDateTime(date.Date.Value.DateTime);
                if (day > today) throw new ArgumentException("오늘 이후 날짜는 입력할 수 없어요.");
                var record = new BossLootRecord(existing?.Id ?? Guid.NewGuid().ToString("N"), owner.Ocid, day, name,
                    chosen?.Id ?? "", title, selectedMode, enteredAmount, (int)party.Value, ratio.Text.Trim(),
                    selectedMode == "ratio" ? (int)member.Value : 1, selectedDifficulty,
                    source?.Boss.Id ?? (addDate is not null ? ManualWeeklyHistory.Id(ClearDraft()) : ""),
                    selectedMode == "settlement" && _partyShares.IsShared(sharedPartyId) ? sharedPartyId : "",
                    selectedMode == "settlement" && _partyShares.IsShared(sharedPartyId) ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : 0);
                record = record with { Settlement = snapshot };
                if (!BossLoot.Valid(record)) throw new ArgumentException("보상·파티 인원·정산 정보를 확인해주세요.");
                if (!string.IsNullOrEmpty(record.SharedPartyId))
                {
                    var sharedParty = _settings.Parties.FirstOrDefault(value => value.Id == record.SharedPartyId)
                        ?? throw new ArgumentException("공유 파티를 찾을 수 없습니다.");
                    _ = PartyShareRecords.NormalizeLoot(record, PartyShareRecords.NormalizeParty(sharedParty));
                }
                return record;
            }
            void UpdatePreview()
            {
                if (previewUpdating) return;
                previewUpdating = true;
                try
                {
                amountHint.Text = BossLoot.TryParseAmount(amount.Text, out var enteredAmount) ? BossIncome.Money(enteredAmount) : "메소 금액을 정수로 입력하세요.";
                try
                {
                    var clear = addDate is not null ? ClearDraft() : null;
                    var receipt = lootEnabled.IsChecked == true ? Draft() : null;
                    preview.Text = (clear is null ? "" : $"{(clear.Cycle == BossCycle.Weekly ? "주간" : "월간")} · 결정 수익 " + (CrystalPrices.Find(clear.Name, clear.Difficulty, clear.Date) is { } price ? CurrentIncomeDisplay.Format(price.Meso / (int)party.Value) : "가격 확인 필요"))
                        + (receipt is null ? "" : (clear is null ? "" : "\n") + "물욕템 내 수령액 · " + CurrentIncomeDisplay.Format(receipt.Received));
                    errorText.Text = ""; saveButton.IsEnabled = true;
                }
                catch (ArgumentException error) { preview.Text = ""; errorText.Text = error.Message; saveButton.IsEnabled = false; }
                }
                finally { previewUpdating = false; }
            }
            void UpdateMarket()
            {
                marketPanel.Visibility = lootEnabled.IsChecked == true && mode.SelectedIndex != 2 ? Visibility.Visible : Visibility.Collapsed;
                var chosen = (item.SelectedItem as ComboBoxItem)?.Tag as BossLootItem;
                var owner = character.SelectedItem as SchedulerCharacter;
                var previous = marketChoice.SelectedItem as LootMarketPrice;
                var prices = chosen is not null && owner is not null ? market?.ForItem(chosen.Id, owner.Character.World) ?? [] : [];
                marketChoice.ItemsSource = prices;
                marketChoice.SelectedItem = prices.FirstOrDefault(value => value.Variant == previous?.Variant) ?? prices.FirstOrDefault();
                marketChoice.Visibility = prices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                marketApply.IsEnabled = marketChoice.SelectedItem is LootMarketPrice;
                var region = owner?.Character.World.StartsWith("챌린저", StringComparison.Ordinal) == true ? "챌린저스" : "본 서버";
                marketHint.Text = marketFailed ? "시세를 불러오지 못했어요. 금액을 직접 입력할 수 있습니다."
                    : market is null ? "시세를 불러오는 중…" : prices.Count == 0 ? "이 아이템의 등록된 시세가 없습니다."
                    : $"{region} 참고 시세 · 서버에서 하루 한 번 갱신합니다. 실제 거래·분배 금액을 확인해주세요."
                    + (chosen?.Name.EndsWith("링 4레벨", StringComparison.Ordinal) == true ? " 사이트의 반지 대표 시세이며 레벨 구분은 제공하지 않습니다." : "");
            }
            void UpdateMode()
            {
                var proportional = mode.SelectedIndex == 1;
                var enabled = lootEnabled.IsChecked == true;
                settlementPanel.Visibility = enabled && mode.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
                mode.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                amount.Visibility = amountHint.Visibility = enabled && mode.SelectedIndex != 3 ? Visibility.Visible : Visibility.Collapsed;
                party.Visibility = enabled && mode.SelectedIndex == 3 ? Visibility.Collapsed : Visibility.Visible;
                ratio.Visibility = member.Visibility = soloReceipt.Visibility = ratioHint.Visibility = enabled && proportional ? Visibility.Visible : Visibility.Collapsed;
                item.Visibility = enabled && mode.SelectedIndex != 2 ? Visibility.Visible : Visibility.Collapsed;
                amount.Header = mode.SelectedIndex == 2 ? "실제 받은 금액 (메소)" : "분배할 총액 (메소)";
                UpdateMarket();
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
                IReadOnlyList<string> keys = source is not null ? [source.Boss.Difficulty]
                    : addDate is not null && clearChoice.SelectedItem is BossRecordChoice selectedChoice ? [selectedChoice.Difficulty]
                    : ManualWeeklyHistory.SingleChoices.Where(price => SchedulerBossHistory.BossKey(price.Name) == SchedulerBossHistory.BossKey(name)).Select(price => price.Difficulty).ToArray();
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
            clearChoice.SelectionChanged += (_, _) =>
            {
                if (clearChoice.SelectedItem is not BossRecordChoice choice) return;
                boss.SelectedItem = choice.Name; ReloadDifficulty();
            };
            difficulty.SelectionChanged += (_, _) => ReloadItems();
            item.SelectionChanged += (_, _) => UpdateMode();
            character.SelectionChanged += (_, _) => ReloadDifficulty(); date.DateChanged += (_, _) => UpdatePreview();
            ratio.TextChanged += (_, _) => UpdatePreview();
            soloReceipt.Click += (_, _) =>
            {
                if (!double.IsFinite(party.Value) || !double.IsFinite(member.Value)) return;
                var count = Math.Clamp((int)party.Value, 1, 6);
                var own = Math.Clamp((int)member.Value, 1, count);
                ratio.Text = string.Join(':', Enumerable.Range(1, count).Select(index => index == own ? "1" : "0"));
                ratioHint.Text = $"{count}인 중 내 순번 {own}만 수령하도록 설정했습니다. 0인 구성원은 분배금이 없습니다.";
            };
            amount.TextChanged += (_, _) => UpdatePreview(); party.ValueChanged += (_, _) => { if (double.IsFinite(party.Value)) member.Maximum = Math.Max(1, party.Value); UpdatePreview(); };
            member.ValueChanged += (_, _) => UpdatePreview(); mode.SelectionChanged += (_, _) => UpdateMode();
            lootEnabled.Checked += (_, _) => UpdateMode(); lootEnabled.Unchecked += (_, _) => UpdateMode();
            marketChoice.SelectionChanged += (_, _) => marketApply.IsEnabled = marketChoice.SelectedItem is LootMarketPrice;
            marketApply.Click += (_, _) => { if (marketChoice.SelectedItem is LootMarketPrice price) { amount.Text = price.WholeEokPrice.ToString(System.Globalization.CultureInfo.InvariantCulture); settlement.SetSale(price.WholeEokPrice); } };
            recordChoice.SelectionChanged += (_, _) =>
            {
                if (source is null) return;
                existing = (recordChoice.SelectedItem as ComboBoxItem)?.Tag as BossLootRecord;
                sharedPartyId = existing?.SharedPartyId ?? "";
                preset.SelectedItem = ((PartyPreset[])preset.ItemsSource).FirstOrDefault(party => party.Id == sharedPartyId);
                date.Date = Offset(existing?.Date ?? source.Boss.Date);
                party.Value = existing?.PartySize ?? source.Boss.PartySize;
                amount.Text = (existing?.Amount ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture); ratio.Text = existing?.Ratios ?? "1:1"; member.Value = existing?.OwnMember ?? 1;
                if (existing?.Settlement is { } savedSettlement) { settlement.Load(savedSettlement); settlement.SetSale(existing.Amount); }
                else { settlement.SetPartySize((int)party.Value); settlement.SetSale(existing?.Amount ?? 0); }
                mode.SelectedIndex = existing?.Mode switch { "ratio" => 1, "received" => 2, "settlement" => 3, null => 3, _ => 0 };
                saveButton.Content = existing is null ? "물욕템 추가" : "수정 저장";
                deleteButton.Visibility = existing is null ? Visibility.Collapsed : Visibility.Visible;
                ReloadItems();
            };
            if (existing?.Settlement is { } savedSettlement) { settlement.Load(savedSettlement); settlement.SetSale(existing.Amount); }
            else { settlement.SetPartySize((int)party.Value); settlement.SetSale(existing?.Amount ?? 0); }
            mode.SelectedIndex = existing?.Mode switch { "ratio" => 1, "received" => 2, "settlement" => 3, null => 3, _ => 0 };
            if (addDate is not null) clearChoice.SelectedIndex = 0;
            else boss.SelectedItem = bossNames.FirstOrDefault(name => SchedulerBossHistory.BossKey(name) == SchedulerBossHistory.BossKey(existing?.Boss ?? "")) ?? bossNames.First();
            if (existing is null && source is not null)
            {
                var day = source.Boss.Date;
                var scheduledParty = _settings.CalendarSchedules.FirstOrDefault(schedule => schedule.Party is not null && schedule.Ocid == source.Ocid && schedule.Boss == source.Boss.Name && schedule.Difficulty == source.Boss.Difficulty
                    && (schedule.Date == day || schedule.Weekly && day >= schedule.Date && (schedule.RepeatUntil is null || day <= schedule.RepeatUntil) && (day.DayNumber - schedule.Date.DayNumber) % 7 == 0))?.Party;
                if (scheduledParty is not null && PartySettlement.Valid(scheduledParty))
                {
                    sharedPartyId = scheduledParty.Id;
                    preset.SelectedItem = ((PartyPreset[])preset.ItemsSource).FirstOrDefault(party => party.Id == scheduledParty.Id);
                    settlement.Apply(scheduledParty, source.Ocid);
                }
            }
            UpdateMode();
            async Task LoadMarket()
            {
                try { market = await _lootMarket.GetAsync(_supportLifetime.Token); }
                catch (Exception error) when (error is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or InvalidDataException or OperationCanceledException) { marketFailed = true; }
                if (!_closed && _lootDialogOpen) UpdateMarket();
            }
            _ = LoadMarket();
            saveButton.Click += (_, _) =>
            {
                if (_closed || _dataDeleting) { errorText.Text = "앱 상태가 바뀌었어요. 창을 다시 열어주세요."; return; }
                try
                {
                    var addingLoot = existing is null;
                    var clear = addDate is not null ? ClearDraft() : null;
                    var record = lootEnabled.IsChecked == true ? Draft() : null;
                    var ocid = clear?.Ocid ?? record!.Ocid;
                    if (!_schedulerCharacters.ContainsKey(ocid)) throw new ArgumentException("스케줄러에 등록한 캐릭터를 선택해주세요.");
                    var previous = _settings.BossLootRecords ?? [];
                    var previousClears = _settings.ManualWeeklyClears;
                    var previousChanges = _settings.BossClearChanges;
                    var previousParties = _settings.BossPartySizes;
                    if (record is not null) _settings.BossLootRecords = previous.Where(item => item.Id != record.Id).Append(record).ToList();
                    if (clear is not null)
                    {
                        _settings.ManualWeeklyClears = previousClears.Append(clear).ToList();
                        _settings.BossClearChanges = BossClearHistory.Restore(previousChanges, [clear]);
                        _settings.BossPartySizes = new(previousParties);
                        BossParty.SetPeriodSize(_settings.BossPartySizes, clear.Ocid, ManualWeeklyHistory.Id(clear), (int)party.Value);
                    }
                    try { _settings.Save(); } catch { _settings.BossLootRecords = previous; _settings.ManualWeeklyClears = previousClears; _settings.BossPartySizes = previousParties; _settings.BossClearChanges = previousChanges; throw; }
                    RefreshManualIncome(); IncomeCalendarPanel.ShowActionStatus(clear is null
                        ? addingLoot ? "물욕템 기록을 추가했어요." : "물욕템 기록을 수정했어요."
                        : "선택한 날짜에 보스 기록을 추가했어요.");
                    CloseEditor();
                    if (_currentPage == "boss-record") GoBack();
                }
                catch (ArgumentException error) { errorText.Text = error.Message; }
                catch (Exception error) when (IsStorageError(error)) { errorText.Text = "기록을 저장하지 못했어요. 저장 공간과 접근 권한을 확인한 뒤 다시 시도해주세요."; }
            };
            deleteButton.Click += (_, _) =>
            {
                if (_closed || _dataDeleting || existing is null) return;
                var previous = _settings.BossLootRecords;
                _settings.BossLootRecords = previous.Where(saved => saved.Id != existing.Id).ToList();
                try
                {
                    _settings.Save(); RefreshIncomeOverview(); PublishWebSnapshot(); IncomeCalendarPanel.ShowActionStatus("물욕템 기록을 삭제했어요.");
                    CloseEditor();
                    if (_currentPage == "boss-record") GoBack();
                }
                catch (Exception error) when (IsStorageError(error)) { _settings.BossLootRecords = previous; errorText.Text = "기록을 삭제하지 못했어요. 다시 시도해주세요."; }
            };
            cancelButton.Click += (_, _) =>
            {
                CloseEditor();
                if (_currentPage == "boss-record") GoBack();
            };
            BossRecordEditorHost.Content = page;
            NavigateTo("boss-record");
        }
        catch
        {
            _lootDialogOpen = false;
            _closeLootEditor = null;
            BossRecordEditorHost.Content = null;
            throw;
        }
    }
}
