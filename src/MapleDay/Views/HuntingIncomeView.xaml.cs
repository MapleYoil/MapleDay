using System.Globalization;
using Microsoft.UI.Xaml.Media;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay.Views;

public sealed record HuntCharacter(string Ocid, string Label, int Level, string World);
public sealed record HuntRow(HuntingIncomeRecord Record, string Label) { public override string ToString() => Label; }
public sealed partial class HuntingIncomeView : UserControl
{
    public event EventHandler? RecordsChanged;
    public event EventHandler? ReplayRequested;
    private void Replay_Click(object sender, RoutedEventArgs args) => ReplayRequested?.Invoke(this, EventArgs.Empty);
    private AppSettings? _settings;
    private IncomeDisplay _display = new();
    private readonly LootMarketClient _market = new();
    private FragmentMarketPrice? _price;
    private readonly Dictionary<NumberBox, TextBox> _editors = [];
    private readonly Dictionary<NumberBox, string> _liveNumbers = [];
    private bool _ready;
    private bool _loading;
    private bool _saving;
    private int _marketGeneration;
    private List<HuntingIncomeRecord>? _bulkUndo;
    private List<HuntingIncomeRecord>? _bulkApplied;
    private HuntCharacter? Character => CharacterInput.SelectedItem as HuntCharacter;
    private DateOnly Date => DateOnly.FromDateTime((DateInput.Date ?? DateTimeOffset.Now).DateTime);
    private int RecordLevel => Existing?.Level is >= 1 and <= 300 ? Existing.Level : Character?.Level ?? 300;
    private HuntingIncomeRecord? Existing => (_settings?.HuntingIncomeRecords ?? []).FirstOrDefault(row => row.Ocid == Character?.Ocid && row.Date == Date);
    public HuntingIncomeView() { InitializeComponent(); }
    public void SetContext(AppSettings settings, IEnumerable<SchedulerCharacter> characters, string? selectedOcid, IncomeDisplay display)
    {
        _ready = false; _settings = settings; _display = display;
        var previous = Character?.Ocid;
        var choices = characters.Where(owner => selectedOcid is null || owner.Ocid == selectedOcid)
            .Select(owner => new HuntCharacter(owner.Ocid, owner.Name + " · " + owner.Character.World, owner.Character.Level, owner.Character.World)).ToArray();
        CharacterInput.ItemsSource = choices;
        CharacterInput.SelectedItem = choices.FirstOrDefault(owner => owner.Ocid == previous) ?? choices.FirstOrDefault();
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        DateInput.MaxDate = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        DateInput.Date ??= DateInput.MaxDate;
        BulkStart.MaxDate = BulkEnd.MaxDate = DateInput.MaxDate;
        BulkStart.Date ??= DateInput.MaxDate.AddDays(-6);
        BulkEnd.Date ??= DateInput.MaxDate;
        _ready = true; LoadDay(); RefreshRecords(); _ = LoadMarketAsync();
    }
    private void Character_Changed(object sender, SelectionChangedEventArgs args)
    { if (_ready) { LoadDay(); _ = LoadMarketAsync(); } }
    private void Date_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) { if (_ready) LoadDay(); }
    private void InputTab_Changed(object sender, SelectionChangedEventArgs args)
    { if (MesoInputs is null || FragmentInputs is null) return; MesoInputs.Visibility = InputTabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; FragmentInputs.Visibility = InputTabs.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; }
    private void LoadDay()
    {
        _loading = true; _liveNumbers.Clear();
        var row = Existing;
        MesoInput.Value = row?.Meso ?? 0; FragmentCount.Value = row?.Fragments ?? 0;
        FragmentPrice.Value = row is null ? Math.Clamp(_settings?.HuntingFragmentPriceMan ?? 1, 1, 9999) : row.FragmentUnitPrice / 10000d;
        BonusInput.Value = (double)(row?.MesoBonus ?? Math.Clamp(_settings?.HuntingMesoBonus ?? 0, 0, 10000));
        var limit = HuntingIncome.DailyLimit(RecordLevel);
        var usage = row is null ? null : HuntingIncome.LimitUsage(row);
        LimitAmount.Maximum = limit; LimitAmount.Value = usage ?? 0;
        MaximumLimit.IsChecked = usage == limit; UseLimit.IsChecked = usage is not null;
        _loading = false; Status.Text = ""; Calculate();
    }
    private static TextBox? FindEditor(DependencyObject root)
    {
        if (root is TextBox editor) return editor;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindEditor(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    private void Number_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not NumberBox box || FindEditor(box) is not { } editor) return;
        if (_editors.TryGetValue(box, out var previous))
        {
            if (ReferenceEquals(previous, editor)) return;
            previous.TextChanged -= LiveNumber_Changed;
        }
        _editors[box] = editor; editor.TextChanged += LiveNumber_Changed;
    }
    private void LiveNumber_Changed(object sender, TextChangedEventArgs args)
    {
        var box = _editors.FirstOrDefault(pair => ReferenceEquals(pair.Value, sender)).Key;
        if (box is null) return;
        if (!_ready || _loading) { _liveNumbers.Remove(box); return; }
        _liveNumbers[box] = ((TextBox)sender).Text;
        Calculate();
    }
    private double NumberValue(NumberBox box) => !_liveNumbers.TryGetValue(box, out var text) ? box.Value
        : double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var value) ? value : double.NaN;
    private void Input_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    { _liveNumbers.Remove(sender); if (_ready && !_loading) Calculate(); }
    private void Limit_Changed(object sender, RoutedEventArgs args) { if (_ready && !_loading) Calculate(); }
    private HuntingIncomeRecord Draft()
    {
        if (Character is not { } owner || !double.IsFinite(NumberValue(MesoInput)) || !double.IsFinite(NumberValue(FragmentCount))
            || !double.IsFinite(NumberValue(FragmentPrice)) || !double.IsFinite(NumberValue(BonusInput)) || !double.IsFinite(NumberValue(LimitAmount)))
            throw new ArgumentException();
        if (NumberValue(FragmentPrice) != Math.Truncate(NumberValue(FragmentPrice)) || NumberValue(FragmentCount) != Math.Truncate(NumberValue(FragmentCount)) || NumberValue(MesoInput) != Math.Truncate(NumberValue(MesoInput))) throw new ArgumentException();
        var bonus = (decimal)NumberValue(BonusInput);
        var level = RecordLevel;
        long? usage = null;
        if (UseLimit.IsChecked == true)
        {
            if (NumberValue(LimitAmount) != Math.Truncate(NumberValue(LimitAmount))) throw new ArgumentException();
            usage = MaximumLimit.IsChecked == true ? HuntingIncome.DailyLimit(level) : checked((long)NumberValue(LimitAmount));
        }
        var meso = usage is { } amount ? HuntingIncome.FromLimitAmount(level, amount, bonus) : checked((long)NumberValue(MesoInput));
        return new(Existing?.Id ?? Guid.NewGuid().ToString("N"), owner.Ocid, Date, meso,
            checked((int)NumberValue(FragmentCount)), HuntingIncome.FragmentPrice(checked((int)NumberValue(FragmentPrice))), bonus, null, level, usage);
    }
    private void Calculate()
    {
        LimitInputs.Visibility = UseLimit.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MesoInput.IsEnabled = UseLimit.IsChecked != true;
        LimitAmount.IsEnabled = MaximumLimit.IsChecked != true;
        if (MaximumLimit.IsChecked == true)
        { _loading = true; _liveNumbers.Remove(LimitAmount); LimitAmount.Value = HuntingIncome.DailyLimit(RecordLevel); _loading = false; }
        DeleteButton.IsEnabled = Existing is not null;
        try
        {
            var row = Draft();
            if (UseLimit.IsChecked == true) { _loading = true; MesoInput.Value = row.Meso; _loading = false; }
            LimitText.Text = $"Lv. {row.Level} · 기본 메소 제한 {IncomeReplay.CompactMeso(HuntingIncome.DailyLimit(row.Level))} 메소";
            Calculation.Text = $"메소 {_display.Format(row.Meso)} + 조각 {row.Fragments:N0}개 × {IncomeReplay.CompactMeso(row.FragmentUnitPrice)} = {_display.Format(row.Total)}";
            SaveButton.IsEnabled = HuntingIncome.Valid(row) && !_saving;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { SaveButton.IsEnabled = false; Calculation.Text = "캐릭터와 유효한 금액·수량을 입력해주세요."; }
        UpdateBulkPreview();
    }
    private HuntingIncome.BulkResult BulkPlan()
    {
        if (_settings is null || BulkStart.Date is null || BulkEnd.Date is null) throw new ArgumentException("시작일과 종료일을 선택해주세요.");
        return HuntingIncome.Bulk(_settings.HuntingIncomeRecords ?? [], Draft(),
            DateOnly.FromDateTime(BulkStart.Date.Value.DateTime), DateOnly.FromDateTime(BulkEnd.Date.Value.DateTime),
            SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow), BulkOverwrite.IsChecked == true);
    }
    private void BulkDate_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) { if (_ready) UpdateBulkPreview(); }
    private void BulkOverwrite_Changed(object sender, RoutedEventArgs args) { if (_ready) UpdateBulkPreview(); }
    private void UpdateBulkPreview()
    {
        if (!_ready || BulkPreview is null) return;
        try
        {
            var plan = BulkPlan(); var days = plan.Added + plan.Updated;
            BulkPreview.Text = $"추가 {plan.Added}일 · 수정 {plan.Updated}일 · 기존 기록 유지 {plan.Skipped}일\n입력할 수익 {_display.Format(checked(Draft().Total * days))}";
            BulkSaveButton.IsEnabled = days > 0 && !_saving;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { BulkPreview.Text = error is ArgumentException ? "캐릭터·기간과 하루당 메소·조각을 확인해주세요. 기간은 최대 10년입니다." : "금액이나 기간을 줄여주세요."; BulkSaveButton.IsEnabled = false; }
        if (_bulkApplied is not null && !ReferenceEquals(_settings?.HuntingIncomeRecords, _bulkApplied))
        { _bulkUndo = _bulkApplied = null; BulkUndoButton.Visibility = Visibility.Collapsed; }
    }
    private void BulkSave_Click(object sender, RoutedEventArgs args)
    {
        if (_settings is null || _saving) return;
        try
        {
            var plan = BulkPlan(); if (plan.Added + plan.Updated == 0) return;
            var previous = _settings.HuntingIncomeRecords ?? [];
            if (Persist(plan.Records.ToList(), Draft(), $"사냥 기록 {plan.Added}일 추가 · {plan.Updated}일 수정 · {plan.Skipped}일 유지했어요."))
            { _bulkUndo = previous; _bulkApplied = _settings.HuntingIncomeRecords; BulkUndoButton.Visibility = Visibility.Visible; }
        }
        catch (Exception error) when (error is ArgumentException or OverflowException) { Status.Text = "기간과 하루당 입력값을 확인해주세요."; }
    }
    private void BulkUndo_Click(object sender, RoutedEventArgs args)
    {
        if (_settings is not null && !_saving && _bulkUndo is not null && ReferenceEquals(_settings.HuntingIncomeRecords, _bulkApplied))
            Persist(_bulkUndo, null, "방금 일괄 입력한 기록을 되돌렸어요.");
    }
    private async Task LoadMarketAsync(bool refresh = false)
    {
        var generation = ++_marketGeneration; var owner = Character;
        _price = null; MarketApply.IsEnabled = false;
        if (owner is null) return;
        MarketText.Text = "참고 시세 확인 중…";
        MarketRefresh.IsEnabled = false;
        try
        {
            var snapshot = await _market.GetAsync(refresh: refresh);
            if (generation != _marketGeneration) return;
            var region = owner.World.StartsWith("챌린저", StringComparison.Ordinal) ? "challengers" : "normal";
            _price = snapshot.Fragments?.FirstOrDefault(row => row.Region == region && row.InputPriceMan is >= 1 and <= 9999);
            MarketText.Text = _price is { } price ? $"참고 시세 {price.InputPriceMan}만 메소 · {price.Date:MM.dd}" : "참고 시세가 아직 없어요. 가격을 직접 입력할 수 있습니다.";
            MarketApply.IsEnabled = _price is not null;
        }
        catch { if (generation == _marketGeneration) MarketText.Text = "시세를 확인하지 못했어요. 가격을 직접 입력할 수 있습니다."; }
        finally { if (generation == _marketGeneration) MarketRefresh.IsEnabled = true; }
    }
    private async void MarketRefresh_Click(object sender, RoutedEventArgs args) => await LoadMarketAsync(true);
    private void Market_Click(object sender, RoutedEventArgs args) { if (_price is { } price) FragmentPrice.Value = price.InputPriceMan; }
    private void Save_Click(object sender, RoutedEventArgs args)
    {
        if (_settings is null || _saving) return;
        try
        {
            var row = Draft(); if (!HuntingIncome.Valid(row)) return;
            Persist((_settings.HuntingIncomeRecords ?? []).Where(saved => saved.Ocid != row.Ocid || saved.Date != row.Date).Append(row).ToList(), row);
        }
        catch (Exception error) when (error is ArgumentException or OverflowException) { Status.Text = "입력값을 확인해주세요."; }
    }
    private void Delete_Click(object sender, RoutedEventArgs args)
    { if (_settings is not null && Existing is { } row) Persist(_settings.HuntingIncomeRecords.Where(saved => saved.Id != row.Id).ToList(), null); }
    private bool Persist(List<HuntingIncomeRecord> records, HuntingIncomeRecord? saved, string? successMessage = null)
    {
        if (_settings is null) return false;
        var previous = _settings.HuntingIncomeRecords; var bonus = _settings.HuntingMesoBonus; var price = _settings.HuntingFragmentPriceMan;
        _settings.HuntingIncomeRecords = records;
        if (saved is not null) { _settings.HuntingMesoBonus = saved.MesoBonus; _settings.HuntingFragmentPriceMan = (int)(saved.FragmentUnitPrice / 10000); }
        try { _saving = true; _settings.Save(); RecordsChanged?.Invoke(this, EventArgs.Empty); LoadDay(); RefreshRecords(); Status.Text = successMessage ?? (saved is null ? "기록을 삭제했어요." : "사냥 기록을 저장했어요."); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _settings.HuntingIncomeRecords = previous; _settings.HuntingMesoBonus = bonus; _settings.HuntingFragmentPriceMan = price; Status.Text = "저장하지 못했어요. 저장 공간과 접근 권한을 확인해주세요."; return false; }
        finally { _saving = false; Calculate(); }
    }
    private void RefreshRecords()
    {
        var characters = (CharacterInput.ItemsSource as HuntCharacter[] ?? []).ToDictionary(owner => owner.Ocid);
        Records.ItemsSource = (_settings?.HuntingIncomeRecords ?? []).Where(HuntingIncome.Valid).Where(row => characters.ContainsKey(row.Ocid))
            .OrderByDescending(row => row.Date).Select(row => new HuntRow(row, $"{row.Date:yyyy.MM.dd} · {characters[row.Ocid].Label} · {_display.Format(row.Total)} · 조각 {row.Fragments}개")).ToArray();
    }
    private void Record_Selected(object sender, SelectionChangedEventArgs args)
    {
        if (Records.SelectedItem is not HuntRow row) return;
        _ready = false;
        CharacterInput.SelectedItem = (CharacterInput.ItemsSource as HuntCharacter[] ?? []).FirstOrDefault(owner => owner.Ocid == row.Record.Ocid);
        DateInput.Date = new DateTimeOffset(row.Record.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        _ready = true; LoadDay(); _ = LoadMarketAsync();
    }
}
