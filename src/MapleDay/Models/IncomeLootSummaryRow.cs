using MapleDay.Core;

namespace MapleDay.Models;

public sealed class IncomeLootSummaryRow
{
    private readonly IncomeDisplay _display;
    public IncomeLootSummaryRow(IReadOnlyList<BossLootRecord> records, IncomeDisplay display)
    {
        Records = records;
        _display = display;
    }

    public IReadOnlyList<BossLootRecord> Records { get; }
    public string Name => Records[0].ItemName;
    public int Count => Records.Count;
    public string CountText => Count.ToString("N0") + "개";
    public long Received => Records.Sum(record => record.Received);
    public string Amount => _display.Format(Received);
    public string? IconFile => BossLootCatalog.Items.FirstOrDefault(item => item.Id == Records[0].ItemId)?.Icon;
    public string Detail => string.Join("\n", Records.OrderByDescending(record => record.Date)
        .Select(record => $"{record.Date:yyyy.MM.dd} · {record.Boss} · {_display.Format(record.Received)}"));
}
