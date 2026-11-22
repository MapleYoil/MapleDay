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
    private AppSettings? _settings;
    private IncomeDisplay _display = new();
    private string? _selectedOcid;
    private string? _editingId;
    private bool _ready;
    private bool _formatting;
    private bool _deleteOpen;

    public IncomeExpenseView()
    {
        InitializeComponent();
        CategoryInput.ItemsSource = IncomeExpenses.Categories;
        CategoryInput.SelectedIndex = 0;
    }

    public void SetContext(AppSettings settings, IEnumerable<SchedulerCharacter> characters, string? selectedOcid, IncomeDisplay display)
    {
        _ready = false; _settings = settings; _display = display; _selectedOcid = selectedOcid;
        var choices = new[] { new ExpenseCharacter("", "공통 지출") }.Concat(characters.Select(character => new ExpenseCharacter(character.Ocid, character.Name))).ToArray();
        var previous = (CharacterInput.SelectedItem as ExpenseCharacter)?.Ocid;
        CharacterInput.ItemsSource = choices;
        CharacterInput.SelectedItem = choices.FirstOrDefault(choice => choice.Ocid == (previous ?? selectedOcid ?? "")) ?? choices[0];
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
        DateInput.MinDate = new DateTimeOffset(new DateOnly(2003, 4, 29).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        DateInput.MaxDate = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        DateInput.Date ??= DateInput.MaxDate;
        _ready = true; RefreshRecords(); UpdateAmountHint();
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
        AmountHint.Text = valid ? BossIncome.Money(amount) : "사용한 메소 금액을 입력하세요.";
        SaveButton.IsEnabled = valid && DateInput.Date is not null && CharacterInput.SelectedItem is ExpenseCharacter && CategoryInput.SelectedItem is string;
    }

    private IncomeExpenseRecord Draft()
    {
        if (DateInput.Date is null || CharacterInput.SelectedItem is not ExpenseCharacter character || CategoryInput.SelectedItem is not string category
            || !BossLoot.TryParseAmount(AmountInput.Text, out var amount) || amount <= 0)
            throw new ArgumentException("날짜와 사용한 금액을 확인해주세요.");
        var record = new IncomeExpenseRecord(_editingId ?? Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateInput.Date.Value.DateTime), amount,
            category, MemoInput.Text.Trim(), character.Ocid);
        if (!IncomeExpenses.Valid(record)) throw new ArgumentException("지출 금액과 메모를 확인해주세요.");
        return record;
    }

    private bool Persist(List<IncomeExpenseRecord> records, string message)
    {
        if (_settings is null) return false;
        var previous = _settings.IncomeExpenseRecords;
        _settings.IncomeExpenseRecords = records.Where(IncomeExpenses.Valid).DistinctBy(record => record.Id).ToList();
        try
        {
            _settings.Save(); RefreshRecords(); Status.Text = message; RecordsChanged?.Invoke(this, EventArgs.Empty); return true;
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
            if (Persist(_settings.IncomeExpenseRecords.Where(saved => saved.Id != record.Id).Append(record).ToList(), _editingId is null ? "지출을 기록했어요." : "지출 기록을 수정했어요."))
                Load(record);
        }
        catch (ArgumentException error) { Status.Text = error.Message; }
    }

    private void New_Click(object sender, RoutedEventArgs args) => ClearEditor();

    private async void Delete_Click(object sender, RoutedEventArgs args)
    {
        if (_deleteOpen || _settings is null || _editingId is null) return;
        var record = _settings.IncomeExpenseRecords.FirstOrDefault(item => item.Id == _editingId);
        if (record is null) { ClearEditor(); return; }
        _deleteOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = RequestedTheme, Title = "지출 기록 삭제",
                Content = $"{record.Date:yyyy.MM.dd}의 {record.Amount:N0} 메소 지출 기록을 삭제할까요?", PrimaryButtonText = "삭제", CloseButtonText = "취소", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && Persist(_settings.IncomeExpenseRecords.Where(item => item.Id != record.Id).ToList(), "지출 기록을 삭제했어요.")) ClearEditor();
        }
        finally { _deleteOpen = false; }
    }

    private void Record_Selected(object sender, SelectionChangedEventArgs args)
    {
        if (Records.SelectedItem is ExpenseRow row) Load(row.Record);
    }

    private void Load(IncomeExpenseRecord record)
    {
        _editingId = record.Id; _ready = false;
        DateInput.Date = new DateTimeOffset(record.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
        CharacterInput.SelectedItem = (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault(choice => choice.Ocid == record.Ocid)
            ?? (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault();
        CategoryInput.SelectedItem = record.Category; AmountInput.Text = record.Amount.ToString("N0", CultureInfo.CurrentCulture); MemoInput.Text = record.Memo;
        _ready = true; SaveButton.Content = "수정 저장"; NewButton.Visibility = DeleteButton.Visibility = Visibility.Visible; UpdateAmountHint();
    }

    private void ClearEditor()
    {
        _editingId = null; Records.SelectedItem = null; _ready = false;
        AmountInput.Text = "0"; MemoInput.Text = ""; CategoryInput.SelectedIndex = 0;
        CharacterInput.SelectedItem = (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault(choice => choice.Ocid == (_selectedOcid ?? ""))
            ?? (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).FirstOrDefault();
        _ready = true; SaveButton.Content = "지출 저장"; NewButton.Visibility = DeleteButton.Visibility = Visibility.Collapsed; UpdateAmountHint();
    }

    private void RefreshRecords()
    {
        if (_settings is null) return;
        var names = (CharacterInput.ItemsSource as ExpenseCharacter[] ?? []).ToDictionary(choice => choice.Ocid, choice => choice.Label);
        Records.ItemsSource = _settings.IncomeExpenseRecords.Where(IncomeExpenses.Valid)
            .Where(record => _selectedOcid is null || record.Ocid == _selectedOcid)
            .OrderByDescending(record => record.Date).ThenByDescending(record => record.Id)
            .Select(record => new ExpenseRow(record, $"{record.Date:yyyy.MM.dd} · {record.Category} · {_display.Format(record.Amount)}"
                + (record.Ocid.Length > 0 && names.TryGetValue(record.Ocid, out var name) ? $" · {name}" : "")
                + (record.Memo.Length > 0 ? $" · {record.Memo}" : ""))).ToArray();
    }
}
