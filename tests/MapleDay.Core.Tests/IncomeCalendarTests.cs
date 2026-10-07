using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class IncomeCalendarTests
{
    [Theory]
    [InlineData(630_000_000L, "+6.3억")]
    [InlineData(100_000_000L, "+1억")]
    [InlineData(12_300_000L, "+1230만")]
    [InlineData(700L, "+700")]
    [InlineData(0L, "+0")]
    public void CalendarUsesCompactKoreanIncome(long amount, string expected) => Assert.Equal(expected, IncomeCalendar.CompactMoney(amount));

    [Fact]
    public void CalendarOmitsEstimatedAndDailyBossesAndKeepsExactDetailsWithoutCountingCapExcluded()
    {
        var date = new DateOnly(2026, 10, 6);
        CalendarBossRecord Record(string ocid, long amount, bool included = true, BossCycle cycle = BossCycle.Weekly, bool estimated = false) =>
            new(ocid, ocid, new BossIncomeRecord(ocid, "스우", "hard", cycle, date, date, date.AddDays(-1), estimated, null, 3, amount, included));
        var records = new[] { Record("first", 300_000_000), Record("second", 330_000_000, estimated: true), Record("excluded", 999, false), Record("daily", 999, cycle: BossCycle.Daily) };
        IReadOnlySet<DateOnly>[] known = [new HashSet<DateOnly> { date }, new HashSet<DateOnly> { date }];
        var month = IncomeCalendar.Month(date, records, known);
        Assert.Equal(42, month.Count);
        Assert.Equal(DayOfWeek.Sunday, month[0].Date.DayOfWeek);
        Assert.Equal(31, month.Count(day => day.InMonth));
        var day = Assert.Single(month, day => day.Date == date);
        Assert.Equal(300_000_000, day.Meso);
        Assert.Equal(2, day.Bosses.Count);
        Assert.Equal(2, day.KnownCharacters);
        Assert.DoesNotContain(day.Bosses, record => record.Boss.DateEstimated);
        Assert.Equal(3, day.Bosses[0].Boss.PartySize);
        Assert.Contains(day.Bosses, record => !record.Boss.Included);
        Assert.Equal(0, month.Single(day => day.Date == date.AddDays(1)).KnownCharacters);
    }
    [Fact]
    public void LeapMonthAndUnknownDatesDoNotInventIncome()
    {
        var month = IncomeCalendar.Month(new DateOnly(2028, 2, 1), [], []);
        Assert.Equal(29, month.Count(day => day.InMonth));
        Assert.All(month, day => { Assert.Equal(0, day.Meso); Assert.Empty(day.Bosses); Assert.Equal(0, day.KnownCharacters); });
    }
}
