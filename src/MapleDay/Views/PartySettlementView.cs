using System.Globalization;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MapleDay.Views;

public sealed class PartySettlementView : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 12 };
    private readonly TextBox _sale = new() { Header = "아이템 판매 금액 (메소)", Text = "0" };
    private readonly TextBox _net = new() { Header = "판매 수수료 차감 후 실수령액 (메소)", Text = "0" };
    private readonly ComboBox _fee = new() { Header = "경매장 판매 수수료", ItemsSource = new[] { "0%", "3%", "5%" }, SelectedIndex = 1 };
    private readonly ComboBox _transfer = new() { Header = "분배금 전달 방식", ItemsSource = new[] { "경매장 (선택한 판매 수수료 적용)", "직접 교환 (5%)", "전달 수수료 없음" }, SelectedIndex = 0 };
    private readonly NumberBox _count = new() { Header = "파티 인원 (인)", Minimum = 1, Maximum = 6, Value = 2, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly TextBox _weights = new() { Header = "실수령액 분배 비율", Text = "1:1", PlaceholderText = "예: 2:1:1" };
    private readonly ComboBox _leader = new() { Header = "파티장", DisplayMemberPath = "Name" };
    private readonly ComboBox _own = new() { Header = "내 구성원", DisplayMemberPath = "Name" };
    private readonly ComboBox _state = new() { Header = "수령 상태", ItemsSource = new[] { "미정산 (수익 0)", "부분 수령", "정산 완료" }, SelectedIndex = 2 };
    private readonly TextBox _partial = new() { Header = "실제로 받은 금액 (메소)", Text = "0" };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _rows = new() { Spacing = 10 };
    private PartyMember[] _members = [];
    private bool _updating;
    public event EventHandler? Changed;
    public long Sale => BossLoot.TryParseAmount(_sale.Text, out var value) ? value : throw new ArgumentException("판매 금액을 확인해주세요.");
    private int Fee => _fee.SelectedIndex switch { 0 => 0, 2 => 5, _ => 3 };
    private string Transfer => _transfer.SelectedIndex switch { 1 => "trade", 2 => "none", _ => "auction" };
    public PartySettlementView()
    {
        Content = _root;
        foreach (var element in new FrameworkElement[] { _sale, _net, _fee, _transfer, _count, _weights, _leader, _own, _state, _partial, _hint, _rows }) _root.Children.Add(element);
        _sale.TextChanged += (_, _) => { if (_updating) return; FormatAmount(_sale); Refresh(); };
        _net.TextChanged += (_, _) =>
        {
            if (_updating) return;
            FormatAmount(_net);
            try { if (!BossLoot.TryParseAmount(_net.Text, out var value)) return; SetSale(PartySettlement.Gross(value, Fee)); }
            catch (ArgumentException) { _hint.Text = "유효한 금액을 입력해주세요."; }
        };
        _fee.SelectionChanged += (_, _) => Refresh(); _transfer.SelectionChanged += (_, _) => Refresh();
        _count.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            if (!double.IsFinite(_count.Value) || _count.Value != Math.Truncate(_count.Value)) { _hint.Text = "인원은 정수로 입력해주세요."; Changed?.Invoke(this, EventArgs.Empty); return; }
            var count = double.IsFinite(_count.Value) ? (int)_count.Value : 0;
            if (count is < 1 or > 6) return;
            SetMembers(Enumerable.Range(1, count).Select(i => _members.ElementAtOrDefault(i-1) ?? new PartyMember(i.ToString(), i == 1 ? "파티장" : $"파티원 {i-1}")).ToArray());
            Refresh();
        };
        _weights.TextChanged += (_, _) => Refresh(); _leader.SelectionChanged += (_, _) => Refresh(); _own.SelectionChanged += (_, _) => Refresh();
        _state.SelectionChanged += (_, _) => Refresh();
        _partial.TextChanged += (_, _) => { if (_updating) return; FormatAmount(_partial); Refresh(); };
        SetMembers([new("1", "파티장"), new("2", "파티원 1")]); Refresh();
    }
    public void SetSale(long sale) { _updating = true; _sale.Text = sale.ToString("N0", CultureInfo.InvariantCulture); _updating = false; Refresh(); }
    private void FormatAmount(TextBox editor)
    {
        var formatted = IntegerInputFormatter.Format(editor.Text, editor.SelectionStart);
        if (formatted.Text == editor.Text) return;
        _updating = true;
        try
        {
            editor.Text = formatted.Text;
            editor.SelectionStart = formatted.Caret;
            editor.SelectionLength = 0;
        }
        finally { _updating = false; }
    }
    private void SetMembers(PartyMember[] members)
    {
        _updating = true;
        var leader = (_leader.SelectedItem as PartyMember)?.Id; var own = (_own.SelectedItem as PartyMember)?.Id;
        _members = members; _count.Value = members.Length; _weights.Text = PartySettlement.Weights(members);
        _leader.ItemsSource = _own.ItemsSource = members;
        _leader.SelectedItem = members.FirstOrDefault(member => member.Id == leader) ?? members[0];
        _own.SelectedItem = members.FirstOrDefault(member => member.Id == own) ?? members[0];
        _updating = false;
    }
    public void Apply(PartyPreset party, string? ownOcid = null) => Load(new(party.Members.Select(member => member with { }).ToArray(), party.LeaderId,
        party.Members.FirstOrDefault(member => member.Ocid == ownOcid)?.Id ?? party.Members.FirstOrDefault(member => member.Ocid.Length > 0)?.Id ?? party.LeaderId, party.AuctionFee, party.Transfer, "complete"));
    public void SetPartySize(int count) => SetMembers(Enumerable.Range(1, count).Select(i => new PartyMember(i.ToString(), i == 1 ? "파티장" : $"파티원 {i-1}")).ToArray());
    public void Load(LootSettlement snapshot)
    {
        SetMembers(snapshot.Members.Select(member => member with { }).ToArray()); _updating = true;
        _fee.SelectedIndex = snapshot.AuctionFee switch { 0 => 0, 5 => 2, _ => 1 };
        _transfer.SelectedIndex = snapshot.Transfer switch { "trade" => 1, "none" => 2, _ => 0 };
        _leader.SelectedItem = _members.FirstOrDefault(member => member.Id == snapshot.LeaderId) ?? _members[0];
        _own.SelectedItem = _members.FirstOrDefault(member => member.Id == snapshot.OwnId) ?? _members[0];
        _state.SelectedIndex = snapshot.State switch { "pending" => 0, "partial" => 1, _ => 2 };
        _partial.Text = snapshot.ActualReceived.ToString("N0", CultureInfo.InvariantCulture); _updating = false; Refresh();
    }
    public LootSettlement Snapshot()
    {
        if (!double.IsFinite(_count.Value) || _count.Value != Math.Truncate(_count.Value) || _count.Value != _members.Length) throw new ArgumentException("파티 인원을 정수로 입력해주세요.");
        var parts = _weights.Text.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != _members.Length) throw new ArgumentException("인원 수만큼 비율을 콜론(:)으로 입력해주세요.");
        var members = _members.Select((member, i) => member with { Weight = decimal.TryParse(parts[i], NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var weight) ? weight : throw new ArgumentException("분배 비율을 확인해주세요.") }).ToArray();
        var result = new LootSettlement(members, (_leader.SelectedItem as PartyMember)?.Id ?? "", (_own.SelectedItem as PartyMember)?.Id ?? "",
            Fee, Transfer, _state.SelectedIndex switch { 0 => "pending", 1 => "partial", _ => "complete" },
            BossLoot.TryParseAmount(_partial.Text, out var actual) ? actual : throw new ArgumentException("실제 받은 금액을 확인해주세요."));
        _ = result.Calculate(Sale); return result;
    }
    private void Refresh()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            _partial.Visibility = _state.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            var snapshot = Snapshot(); var result = snapshot.Calculate(Sale);
            _net.Text = result.SaleNet.ToString("N0", CultureInfo.InvariantCulture);
            _hint.Text = $"판매 후 {BossIncome.Money(result.SaleNet)} · 총 수수료 {BossIncome.Money(result.Fees)}\n"
                + $"내 확정 수익 {BossIncome.Money(snapshot.Received(Sale))} · 1메소 단위 절사 잔액은 파티장에게 남습니다.";
            _rows.Children.Clear();
            foreach (var row in result.Members)
            {
                var panel = new StackPanel { Spacing = 5 };
                panel.Children.Add(new TextBlock { Text = row.Name + (row.Leader ? " · 파티장 보유액" : " · 전달 금액 / 실수령액"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                panel.Children.Add(new TextBlock { Text = row.Leader ? $"{row.Received:N0} 메소" : $"{row.TransferAmount:N0} / {row.Received:N0} 메소", TextWrapping = TextWrapping.Wrap });
                if (!row.Leader)
                {
                    var copy = new Button { Content = "전달 금액 복사" };
                    copy.Click += (_, _) => { var package = new DataPackage(); package.SetText(row.TransferAmount.ToString(CultureInfo.InvariantCulture)); Clipboard.SetContent(package); };
                    panel.Children.Add(copy);
                }
                _rows.Children.Add(new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = AppTheme.Brush("StageBrush"), Child = panel });
            }
        }
        catch (Exception error) when (error is ArgumentException or OverflowException or InvalidOperationException) { _rows.Children.Clear(); _hint.Text = error.Message; }
        finally { _updating = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
