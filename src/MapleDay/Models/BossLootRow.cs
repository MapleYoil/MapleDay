using MapleDay.Core;
namespace MapleDay.Models;
public sealed class BossLootRow(BossLootRecord record, string characterName, IncomeDisplay display, bool canEdit = true)
{
    public bool CanEdit => canEdit;
    public BossLootRecord Record => record;
    public string Name => record.ItemName;
    public string? IconFile => BossLootCatalog.Items.FirstOrDefault(item => item.Id == record.ItemId)?.Icon;
    public string Details => $"{record.Date:yyyy.MM.dd} · {characterName} · {record.Boss}" + (!string.IsNullOrEmpty(record.Difficulty) ? " · " + BossLootCatalog.DifficultyLabel(record.Difficulty) : "");
    public string Distribution => BossLoot.Distribution(record);
    public string Amount => "내 수익 " + display.Format(record.Received);
}
