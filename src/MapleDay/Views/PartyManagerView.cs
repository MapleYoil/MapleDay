using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MapleDay.Views;

public sealed class PartyManagerView : UserControl
{
    private sealed record PartyBossRow(PartyBoss Boss, string Label);
    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(24), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListView _list = new() { DisplayMemberPath = "Name", MaxHeight = 220 };
    private readonly ListView _bosses = new() { DisplayMemberPath = "Label", MaxHeight = 220 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sharedLootHint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListView _sharedLoot = new() { MaxHeight = 260 };
    private readonly PartySettlementView _settlement = new();
    private PartyShareService? _share;
    private AppSettings? _settings;
    private SchedulerCharacter[] _characters = [];
    private bool _dialog;
    public event EventHandler? Changed;
    public PartyManagerView()
    {
        Content = new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _root.Children.Add(new TextBlock { Text = "파티", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _root.Children.Add(new TextBlock { Text = "구성원과 보스를 저장하고 캘린더·물욕템 정산에서 불러오세요.", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, handler) in new (string, RoutedEventHandler)[] { ("파티 추가", async (_, _) => await EditPartyAsync(null)),
            ("파티 수정", async (_, _) => { if (_list.SelectedItem is PartyPreset party) await EditPartyAsync(party); }), ("파티 삭제", async (_, _) => await DeleteAsync()) })
        { var button = new Button { Content = label }; button.Click += handler; actions.Children.Add(button); }
        var bossActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, handler) in new (string, RoutedEventHandler)[] { ("보스 추가", async (_, _) => await EditBossAsync(null)),
            ("보스 수정", async (_, _) => { if (_bosses.SelectedItem is PartyBossRow row) await EditBossAsync(row.Boss); else _status.Text = "수정할 보스를 선택해주세요."; }),
            ("보스 삭제", async (_, _) => await DeleteBossAsync()) })
        { var button = new Button { Content = label }; button.Click += handler; bossActions.Children.Add(button); }
        var shareActions = new Grid { RowSpacing = 8, ColumnSpacing = 8 };
        for (var i = 0; i < 3; i++) shareActions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 2; i++) shareActions.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var shareIndex = 0;
        foreach (var (label, handler) in new (string, RoutedEventHandler)[] { ("공유 시작", async (_, _) => await ShareAsync()),
            ("파티원 초대", async (_, _) => await InviteAsync()), ("공유 파티 참여", async (_, _) => await JoinAsync()),
            ("지금 동기화", async (_, _) => await SyncAsync()), ("공유 해제", async (_, _) => await LeaveAsync()) })
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, label); button.Click += handler;
            Grid.SetRow(button, shareIndex / 3); Grid.SetColumn(button, shareIndex % 3); shareActions.Children.Add(button); shareIndex++;
        }
        _root.Children.Add(actions); _root.Children.Add(_list); _root.Children.Add(_summary);
        _root.Children.Add(new TextBlock { Text = "등록한 보스", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _root.Children.Add(bossActions); _root.Children.Add(_bosses); _root.Children.Add(shareActions); _root.Children.Add(_status);
        _root.Children.Add(new TextBlock { Text = "공유 물욕 현황", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _root.Children.Add(_sharedLootHint); _root.Children.Add(_sharedLoot);
        _root.Children.Add(new TextBlock { Text = "파티 분배금 계산", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _root.Children.Add(new TextBlock { Text = "판매 수수료와 전달 수수료를 반영한 예상 정산입니다. 저장은 처치 기록의 물욕템 정산에서 진행합니다.", TextWrapping = TextWrapping.Wrap });
        _root.Children.Add(_settlement);
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is not PartyPreset party) return;
            RefreshSelection(party);
            _settlement.Apply(party);
        };
    }
    public void SetShareService(PartyShareService service)
    {
        if (_share == service) return;
        if (_share is not null) _share.StatusChanged -= ShareStatusChanged;
        _share = service; _share.StatusChanged += ShareStatusChanged;
    }
    private void ShareStatusChanged(string status) => DispatcherQueue.TryEnqueue(() =>
    {
        _status.Text = status;
        if (_list.SelectedItem is PartyPreset party) RefreshSelection(party);
    });
    private void RefreshSelection(PartyPreset party)
    {
        var link = _share?.Link(party.Id);
        var sharing = link is null ? "공유하지 않음" : "공유 중 · 내 기록 " + (party.Members.FirstOrDefault(member => member.Id == link.MemberId)?.Name ?? "연결 확인 필요");
        _summary.Text = string.Join(" · ", party.Members.Select(member => member.Name)) + "\n" + sharing;
        var selectedBoss = (_bosses.SelectedItem as PartyBossRow)?.Boss;
        var rows = party.Bosses.Select(boss => new PartyBossRow(boss,
            BossLootCatalog.DifficultyLabel(boss.Difficulty) + " " + boss.Name + $" · {boss.Members.Length}인")).ToArray();
        _bosses.ItemsSource = rows;
        _bosses.SelectedItem = rows.FirstOrDefault(row => row.Boss.Name == selectedBoss?.Name && row.Boss.Difficulty == selectedBoss?.Difficulty) ?? rows.FirstOrDefault();
        var loot = _settings?.BossLootRecords.Where(record => record.SharedPartyId == party.Id).OrderByDescending(record => record.Date).ThenBy(record => record.ItemName).ToArray() ?? [];
        _sharedLoot.ItemsSource = loot.Select(record => $"{record.Date:yy.MM.dd} · {BossLootCatalog.DifficultyLabel(record.Difficulty)} {record.Boss} · {record.ItemName} · 판매 {BossIncome.Money(record.Amount)} · "
            + (record.Settlement?.State == "complete" ? "정산 완료" : record.Settlement?.State == "partial" ? "부분 수령" : "미정산")).ToArray();
        _sharedLootHint.Text = link is null ? "파티 공유를 시작하면 이 파티의 물욕템과 판매·정산 상태를 함께 볼 수 있습니다."
            : loot.Length == 0 ? "아직 공유한 물욕템 기록이 없습니다." : $"{loot.Length:N0}건 · 변경하면 연결된 파티원에게 자동 반영됩니다.";
        _sharedLoot.Visibility = loot.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetContext(AppSettings settings, IEnumerable<SchedulerCharacter> characters)
    {
        _settings = settings; _characters = characters.ToArray();
        var selected = (_list.SelectedItem as PartyPreset)?.Id;
        _list.ItemsSource = settings.Parties.Where(PartySettlement.Valid).ToArray();
        _list.SelectedItem = settings.Parties.FirstOrDefault(party => party.Id == selected) ?? settings.Parties.FirstOrDefault();
        if (settings.Parties.Count == 0) { _summary.Text = "등록한 파티가 없습니다. 파티 없이도 아래에서 분배금을 계산할 수 있습니다."; _bosses.ItemsSource = null; }
        else if (_list.SelectedItem is PartyPreset party) RefreshSelection(party);
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
        if (_share?.IsShared(party.Id) == true) { _status.Text = "공유 중인 파티는 공유 연결을 해제한 뒤 삭제해주세요."; return; }
        _dialog = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "파티 삭제", Content = "파티만 삭제합니다. 이미 저장한 일정과 정산은 유지됩니다.", PrimaryButtonText = "삭제", CloseButtonText = "취소" };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) Save(_settings.Parties.Where(item => item.Id != party.Id).ToList());
        }
        finally { _dialog = false; }
    }

    private async Task EditBossAsync(PartyBoss? existing)
    {
        if (_dialog || _settings is null || _list.SelectedItem is not PartyPreset party) { _status.Text = "먼저 파티를 만들고 선택해주세요."; return; }
        if (_share?.IsShared(party.Id) == true) { _status.Text = "공유 중인 파티는 공유를 종료한 뒤 보스를 변경해주세요."; return; }
        _dialog = true;
        try
        {
            var choices = BossRecordChoices.Single.ToArray();
            var search = new TextBox { Header = "보스 검색", PlaceholderText = "보스 이름 또는 난이도" };
            var boss = new ComboBox { Header = "보스·난이도 (하드 스우 이상 체력 순)", DisplayMemberPath = "Label", ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch };
            boss.SelectedItem = choices.FirstOrDefault(item => item.Name == existing?.Name && item.Difficulty == existing?.Difficulty) ?? choices.FirstOrDefault();
            search.TextChanged += (_, _) =>
            {
                var selected = boss.SelectedItem as BossRecordChoice;
                var filtered = choices.Where(item => item.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
                boss.ItemsSource = filtered; boss.SelectedItem = filtered.FirstOrDefault(item => item.Name == selected?.Name && item.Difficulty == selected?.Difficulty) ?? filtered.FirstOrDefault();
            };
            var memberGrid = new Grid { RowSpacing = 8, ColumnSpacing = 12 };
            for (var column = 0; column < 3; column++) memberGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var row = 0; row < Math.Max(1, (party.Members.Length + 2) / 3); row++) memberGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var memberChecks = new List<(PartyMember Member, CheckBox Check)>();
            for (var index = 0; index < party.Members.Length; index++)
            {
                var member = party.Members[index];
                var check = new CheckBox { Content = member.Name, IsChecked = existing?.Members.Contains(member.Id) ?? index < BossParty.Maximum((boss.SelectedItem as BossRecordChoice)?.Name ?? "") };
                Grid.SetRow(check, index / 3); Grid.SetColumn(check, index % 3); memberGrid.Children.Add(check); memberChecks.Add((member, check));
            }
            var limit = new TextBlock { TextWrapping = TextWrapping.Wrap };
            void UpdateLimit()
            {
                if (boss.SelectedItem is not BossRecordChoice selected) { limit.Text = "보스와 난이도를 선택해주세요."; return; }
                var maximum = BossParty.Maximum(selected.Name);
                var checkedMembers = memberChecks.Where(item => item.Check.IsChecked == true).ToArray();
                foreach (var extra in checkedMembers.Skip(maximum)) extra.Check.IsChecked = false;
                limit.Text = $"참여자 선택 · 최대 {maximum}인";
            }
            boss.SelectionChanged += (_, _) => UpdateLimit(); UpdateLimit();
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
            var form = new StackPanel { Spacing = 14 };
            form.Children.Add(new TextBlock { Text = party.Name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            form.Children.Add(new TextBlock { Text = "이 파티가 함께 잡을 보스와 난이도, 참여자만 선택하세요.", TextWrapping = TextWrapping.Wrap });
            form.Children.Add(search); form.Children.Add(boss); form.Children.Add(limit); form.Children.Add(memberGrid); form.Children.Add(error);
            var width = Math.Min(680, Math.Max(420, XamlRoot.Size.Width - 96));
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = existing is null ? "보스 추가" : "보스 수정",
                PrimaryButtonText = "저장", CloseButtonText = "취소", Content = form, Width = width, MinWidth = width, MaxWidth = width };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    if (boss.SelectedItem is not BossRecordChoice selected) throw new ArgumentException("보스와 난이도를 선택해주세요.");
                    var memberIds = memberChecks.Where(item => item.Check.IsChecked == true).Select(item => item.Member.Id).ToArray();
                    if (memberIds.Length == 0 || memberIds.Length > BossParty.Maximum(selected.Name)) throw new ArgumentException("보스 참여 인원을 확인해주세요.");
                    if (party.Bosses.Any(item => item.Name == selected.Name && item.Difficulty == selected.Difficulty
                        && (existing is null || item.Name != existing.Name || item.Difficulty != existing.Difficulty)))
                        throw new ArgumentException("이미 등록한 보스와 난이도입니다.");
                    var value = new PartyBoss(selected.Name, selected.Difficulty, memberIds);
                    var slots = existing is null ? party.Bosses.Append(value).ToArray()
                        : party.Bosses.Select(item => item.Name == existing.Name && item.Difficulty == existing.Difficulty ? value : item).ToArray();
                    var updated = party with { Bosses = slots };
                    if (!PartySettlement.Valid(updated)) throw new ArgumentException("보스·난이도·참여자를 확인해주세요.");
                    if (!Save(_settings.Parties.Select(item => item.Id == party.Id ? updated : item).ToList())) { error.Text = _status.Text; args.Cancel = true; }
                }
                catch (Exception exception) when (exception is ArgumentException or OverflowException) { error.Text = exception.Message; args.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        finally { _dialog = false; }
    }

    private async Task DeleteBossAsync()
    {
        if (_dialog || _settings is null || _list.SelectedItem is not PartyPreset party || _bosses.SelectedItem is not PartyBossRow selected)
        { _status.Text = "삭제할 보스를 선택해주세요."; return; }
        if (_share?.IsShared(party.Id) == true) { _status.Text = "공유 중인 파티는 공유를 종료한 뒤 보스를 변경해주세요."; return; }
        _dialog = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "보스 삭제",
                Content = $"{BossLootCatalog.DifficultyLabel(selected.Boss.Difficulty)} {selected.Boss.Name}을(를) 이 파티에서 삭제합니다. 기존 일정과 정산은 유지됩니다.",
                PrimaryButtonText = "삭제", CloseButtonText = "취소" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var updated = party with { Bosses = party.Bosses.Where(item => item.Name != selected.Boss.Name || item.Difficulty != selected.Boss.Difficulty).ToArray() };
            Save(_settings.Parties.Select(item => item.Id == party.Id ? updated : item).ToList());
        }
        finally { _dialog = false; }
    }

    private async Task ShareAsync()
    {
        if (_dialog || _share is null || _list.SelectedItem is not PartyPreset party) return;
        if (_share.IsShared(party.Id)) { _status.Text = "이미 공유 중인 파티입니다."; return; }
        var candidates = party.Members.Where(member => !string.IsNullOrWhiteSpace(member.Ocid)).ToArray();
        if (candidates.Length == 0) { _status.Text = "파티 수정에서 내 파티원에 스케줄러 캐릭터를 연결해주세요."; return; }
        _dialog = true;
        try
        {
            var choice = new ComboBox { Header = "이 PC에서 기록할 파티원", ItemsSource = candidates, DisplayMemberPath = "Name", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "이 파티에서 등록한 물욕템과 판매·정산 상태를 초대된 파티원에게 암호화해 공유합니다.", TextWrapping = TextWrapping.Wrap }); panel.Children.Add(choice);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "파티 공유 시작", Content = panel, PrimaryButtonText = "공유 시작", CloseButtonText = "취소" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || choice.SelectedItem is not PartyMember member) return;
            _status.Text = "파티 공유를 준비하는 중…"; await _share.CreateAsync(party, member.Id); RefreshSelection(party);
        }
        catch (Exception error) when (error is ArgumentException or IOException or System.Net.Http.HttpRequestException or InvalidOperationException) { _status.Text = error.Message; }
        finally { _dialog = false; }
    }

    private async Task InviteAsync()
    {
        if (_dialog || _share is null || _list.SelectedItem is not PartyPreset party) return;
        if (!_share.IsShared(party.Id)) { _status.Text = "먼저 파티 공유를 시작해주세요."; return; }
        _dialog = true;
        try
        {
            var members = party.Members.Where(member => member.Id != _share.Link(party.Id)?.MemberId).ToArray();
            if (members.Length == 0) { _status.Text = "초대할 다른 파티원이 없습니다."; return; }
            var choice = new ComboBox { Header = "초대할 파티원", ItemsSource = members, DisplayMemberPath = "Name", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var choose = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "파티원 초대", Content = choice, PrimaryButtonText = "코드 만들기", CloseButtonText = "취소" };
            if (await choose.ShowAsync() != ContentDialogResult.Primary || choice.SelectedItem is not PartyMember member) return;
            var code = await _share.InviteAsync(party.Id, member.Id);
            var box = new TextBox { Text = code, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = $"{member.Name} 전용 코드입니다. 5분 안에 한 번만 사용할 수 있으며 공유 기록을 여는 키가 포함되어 있습니다.", TextWrapping = TextWrapping.Wrap }); panel.Children.Add(box);
            var show = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "파티 초대 코드", Content = panel, PrimaryButtonText = "코드 복사", CloseButtonText = "닫기" };
            if (await show.ShowAsync() == ContentDialogResult.Primary) { var data = new DataPackage(); data.SetText(code); Clipboard.SetContent(data); _status.Text = "초대 코드를 복사했어요."; }
        }
        catch (Exception error) when (error is ArgumentException or IOException or System.Net.Http.HttpRequestException or InvalidOperationException) { _status.Text = error.Message; }
        finally { _dialog = false; }
    }

    private async Task JoinAsync()
    {
        if (_dialog || _share is null || _characters.Length == 0) return; _dialog = true;
        try
        {
            var code = new PasswordBox { Header = "받은 파티 초대 코드" };
            var character = new ComboBox { Header = "내 파티원으로 연결할 캐릭터", ItemsSource = _characters, DisplayMemberPath = "Name", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(code); panel.Children.Add(character);
            panel.Children.Add(new TextBlock { Text = "참여하면 기존 물욕템과 판매 상태를 불러오고, 이후 변경을 실시간으로 공유합니다.", TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "공유 파티 참여", Content = panel, PrimaryButtonText = "참여", CloseButtonText = "취소" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || character.SelectedItem is not SchedulerCharacter selected) return;
            _status.Text = "공유 파티를 불러오는 중…"; await _share.JoinAsync(code.Password, selected.Ocid); SetContext(_settings!, _characters); Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is ArgumentException or IOException or System.Net.Http.HttpRequestException or InvalidOperationException or FormatException) { _status.Text = error.Message; }
        finally { _dialog = false; }
    }

    private async Task SyncAsync()
    {
        if (_share is null || _list.SelectedItem is not PartyPreset party || !_share.IsShared(party.Id)) { _status.Text = "선택한 파티는 공유 중이 아닙니다."; return; }
        _status.Text = "공유 기록을 확인하는 중…"; _status.Text = await _share.SynchronizeAsync(party.Id); SetContext(_settings!, _characters); Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task LeaveAsync()
    {
        if (_dialog || _share is null || _list.SelectedItem is not PartyPreset party || !_share.IsShared(party.Id)) { _status.Text = "선택한 파티는 공유 중이 아닙니다."; return; }
        _dialog = true;
        try
        {
            var all = _share.CanDeleteSharedData(party.Id);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "파티 공유 해제",
                Content = new TextBlock { Text = all ? "공유를 만든 PC이므로 모든 파티원의 공유 연결을 종료합니다. 각 PC의 파티와 물욕템 기록은 유지됩니다." : "이 PC만 공유에서 나갑니다. 이 PC의 파티와 물욕템 기록은 유지됩니다.", TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = all ? "전체 공유 종료" : "이 PC 연결 해제", CloseButtonText = "취소" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await _share.LeaveAsync(party.Id, all); RefreshSelection(party);
        }
        finally { _dialog = false; }
    }
    private async Task EditPartyAsync(PartyPreset? existing)
    {
        if (_dialog || _settings is null) return; _dialog = true;
        if (existing is not null && _share?.IsShared(existing.Id) == true)
        {
            _dialog = false; _status.Text = "공유 중에는 파티 구성 변경으로 연결이 어긋나지 않도록 수정할 수 없습니다. 공유를 종료한 뒤 수정해주세요."; return;
        }
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
                var selectedLeader = (leader.SelectedItem as PartyMember)?.Id ?? existing?.LeaderId;
                var old = editors.ToArray(); editors.Clear(); fields.Children.Clear();
                var header = new Grid { ColumnSpacing = 10 };
                header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new() { Width = new GridLength(1.45, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new() { Width = new GridLength(110) });
                foreach (var (label, column) in new[] { ("이름", 0), ("내 캐릭터 연결", 1), ("분배 비율", 2) })
                {
                    var labelText = new TextBlock { Text = label }; Grid.SetColumn(labelText, column); header.Children.Add(labelText);
                }
                fields.Children.Add(header);
                for (var i = 0; i < n; i++)
                {
                    var stored = existing?.Members.ElementAtOrDefault(i);
                    var id = old.ElementAtOrDefault(i).Id ?? stored?.Id ?? Guid.NewGuid().ToString("N");
                    var text = new TextBox { MaxLength = 50, Text = old.ElementAtOrDefault(i).Name?.Text ?? stored?.Name ?? (i == 0 ? "파티장" : $"파티원 {i}") };
                    var character = new ComboBox { ItemsSource = options, DisplayMemberPath = "Label", HorizontalAlignment = HorizontalAlignment.Stretch };
                    character.SelectedItem = options.FirstOrDefault(option => option.Ocid == ((old.ElementAtOrDefault(i).Character?.SelectedItem as ScheduleCharacter)?.Ocid ?? stored?.Ocid)) ?? options[0];
                    character.SelectionChanged += (_, _) => { if (character.SelectedItem is ScheduleCharacter { Ocid.Length: > 0 } selected) text.Text = selected.Name; };
                    var weight = new NumberBox { Minimum = 0, Maximum = 1000000, Value = old.ElementAtOrDefault(i).Weight?.Value ?? (double)(stored?.Weight ?? 1) };
                    AutomationProperties.SetName(text, $"구성원 {i + 1} 이름"); AutomationProperties.SetName(character, $"구성원 {i + 1} 내 캐릭터 연결"); AutomationProperties.SetName(weight, $"구성원 {i + 1} 분배 비율");
                    var row = new Grid { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new() { Width = new GridLength(1.45, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new() { Width = new GridLength(110) });
                    Grid.SetColumn(character, 1); Grid.SetColumn(weight, 2); row.Children.Add(text); row.Children.Add(character); row.Children.Add(weight);
                    editors.Add((id, text, character, weight)); fields.Children.Add(row);
                }
                leader.ItemsSource = editors.Select(editor => new PartyMember(editor.Id, editor.Name.Text)).ToArray();
                leader.SelectedItem = ((PartyMember[])leader.ItemsSource).FirstOrDefault(member => member.Id == selectedLeader) ?? ((PartyMember[])leader.ItemsSource)[0];
            }
            Members(); count.ValueChanged += (_, _) => Members();
            var note = new TextBox { Header = "메모", AcceptsReturn = true, MaxLength = 2000, Text = existing?.Note ?? "" };
            var fee = new ComboBox { Header = "기본 경매장 수수료", ItemsSource = new[] { "0%", "3%", "5%" }, SelectedIndex = existing?.AuctionFee switch { 0 => 0, 5 => 2, _ => 1 } };
            var transfer = new ComboBox { Header = "기본 전달 방식", ItemsSource = new[] { "경매장", "직접 교환 5%", "수수료 없음" }, SelectedIndex = existing?.Transfer switch { "trade" => 1, "none" => 2, _ => 0 } };
            var memberError = new TextBlock { TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(memberError, AutomationLiveSetting.Polite);

            var basics = new Grid { ColumnSpacing = 12 };
            basics.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
            basics.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(count, 1); basics.Children.Add(name); basics.Children.Add(count);
            var memberForm = new StackPanel { Spacing = 14 };
            memberForm.Children.Add(new TextBlock { Text = "파티 정보", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            memberForm.Children.Add(new TextBlock { Text = "파티를 저장한 뒤 메인 화면에서 보스를 하나씩 추가할 수 있습니다. 내 캐릭터는 본인 항목에만 연결하세요.", TextWrapping = TextWrapping.Wrap });
            memberForm.Children.Add(basics); memberForm.Children.Add(fields); memberForm.Children.Add(leader); memberForm.Children.Add(memberError);

            var settlementGrid = new Grid { ColumnSpacing = 12 };
            settlementGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); settlementGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(transfer, 1); settlementGrid.Children.Add(fee); settlementGrid.Children.Add(transfer);
            memberForm.Children.Insert(memberForm.Children.Count - 1, settlementGrid);
            memberForm.Children.Insert(memberForm.Children.Count - 1, note);
            var pageHeight = Math.Max(280, XamlRoot.Size.Height - 230);
            var memberPage = new ScrollViewer { Content = memberForm, MaxHeight = pageHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var width = Math.Min(760, Math.Max(420, XamlRoot.Size.Width - 96));
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme,
                Title = existing is null ? "새 파티" : "파티 수정", PrimaryButtonText = "저장", CloseButtonText = "취소", Content = memberPage,
                Width = width, MinWidth = width, MaxWidth = width };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    var members = editors.Select(editor => new PartyMember(editor.Id, editor.Name.Text.Trim(), (editor.Character.SelectedItem as ScheduleCharacter)?.Ocid ?? "", (decimal)editor.Weight.Value)).ToArray();
                    var leaderId = (leader.SelectedItem as PartyMember)?.Id ?? "";
                    if (name.Text.Trim().Length is 0 or > 80 || !PartySettlement.ValidMembers(members, leaderId))
                        throw new ArgumentException("파티 이름·구성원 이름·분배 비율·파티장을 확인해주세요.");
                    var memberIds = members.Select(member => member.Id).ToHashSet(StringComparer.Ordinal);
                    var preservedBosses = (existing?.Bosses ?? []).Select(slot =>
                    {
                        var selected = slot.Members.Where(memberIds.Contains).Take(BossParty.Maximum(slot.Name)).ToArray();
                        return slot with { Members = selected.Length > 0 ? selected : members.Take(BossParty.Maximum(slot.Name)).Select(member => member.Id).ToArray() };
                    }).ToArray();
                    var party = new PartyPreset(existing?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), members, leaderId,
                        preservedBosses, fee.SelectedIndex switch { 0 => 0, 2 => 5, _ => 3 }, transfer.SelectedIndex switch { 1 => "trade", 2 => "none", _ => "auction" }, note.Text);
                    if (!PartySettlement.Valid(party)) throw new ArgumentException("이름·비율·보스별 참여자를 확인해주세요. 보스별 최대 인원을 초과할 수 없습니다.");
                    if (!Save(_settings.Parties.Where(item => item.Id != party.Id).Append(party).ToList())) { memberError.Text = _status.Text; args.Cancel = true; }
                }
                catch (Exception ex) when (ex is ArgumentException or OverflowException) { memberError.Text = ex.Message; args.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        finally { _dialog = false; }
    }
}
