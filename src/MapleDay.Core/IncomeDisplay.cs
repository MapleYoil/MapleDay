using System.Globalization;

namespace MapleDay.Core;

public sealed record IncomeDisplay(string Mode = "meso", decimal WonPerHundredMillion = 1500)
{
    public string ValidMode => Mode is "cash" or "both" ? Mode : "meso";
    public decimal Rate => WonPerHundredMillion is >= 1 and <= 1_000_000_000 ? WonPerHundredMillion : 1500;
    public decimal Cash(long meso) => decimal.Round(meso / 100_000_000m * Rate, 0, MidpointRounding.AwayFromZero);
    public string CashText(long meso) => Cash(meso).ToString("N0", CultureInfo.GetCultureInfo("ko-KR")) + "원";
    public string Format(long meso, bool compact = false)
    {
        var amount = compact ? IncomeCalendar.CompactMoney(meso) : BossIncome.Money(meso);
        return ValidMode switch { "cash" => CashText(meso), "both" => amount + (compact ? "\n" : " · ") + CashText(meso), _ => amount };
    }
}
