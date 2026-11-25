using System.Globalization;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapleDay.Views;

public sealed record ExpenseCharacter(string Ocid, string Label);
public sealed record ExpenseRow(IncomeExpenseRecord Record, string Label) { public override string ToString() => Label; }

public sealed partial class IncomeExpenseView : UserControl
{
    public event EventHandler? RecordsChanged;
    public event EventHandler<DateOnly>? DateSelected;
    private AppSettings? _settings;
    private IncomeDisplay _display = new();
    private string? _selectedOcid;
    private string? _editingId;
    private bool _ready;
    private bool _formatting;
    private bool _deleteOpen;
    private DateOnly? Date => DateInput.Date is { } date ? DateOnly.FromDateTime(date.DateTime) : null;
    private bool IsIncome => KindInput.SelectedIndex == 1;

    public IncomeExpenseView()
    {
        InitializeComponent();
        KindInput.SelectedIndex = 0;
        SetKindFields();
    }

    public void SetContext(AppSettings settings, IEnumerable<SchedulerCharacter> characters, string? selectedOcid, IncomeDisplay display)
    {
        var filterChanged = _selectedOcid != selectedOcid;
        _ready = false; _settings = settings; _display = display; _selectedOcid = selectedOcid;
        var choices = (selectedOcid is null ? new[] { new ExpenseCharacter("", "공통") } : [])
            .Concat(characters.Select(character => new ExpenseCharacter(character.Ocid, character.Name))).ToList();
        var known = choices.Select(choice => choice.Ocid).ToHashSet(StringComparer.Ordinal);
        choices.AddRange(settings.IncomeExpenseRecords.Where(record => record.Ocid.Length > 0 && (_selectedOcid is null || _selectedOcid == record.Ocid))
            .Where(record => known.Add(record.Ocid)).Select(record => new ExpenseCharacter(record.Ocid, "이전 캐릭터")));
        var previous = (CharacterInput.SelectedItem as ExpenseCharacter)?.Ocid;
        CharacterInput.ItemsSource = choices.ToArray();
        CharacterInput.SelectedItem = choices.FirstOrDefault(choice => choice.Ocid == (previous ?? selectedOcid ?? "")) ?? choices.FirstOrDefault();
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        DateInput.MinDate = new DateTimeOffset(IncomeExpenses.FirstDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        DateInput.MaxDate = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        DateInput.Date ??= DateInput.MaxDate;
        _ready = true;
        if (filterChanged || _editingId is not null && !settings.IncomeExpenseRecords.Any(record => record.Id == _editingId && record.Date == Date))
            ClearEditor();
        RefreshRecords();
        UpdateAmountHint();
    }

    public void SelectDate(DateOnly date)
    {
        if (!_ready || Date == date) return;
        _ready = false;
        DateInput.Date = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        _ready = true;
        SwitchDay();
    }

    private void Date_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_ready) return;
        SwitchDay();
        if (Date is { } date) DateSelected?.Invoke(this, date);
    }

    private void SwitchDay()
    {
        ClearEditor();
        Status.Text = "";
        RefreshRecords();
    }

    private void Kind_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!_ready) return;
        SetKindFields();
        UpdateAmountHint();
    }

    private void SetKindFields()
    {
        CategoryInput.ItemsSource = IsIncome ? new[] { IncomeExpenses.SaleCategory } : IncomeExpenses.Categories;
        CategoryInput.SelectedIndex = 0;
        AmountInput.Header = IsIncome ? "실제로 받은 금액 (메소)" : "사용한 금액 (메소)";
        MemoInput.PlaceholderText = IsIncome ? "예: 이전 보조무기 판매" : "예: 장비 구매, 스타포스 강화";
        SaleHint.Visibility = IsIncome ? Visibility.Visible : Visibility.Collapsed;
        if (_editingId is null) SaveButton.Content = IsIncome ? "수입 추가" : "지출 추가";
    }

    private void AmountInput_Changed(object sender, TextChangedEventArgs args)
    {
        if (_formatting || !_ready) return;
        var formatted = IntegerInputFormatter.Format(AmountInput.Text, AmountInput.SelectionStart);
        if (formatted.Text != AmountInput.Text)
        {
            _formatting = true; AmountInput.Text = formatted.Text; AmountInput.SelectionStart = formatted.Caret;
            AmountInput.SelectionLength = 0; _formatting = false;
        }
        UpdateAmountHint();
    }

    private void UpdateAmountHint()
    {
        var valid = BossLoot.TryParseAmount(AmountInput.Text, out var amount) && amount > 0;
        AmountHint.Text = valid ? BossIncome.Money(amount) : IsIncome ? "실제로 받은 메소 금액을 입력하세요." : "사용한 메소 금액을 입력하세요.";
        SaveButton.IsEnabled = valid && DateInput.Date is not null && CharacterInput.SelectedItem is ExpenseCharacter && CategoryInput.SelectedItem is string;
    }

    private IncomeExpenseRecord Draft()
    {
        if (Date is not { } date || CharacterInput.SelectedItem is not ExpenseCharacter character || CategoryInput.SelectedItem is not string category
            || !BossLoot.TryParseAmount(AmountInput.Text, out var amount) || amount <= 0)
            throw new ArgumentException("날짜와 메소 금액을 확인해주세요.");
        var record = new IncomeExpenseRecord(_editingId ?? Guid.NewGuid().ToString("N"), date, amount,
            category, MemoInput.Text.Trim(), character.Ocid);
        if (!IncomeExpenses.Valid(record)) throw new ArgumentException("분류와 금액, 메모를 확인해주세요.");
        return record;
    }

    private bool Persist(List<IncomeExpenseRecord> records)
    {
        if (_settings is null) return false;
        var previous = _settings.IncomeExpenseRecords;
        _settings.IncomeExpenseRecords = records.Where(IncomeExpenses.Valid).DistinctBy(record => record.Id).ToList();
        try
        {
            _settings.Save();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settings.IncomeExpenseRecords = previous; Status.Text = "저장하지 못했어요. 저장 공간과 접근 권한을 확인해주세요."; return false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs args)
    {
        if (_settings is null) return;
        try
        {
            var record = Draft();
            var message = _editingId is null ? "가계부에 기록했어요." : "가계부 기록을 수정했어요.";
            if (Persist(_settings.IncomeExpenseRecords.Where(saved => saved.Id != record.Id).Append(record).ToList()))
                FinishChange(message, keepIncomeKind: record.IsIncome);
        }
        catch (ArgumentException error) { Status.Text = error.Message; }
    }

    private void New_Click(object sender, RoutedEventArgs args)
    {
        ClearEditor();
        Status.Text = "";
    }

    private async void Delete_Click(object sender, RoutedEventArgs args)
    {
        if (_deleteOpen || _settings is null || _editingId is null) return;
        var record = _settings.IncomeExpenseRecords.FirstOrDefault(item => item.Id == _editingId);
        if (record is null) { ClearEditor(); return; }
        _deleteOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "가계부 기록 삭제",
                Content = $"{record.Date:yyyy.MM.dd}의 {record.Category} {record.Amount:N0} 메소 기록을 삭제할까요?", PrimaryButtonText = "삭제", CloseButtonText = "취소", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && Persist(_settings.IncomeExpenseRecords.Where(item => item.Id != record.Id).ToList())) FinishChange("가계부 기록을 삭제했어요.");
        }
        finally { _deleteOpen = false; }
    }

    private void Record_Selected(object sender, SelectionChangedEventArgs args)
    {
        if (_ready && Records.SelectedItem is ExpenseRow row) Load(row.Record);
    }

    private void Load(IncomeExpenseRecord record)
    {
        _editingId = record.Id; _ready = false;
        CharacterInput.SelectedItem = (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault(choice => choice.Ocid == record.Ocid)
            ?? (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault();
        KindInput.SelectedIndex = record.IsIncome ? 1 : 0;
        SetKindFields();
        CategoryInput.SelectedItem = record.Category; AmountInput.Text = record.Amount.ToString("N0", CultureInfo.CurrentCulture); MemoInput.Text = record.Memo;
        EditorHeading.Text = "가계부 기록 수정";
        Status.Text = "";
        _ready = true; SaveButton.Content = "수정 저장"; NewButton.Visibility = DeleteButton.Visibility = Visibility.Visible; UpdateAmountHint();
    }

    private void FinishChange(string message, bool keepIncomeKind = false)
    {
        ClearEditor(keepIncomeKind);
        RefreshRecords();
        Status.Text = message;
        RecordsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearEditor(bool keepIncomeKind = false)
    {
        _ready = false; _editingId = null; Records.SelectedItem = null;
        KindInput.SelectedIndex = keepIncomeKind ? 1 : 0;
        SetKindFields();
        AmountInput.Text = "0"; MemoInput.Text = "";
        CharacterInput.SelectedItem = (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault(choice => choice.Ocid == (_selectedOcid ?? ""))
            ?? (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault();
        EditorHeading.Text = "새 가계부 기록";
        _ready = true; NewButton.Visibility = DeleteButton.Visibility = Visibility.Collapsed; UpdateAmountHint();
    }

    private void RefreshRecords()
    {
        if (_settings is null) return;
        var names = (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).ToDictionary(choice => choice.Ocid, choice => choice.Label);
        var editorId = _editingId;
        var rows = _settings.IncomeExpenseRecords.Where(IncomeExpenses.Valid)
            .Where(record => record.Date == Date && (_selectedOcid is null || record.Ocid == _selectedOcid))
            .Reverse()
            .Select(record => new ExpenseRow(record, $"{record.Category} · {(record.IsIncome ? "+" : "")}{_display.Format(record.NetAmount)}"
                + (record.Ocid.Length > 0 && names.TryGetValue(record.Ocid, out var name) ? $" · {name}" : "")
                + (record.Memo.Length > 0 ? $" · {record.Memo}" : ""))).ToArray();
        _ready = false;
        Records.ItemsSource = rows;
        Records.SelectedItem = rows.FirstOrDefault(row => row.Record.Id == editorId);
        _ready = true;
        RecordsHeading.Visibility = Records.Visibility = rows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
