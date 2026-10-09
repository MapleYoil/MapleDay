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

    [Theory]
    [InlineData(IncomeCalendarCategory.All, 460_000_000L, 1, 1, 2, 1)]
    [InlineData(IncomeCalendarCategory.Boss, 400_000_000L, 1, 1, 0, 1)]
    [InlineData(IncomeCalendarCategory.Hunting, 60_000_000L, 0, 0, 2, 0)]
    public void CategoryCalendarsSeparateBossLootAndHunting(IncomeCalendarCategory category, long amount,
        int bossCount, int lootCount, int huntCount, int knownCount)
    {
        var date = new DateOnly(2026, 10, 9);
        var boss = new BossIncomeRecord("스우", "스우", "hard", BossCycle.Weekly, date, date, date.AddDays(-1), false, null, 1, 300_000_000, true);
        var loot = new BossLootRecord("loot", "first", date, "스우", "", "루즈 컨트롤 머신 마크", "received", 100_000_000, 1);
        CalendarHuntingRecord[] hunting = [
            new("첫 캐릭터", new("hunt1", "first", date, 20_000_000, 10, 1_000_000)),
            new("둘째 캐릭터", new("hunt2", "second", date, 10_000_000, 20, 1_000_000))
        ];
        IReadOnlySet<DateOnly>[] known = [new HashSet<DateOnly> { date }];
        var day = IncomeCalendar.Month(date, [new("first", "첫 캐릭터", boss)], known,
            [new("첫 캐릭터", loot)], hunting, category).Single(day => day.Date == date);
        Assert.Equal(amount, day.Meso);
        Assert.Equal(bossCount, day.Bosses.Count);
        Assert.Equal(lootCount, day.Loot!.Count);
        Assert.Equal(huntCount, day.Hunting!.Count);
        Assert.Equal(knownCount, day.KnownCharacters);
    }

    [Fact]
    public void HistoricalHuntingIncludesFragmentOnlyAndMesoOnlyDaysWithoutDuplicatesOrInvalidRecords()
    {
        var date = new DateOnly(2026, 1, 31);
        var fragmentOnly = new HuntingIncomeRecord("fragments", "c", date, 0, 10, 5_990_000);
        var mesoOnly = new HuntingIncomeRecord("meso", "c", date.AddDays(1), 50_000_000, 0, 10_000);
        CalendarHuntingRecord[] records = [new("캐릭터", fragmentOnly), new("캐릭터", fragmentOnly),
            new("캐릭터", mesoOnly), new("캐릭터", mesoOnly with { Id = "invalid", Meso = -1 })];
        var month = IncomeCalendar.Month(date, [], [], huntingRecords: records, category: IncomeCalendarCategory.Hunting);
        var last = month.Single(day => day.Date == date);
        Assert.True(last.InMonth);
        Assert.Equal(59_900_000, last.Meso);
        Assert.Single(last.Hunting!);
        var following = month.Single(day => day.Date == date.AddDays(1));
        Assert.False(following.InMonth);
        Assert.Equal(50_000_000, following.Meso);
        Assert.Equal(HuntingIncome.Sum(records.Select(item => item.Hunting), new(2026, 1, 1), date),
            month.Where(day => day.InMonth).Sum(day => day.Meso));
    }
}
