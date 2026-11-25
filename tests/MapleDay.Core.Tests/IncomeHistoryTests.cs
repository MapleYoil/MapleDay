using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class IncomeHistoryTests
{
    [Fact]
    public void GameWeeksRunFromThursdayThroughWednesdayAndUseSundayMonth()
    {
        var week = IncomeHistory.Week(new DateOnly(2026, 11, 1));
        Assert.Equal(new DateOnly(2026, 10, 29), week.Start);
        Assert.Equal(new DateOnly(2026, 11, 4), week.End);
        Assert.Equal(new DateOnly(2026, 11, 1), week.Anchor);
        Assert.Equal(1, week.WeekOfMonth);
        Assert.Equal("2026년 11월 1주차 · 10.29~11.04", week.Label);
    }

    [Fact]
    public void MonthReturnsEveryGameWeekAnchoredInThatMonth()
    {
        var weeks = IncomeHistory.Month(new DateOnly(2026, 11, 1));
        Assert.Equal(5, weeks.Count);
        Assert.Equal(new DateOnly(2026, 10, 29), weeks[0].Start);
        Assert.Equal(new DateOnly(2026, 11, 26), weeks[^1].Start);
    }

    [Fact]
    public void LedgerTotalsValidateAndSumDistinctRecordsWithinPeriod()
    {
        var first = new IncomeExpenseRecord("a", new(2026, 11, 1), 20_000_000_000, "강화", "스타포스", "c");
        var second = new IncomeExpenseRecord("b", new(2026, 11, 2), 1_000_000_000, "코디");
        var sale = new IncomeExpenseRecord("sale", new(2026, 11, 3), 25_000_000_000, IncomeExpenses.SaleCategory);
        var outside = sale with { Id = "outside", Date = new(2026, 10, 31) };
        Assert.True(IncomeExpenses.Valid(first));
        var totals = IncomeExpenses.Totals([first, first, second, sale, sale, outside], new(2026, 11, 1), new(2026, 11, 30));
        Assert.Equal(21_000_000_000, totals.Spending);
        Assert.Equal(25_000_000_000, totals.Sales);
        Assert.Equal(4_000_000_000, totals.Net);
        Assert.False(IncomeExpenses.Valid(first with { Amount = 0 }));
        Assert.False(IncomeExpenses.Valid(first with { Category = "알 수 없음" }));
    }

    [Fact]
    public void HuntingNormalizationKeepsTheLatestLogicalDayRecord()
    {
        var date = new DateOnly(2026, 11, 1);
        var old = new HuntingIncomeRecord("old", "c", date, 700_000_000, 0, 10_000);
        var latest = old with { Id = "latest", Meso = 600_000_000 };
        Assert.Equal(latest, Assert.Single(HuntingIncome.Normalize([old, latest])));
    }
}
