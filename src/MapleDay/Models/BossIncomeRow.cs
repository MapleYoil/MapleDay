using MapleDay.Core;

namespace MapleDay.Models;

public sealed class BossIncomeRow(string ocid, BossIncomeRecord record)
{
    public string Ocid => ocid;
    public string Id => record.Id;
    public int PartySize => record.PartySize;
    public string Name => $"{record.Name} · {record.DifficultyLabel}";
    public string Date => $"{record.Date:yyyy-MM-dd}";
    public string Party => $"파티 {record.PartySize}인";
    public string Amount => record.Meso is null ? "가격 확인 필요" : BossIncome.Money(record.Meso.Value);
    public string Status => !record.Included ? "주간 12개 상한에서 제외" : record.Price is null ? "미확인 가격 또는 가격 변경 구간 · 합산 제외" : "수익에 포함";
    public string Price => record.Price is { } price ? $"1인 가격 {BossIncome.Money(price.Meso)} · {price.EffectiveFrom:yyyy-MM-dd} 가격표" : "해당 기간의 가격을 확정할 수 없어요.";
    public Uri? Source => record.Price is { } price ? new Uri(price.Source) : null;
}

public sealed class IncomeContributionRow(string ocid, string name, long meso, long total)
{
    public string Ocid => ocid;
    public string Name => name;
    public string Amount => BossIncome.Money(meso);
    public double Percent => total > 0 ? (double)meso / total * 100 : 0;
    public string Share => $"{Percent:0.0}%";
    public string AccessibleName => $"{Name}, {Amount}, 전체 주간 수익의 {Share}";
}
