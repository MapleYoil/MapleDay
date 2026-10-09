using MapleDay.Core;
using MapleDay.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay;

public sealed partial class MainWindow
{
    private async void IncomeCalendar_BossEditRequested(object? sender, CalendarBossRecord source)
    {
        if (_closed || _dataDeleting || _lootDialogOpen || _bulkDialogOpen) return;
        _lootDialogOpen = true;
        try
        {
            var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
            DateTimeOffset Offset(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
            var date = new CalendarDatePicker { Header = "클리어 날짜", Date = Offset(source.Boss.Date),
                MinDate = Offset(SchedulerBossHistory.FirstDate), MaxDate = Offset(today) };
            var boss = new ComboBox { Header = "보스", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var name in ManualWeeklyHistory.SingleChoices.Select(price => price.Name).Distinct()) boss.Items.Add(name);
            var difficulty = new ComboBox { Header = "난이도", HorizontalAlignment = HorizontalAlignment.Stretch };
            var party = new NumberBox { Header = "파티 인원 (인)", Minimum = 1, Maximum = BossParty.Maximum(source.Boss.Name),
                Value = source.Boss.PartySize, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Services.AppTheme.Brush("DangerTextBrush") };
            var linked = StoredLoot.Count(item => BossLoot.ForClear(item, source.Ocid, source.Boss));
            var content = new StackPanel { Spacing = 12, MinWidth = 340 };
            foreach (var control in new FrameworkElement[] { date, boss, difficulty, party, preview, errorText }) content.Children.Add(control);
            content.Children.Add(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Text = "API 조회 기록에도 수정·삭제 내용이 유지됩니다."
                    + (linked > 0 ? $"\n보스 삭제 시 연결된 물욕템 {linked}건도 함께 삭제됩니다. 수정 시 물욕템 정산 금액과 분배는 유지됩니다." : "") });
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
                Title = source.CharacterName + " · 보스 기록 수정", Content = content,
                PrimaryButtonText = "저장", SecondaryButtonText = "보스 삭제", CloseButtonText = "취소", IsPrimaryButtonEnabled = false };
            ManualWeeklyClear Draft()
            {
                if (date.Date is not { } chosenDate || boss.SelectedItem is not string name
                    || difficulty.SelectedItem is not ComboBoxItem { Tag: string key }) throw new ArgumentException("날짜·보스·난이도를 선택하세요.");
                var clear = new ManualWeeklyClear(source.Ocid, name, key, DateOnly.FromDateTime(chosenDate.DateTime), ManualWeeklyHistory.CycleFor(name));
                if (!ManualWeeklyHistory.Valid(clear) || clear.Date < SchedulerBossHistory.FirstDate || clear.Date > today)
                    throw new ArgumentException("클리어 날짜와 난이도를 확인하세요.");
                if (!double.IsFinite(party.Value) || party.Value != Math.Truncate(party.Value) || party.Value < 1 || party.Value > BossParty.Maximum(name))
                    throw new ArgumentException($"파티 인원은 1~{BossParty.Maximum(name)}인으로 입력하세요.");
                var id = ManualWeeklyHistory.Id(clear);
                if (id != source.Boss.Id && _schedulerCharacters.TryGetValue(source.Ocid, out var owner)
                    && owner.Income?.Records.Any(record => record.Id == id) == true)
                    throw new ArgumentException("같은 초기화 기간에 해당 보스의 기록이 이미 있어요.");
                _ = BossClearHistory.Edit(source.Ocid, source.Boss, clear, _settings.BossClearChanges, _settings.BossLootRecords);
                return clear;
            }
            void UpdatePreview()
            {
                try
                {
                    var clear = Draft(); var price = CrystalPrices.Find(clear.Name, clear.Difficulty, clear.Date);
                    preview.Text = price is null ? "이 날짜의 결정 가격이 없어 수익 합산에서 제외됩니다." : $"결정 수익 {CurrentIncomeDisplay.Format(price.Meso / (int)party.Value)}";
                    errorText.Text = ""; dialog.IsPrimaryButtonEnabled = true;
                }
                catch (ArgumentException error) { errorText.Text = error.Message; dialog.IsPrimaryButtonEnabled = false; }
            }
            void ReloadDifficulty()
            {
                var selected = (difficulty.SelectedItem as ComboBoxItem)?.Tag as string ?? source.Boss.Difficulty;
                difficulty.Items.Clear();
                if (boss.SelectedItem is string name)
                {
                    foreach (var key in ManualWeeklyHistory.SingleChoices.Where(price => price.Name == name).Select(price => price.Difficulty).Distinct())
                        difficulty.Items.Add(new ComboBoxItem { Content = DifficultyLabel(key), Tag = key });
                    difficulty.SelectedItem = difficulty.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected)
                        ?? difficulty.Items.OfType<ComboBoxItem>().FirstOrDefault();
                    party.Maximum = BossParty.Maximum(name); party.Value = Math.Min(party.Value, party.Maximum);
                }
                UpdatePreview();
            }
            boss.SelectionChanged += (_, _) => ReloadDifficulty(); difficulty.SelectionChanged += (_, _) => UpdatePreview();
            date.DateChanged += (_, _) => UpdatePreview(); party.ValueChanged += (_, _) => UpdatePreview();
            boss.SelectedItem = boss.Items.OfType<string>().FirstOrDefault(name => SchedulerBossHistory.BossKey(name) == SchedulerBossHistory.BossKey(source.Boss.Name));
            void Save(ManualWeeklyClear? replacement)
            {
                if (_closed || _dataDeleting || !_schedulerCharacters.TryGetValue(source.Ocid, out var owner)
                    || owner.Income?.Records.Contains(source.Boss) != true) throw new ArgumentException("기록이 변경되었어요. 기록을 다시 선택하세요.");
                var plan = BossClearHistory.Edit(source.Ocid, source.Boss, replacement, _settings.BossClearChanges, _settings.BossLootRecords);
                var previousChanges = _settings.BossClearChanges; var previousLoot = _settings.BossLootRecords;
                var previousManual = _settings.ManualWeeklyClears; var previousParties = _settings.BossPartySizes;
                _settings.BossClearChanges = plan.Changes.ToList(); _settings.BossLootRecords = plan.Loot.ToList();
                _settings.ManualWeeklyClears = previousManual.Where(clear => clear.Ocid != source.Ocid || ManualWeeklyHistory.Id(clear) != source.Boss.Id).ToList();
                _settings.BossPartySizes = new(previousParties);
                if (replacement is not null) _settings.BossPartySizes[ManualWeeklyHistory.Key(replacement)] = (int)party.Value;
                try { _settings.Save(); }
                catch { _settings.BossClearChanges = previousChanges; _settings.BossLootRecords = previousLoot;
                    _settings.ManualWeeklyClears = previousManual; _settings.BossPartySizes = previousParties; throw; }
                RefreshManualIncome();
                IncomeCalendarPanel.ShowActionStatus(replacement is null ? "보스 기록을 삭제했어요." : "보스 기록을 수정했어요.");
            }
            dialog.PrimaryButtonClick += (_, args) =>
            {
                args.Cancel = true;
                try { Save(Draft()); args.Cancel = false; }
                catch (ArgumentException error) { errorText.Text = error.Message; }
                catch (Exception error) when (IsStorageError(error)) { errorText.Text = "기록을 저장하지 못했어요. 저장 공간과 접근 권한을 확인하세요."; }
            };
            // Close the edit dialog before showing the deletion confirmation.
            if (await dialog.ShowAsync() == ContentDialogResult.Secondary && !_closed && !_dataDeleting)
            {
                var confirm = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
                    Title = "보스 기록 삭제", Content = $"{source.CharacterName} · {source.Boss.Name} · {source.Boss.DifficultyLabel}\n{source.Boss.Date:yyyy.MM.dd} 기록을 삭제할까요?"
                        + (linked > 0 ? $"\n연결된 물욕템 {linked}건도 함께 삭제됩니다." : ""), PrimaryButtonText = "삭제", CloseButtonText = "취소" };
                confirm.PrimaryButtonClick += (_, args) =>
                {
                    args.Cancel = true;
                    try { Save(null); args.Cancel = false; }
                    catch (ArgumentException error) { confirm.Content = error.Message; }
                    catch (Exception error) when (IsStorageError(error)) { confirm.Content = "기록을 삭제하지 못했어요. 다시 시도하세요."; }
                };
                await confirm.ShowAsync();
            }
        }
        finally { _lootDialogOpen = false; }
    }
}
