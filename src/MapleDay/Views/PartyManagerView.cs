using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay.Views;

public sealed class PartyManagerView : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(24), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListView _list = new() { DisplayMemberPath = "Name", MaxHeight = 220 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly PartySettlementView _settlement = new();
    private AppSettings? _settings;
    private SchedulerCharacter[] _characters = [];
    private bool _dialog;
    public event EventHandler? Changed;
    public PartyManagerView()
    {
        Content = new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _root.Children.Add(new TextBlock { Text = "파티", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _root.Children.Add(new TextBlock { Text = "구성원과 보스를 저장하고 캘린더·물욕템 정산에서 불러오세요.", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, handler) in new (string, RoutedEventHandler)[] { ("파티 추가", async (_, _) => await EditAsync(null)),
            ("수정", async (_, _) => { if (_list.SelectedItem is PartyPreset party) await EditAsync(party); }), ("삭제", async (_, _) => await DeleteAsync()) })
        { var button = new Button { Content = label }; button.Click += handler; actions.Children.Add(button); }
        _root.Children.Add(actions); _root.Children.Add(_list); _root.Children.Add(_summary); _root.Children.Add(_status);
        _root.Children.Add(new TextBlock { Text = "파티 분배금 계산", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _root.Children.Add(new TextBlock { Text = "판매 수수료와 전달 수수료를 반영한 예상 정산입니다. 저장은 처치 기록의 물욕템 정산에서 진행합니다.", TextWrapping = TextWrapping.Wrap });
        _root.Children.Add(_settlement);
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is not PartyPreset party) return;
            _summary.Text = string.Join(" · ", party.Members.Select(member => member.Name)) + "\n" + string.Join(" / ", party.Bosses.Select(boss => BossLootCatalog.DifficultyLabel(boss.Difficulty) + " " + boss.Name + $" {boss.Members.Length}인"));
            _settlement.Apply(party);
        };
    }
    public void SetContext(AppSettings settings, IEnumerable<SchedulerCharacter> characters)
    {
        _settings = settings; _characters = characters.ToArray();
        var selected = (_list.SelectedItem as PartyPreset)?.Id;
        _list.ItemsSource = settings.Parties.Where(PartySettlement.Valid).ToArray();
        _list.SelectedItem = settings.Parties.FirstOrDefault(party => party.Id == selected) ?? settings.Parties.FirstOrDefault();
        if (settings.Parties.Count == 0) _summary.Text = "등록한 파티가 없습니다. 파티 없이도 아래에서 분배금을 계산할 수 있습니다.";
    }
    private bool Save(List<PartyPreset> parties)
    {
        if (_settings is null) return false;
        var old = _settings.Parties; _settings.Parties = parties;
        try { _settings.Save(); SetContext(_settings, _characters); Changed?.Invoke(this, EventArgs.Empty); _status.Text = "저장했습니다. 기존 일정과 정산은 변경되지 않습니다."; return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _settings.Parties = old; _status.Text = "파티를 저장하지 못했어요."; return false; }
    }
    private async Task DeleteAsync()
    {
        if (_dialog || _settings is null || _list.SelectedItem is not PartyPreset party) return;
        _dialog = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "파티 삭제", Content = "파티만 삭제합니다. 이미 저장한 일정과 정산은 유지됩니다.", PrimaryButtonText = "삭제", CloseButtonText = "취소" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) Save(_settings.Parties.Where(item => item.Id != party.Id).ToList());
        }
        finally { _dialog = false; }
    }
    private async Task EditAsync(PartyPreset? existing)
    {
        if (_dialog || _settings is null) return; _dialog = true;
        try
        {
            var name = new TextBox { Header = "파티 이름", MaxLength = 80, Text = existing?.Name ?? "" };
            var count = new NumberBox { Header = "구성원 수 (인)", Minimum = 1, Maximum = 6, Value = existing?.Members.Length ?? 2, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var fields = new StackPanel { Spacing = 10 };
            var editors = new List<(string Id, TextBox Name, ComboBox Character, NumberBox Weight)>();
            var options = new[] { new ScheduleCharacter("", "", "직접 입력 (다른 파티원)") }.Concat(_characters.Select(character => new ScheduleCharacter(character.Ocid, character.Name, character.Name + " · " + character.Character.World))).ToArray();
            var leader = new ComboBox { Header = "파티장", DisplayMemberPath = "Name" };
            void Members()
            {
                var n = double.IsFinite(count.Value) ? (int)count.Value : 0; if (n is < 1 or > 6) return;
                var old = editors.ToArray(); editors.Clear(); fields.Children.Clear();
                for (var i = 0; i < n; i++)
                {
                    var stored = existing?.Members.ElementAtOrDefault(i);
                    var id = old.ElementAtOrDefault(i).Id ?? stored?.Id ?? Guid.NewGuid().ToString("N");
                    var text = new TextBox { Header = $"구성원 {i+1} 이름", MaxLength = 50, Text = old.ElementAtOrDefault(i).Name?.Text ?? stored?.Name ?? (i == 0 ? "파티장" : $"파티원 {i}") };
                    var character = new ComboBox { Header = "내 캐릭터 연결 (선택)", ItemsSource = options, DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch };
                    character.SelectedItem = options.FirstOrDefault(option => option.Ocid == ((old.ElementAtOrDefault(i).Character?.SelectedItem as ScheduleCharacter)?.Ocid ?? stored?.Ocid)) ?? options[0];
                    character.SelectionChanged += (_, _) => { if (character.SelectedItem is ScheduleCharacter { Ocid.Length: > 0 } selected) text.Text = selected.Name; };
                    var weight = new NumberBox { Header = "기본 분배 비율", Minimum = 0, Maximum = 1000000, Value = old.ElementAtOrDefault(i).Weight?.Value ?? (double)(stored?.Weight ?? 1) };
                    editors.Add((id, text, character, weight)); fields.Children.Add(text); fields.Children.Add(character); fields.Children.Add(weight);
                }
                leader.ItemsSource = editors.Select(editor => new PartyMember(editor.Id, editor.Name.Text)).ToArray();
                leader.SelectedItem = ((PartyMember[])leader.ItemsSource).FirstOrDefault(member => member.Id == existing?.LeaderId) ?? ((PartyMember[])leader.ItemsSource)[0];
            }
            Members(); count.ValueChanged += (_, _) => Members();
            var bossList = new StackPanel { Spacing = 8 };
            var slots = new List<PartyBoss>(existing?.Bosses ?? []);
            void RefreshBosses()
            {
                bossList.Children.Clear();
                foreach (var slot in slots.ToArray())
                {
                    var row = new StackPanel { Spacing = 4 }; row.Children.Add(new TextBlock { Text = BossLootCatalog.DifficultyLabel(slot.Difficulty) + " " + slot.Name });
                    var checks = new StackPanel { Spacing = 8 };
                    foreach (var editor in editors)
                    {
                        var check = new CheckBox { Content = editor.Name.Text, IsChecked = slot.Members.Contains(editor.Id) };
                        void Update() { var i = slots.FindIndex(item => item.Name == slot.Name && item.Difficulty == slot.Difficulty); if (i < 0) return; var ids = slots[i].Members.ToHashSet(); if (check.IsChecked == true) ids.Add(editor.Id); else ids.Remove(editor.Id); slots[i] = slots[i] with { Members = ids.ToArray() }; }
                        check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update(); checks.Children.Add(check);
                    }
                    row.Children.Add(checks); var remove = new Button { Content = "보스 제거" }; remove.Click += (_, _) => { slots.RemoveAll(item => item.Name == slot.Name && item.Difficulty == slot.Difficulty); RefreshBosses(); }; row.Children.Add(remove); bossList.Children.Add(row);
                }
            }
            var search = new TextBox { Header = "보스 검색", PlaceholderText = "보스 이름 또는 난이도" };
            var bosses = new ComboBox { Header = "보스·난이도 (하드 스우 이상 체력 순)", DisplayMemberPath = "Label", ItemsSource = BossRecordChoices.Single, HorizontalAlignment = HorizontalAlignment.Stretch };
            search.TextChanged += (_, _) => bosses.ItemsSource = BossRecordChoices.Single.Where(item => item.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            var addBoss = new Button { Content = "보스 추가" };
            addBoss.Click += (_, _) => { if (bosses.SelectedItem is not BossRecordChoice item || slots.Any(slot => slot.Name == item.Name && slot.Difficulty == item.Difficulty)) return; slots.Add(new(item.Name, item.Difficulty, editors.Take(BossParty.Maximum(item.Name)).Select(editor => editor.Id).ToArray())); RefreshBosses(); };
            count.ValueChanged += (_, _) => RefreshBosses(); RefreshBosses();
            var note = new TextBox { Header = "메모", AcceptsReturn = true, MaxLength = 2000, Text = existing?.Note ?? "" };
            var fee = new ComboBox { Header = "기본 경매장 수수료", ItemsSource = new[] { "0%", "3%", "5%" }, SelectedIndex = existing?.AuctionFee switch { 0 => 0, 5 => 2, _ => 1 } };
            var transfer = new ComboBox { Header = "기본 전달 방식", ItemsSource = new[] { "경매장", "직접 교환 5%", "수수료 없음" }, SelectedIndex = existing?.Transfer switch { "trade" => 1, "none" => 2, _ => 0 } };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var form = new StackPanel { Spacing = 12 };
            foreach (var element in new FrameworkElement[] { name, count, fields, leader, search, bosses, addBoss, bossList, fee, transfer, note, error }) form.Children.Add(element);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "파티 구성", PrimaryButtonText = "저장", CloseButtonText = "취소", Content = new ScrollViewer { Content = form, MaxHeight = Math.Max(200, XamlRoot.Size.Height-220) } };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    var members = editors.Select(editor => new PartyMember(editor.Id, editor.Name.Text.Trim(), (editor.Character.SelectedItem as ScheduleCharacter)?.Ocid ?? "", (decimal)editor.Weight.Value)).ToArray();
                    var party = new PartyPreset(existing?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), members, (leader.SelectedItem as PartyMember)?.Id ?? "", slots.ToArray(), fee.SelectedIndex switch { 0 => 0, 2 => 5, _ => 3 }, transfer.SelectedIndex switch { 1 => "trade", 2 => "none", _ => "auction" }, note.Text);
                    if (!PartySettlement.Valid(party)) throw new ArgumentException("이름·비율·보스별 참여자를 확인해주세요. 보스별 최대 인원을 초과할 수 없습니다.");
                    if (!Save(_settings.Parties.Where(item => item.Id != party.Id).Append(party).ToList())) { error.Text = _status.Text; args.Cancel = true; }
                }
                catch (Exception ex) when (ex is ArgumentException or OverflowException) { error.Text = ex.Message; args.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        finally { _dialog = false; }
    }
}
