using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class BossIncomeTests
{
    [Theory]
    [InlineData("스우", "normal", "2026-06-25", 16700000L)]
    [InlineData("스우", "normal", "2026-09-16", 16700000L)]
    [InlineData("스우", "normal", "2026-09-17", 8350000L)]
    [InlineData("검은 마법사", "hard", "2026-06-30", 700000000L)]
    [InlineData("검은 마법사", "hard", "2026-07-01", 665000000L)]
    [InlineData("검은 마법사", "hard", "2026-09-30", 665000000L)]
    [InlineData("검은 마법사", "hard", "2026-10-01", 465000000L)]
    [InlineData("검은마법사", "익스트림", "2026-10-06", 5680000000L)]
    [InlineData("벨로나", "hard", "2026-10-06", 2950000000L)]
    [InlineData("시즌 보스 찬란한 흉성", "normal", "2026-10-06", 576000000L)]
    public void PriceVersionsUseEffectiveDateAndGameNameAliases(string boss, string difficulty, string date, long expected)
        => Assert.Equal(expected, CrystalPrices.Find(boss, difficulty, DateOnly.Parse(date))!.Meso);

    [Fact]
    public void UnsupportedPricesAreNotInvented()
    {
        Assert.Null(CrystalPrices.Find("unknown", "normal", new(2026, 10, 6)));
        Assert.Null(CrystalPrices.Find("벨로나", "hard", new(2026, 8, 19)));
        Assert.All(CrystalPrices.All, price =>
        {
            Assert.True(Uri.TryCreate(price.Source, UriKind.Absolute, out var source));
            Assert.Equal("https", source.Scheme);
        });
    }

    [Fact]
    public void RepeatedFlagsDifficultiesAndUnregisteredClearsProduceOneRewardPerBoss()
    {
        var today = new DateOnly(2026, 10, 6);
        var records = Enumerable.Range(1, 6).Select(day => Snapshot(new(2026, 10, day),
            Boss("스우", true), Boss("스우", true, "hard"), Boss("데미안", true, registered: false)));
        var income = BossIncome.Calculate(records, today);
        Assert.Equal(2, income.Records.Count);
        Assert.Equal(48900000L + 8750000L, income.Total);
        Assert.All(income.Records, record => Assert.False(record.DateEstimated));
    }

    [Fact]
    public void DailyBossesNeverEnterAnyIncomeTotal()
    {
        var today = new DateOnly(2026, 10, 6);
        var income = BossIncome.Calculate([Snapshot(today, Boss("힐라", true, "hard", "daily"))], today);
        Assert.Empty(income.Records);
        Assert.Equal(0, income.Total);
    }

    [Fact]
    public void CapKeepsHighestTwelvePerCharacterPerThursdayWeek()
    {
        var bosses = CrystalPrices.All.Where(price => price.EffectiveFrom <= new DateOnly(2026, 10, 6)
            && price.Name != "검은 마법사").DistinctBy(price => SchedulerBossHistory.BossKey(price.Name)).Take(14)
            .Select(price => Boss(price.Name, true, price.Difficulty)).ToArray();
        var first = new DateOnly(2026, 10, 1);
        var today = first.AddDays(7);
        var result = BossIncome.Calculate([Snapshot(first, bosses), Snapshot(today, bosses)], today);
        Assert.Equal(24, result.Records.Count(record => record.Included));
        Assert.Equal(4, result.CapExcluded);
        var expected = bosses.Select(boss => CrystalPrices.Find(boss.Name!, boss.Difficulty, today)!.Meso).OrderDescending().Take(12).Sum();
        Assert.Equal(expected, result.Weekly);
        Assert.Equal(expected * 2, result.Total);
        var anotherCharacter = BossIncome.Calculate([Snapshot(today, bosses)], today);
        Assert.Equal(expected, anotherCharacter.Weekly);
    }

    [Fact]
    public void PartySplitIsFlooredAndDoesNotAffectAnotherRecord()
    {
        var date = new DateOnly(2026, 10, 1);
        var snapshots = new[] { Snapshot(date, Boss("스우", true), Boss("데미안", true)) };
        var result = BossIncome.Calculate(snapshots, date, id => id.EndsWith("icon_13.png") ? 3 : 1);
        Assert.Equal(8350000L / 3 + 8750000L, result.Total);
        Assert.Equal(1, BossIncome.Calculate(snapshots, date, _ => 0).Records[0].PartySize);
        Assert.Equal(6, BossIncome.Calculate(snapshots, date, _ => 10).Records[0].PartySize);
    }

    [Fact]
    public void ThreePersonBossIncomeClampsLegacySixPersonOverrides()
    {
        var date = new DateOnly(2026, 10, 1);
        var result = BossIncome.Calculate([Snapshot(date, Boss("벨로나", true, "hard"), Boss("스우", true))], date, _ => 6);
        var limited = result.Records.Single(record => record.Name == "벨로나");
        Assert.Equal(3, limited.PartySize);
        Assert.Equal(2950000000L / 3, limited.Meso);
        Assert.Equal(6, result.Records.Single(record => record.Name == "스우").PartySize);
    }

    [Fact]
    public void CalendarMonthIncludesWeeklyAndMonthlyRewardsWithoutMovingPriorMonthKills()
    {
        var sep30 = new DateOnly(2026, 9, 30);
        var oct1 = sep30.AddDays(1);
        var result = BossIncome.Calculate([Snapshot(sep30.AddDays(-1), Boss("스우", false), Boss("검은 마법사", false, "hard", "monthly")),
            Snapshot(sep30, Boss("스우", true), Boss("검은 마법사", true, "hard", "monthly")),
            Snapshot(oct1, Boss("스우", true), Boss("검은 마법사", true, "hard", "monthly"))], oct1);
        Assert.Equal(8350000L + 465000000L, result.Monthly);
        Assert.Equal(8350000L * 2 + 665000000L + 465000000L, result.Total);
        Assert.Equal(2, result.Periods(BossCycle.Monthly).Count);
    }

    [Fact]
    public void KnownFalseToTrueTransitionMakesDateExactAndUnknownGapIsExcluded()
    {
        var oct1 = new DateOnly(2026, 10, 1);
        var oct3 = oct1.AddDays(2);
        var exact = BossIncome.Calculate([Snapshot(oct1, Boss("스우", false)), Snapshot(oct1.AddDays(1), Boss("스우", false)),
            Snapshot(oct3, Boss("스우", true))], oct3);
        Assert.False(Assert.Single(exact.Records).DateEstimated);
        Assert.True(exact.CompleteRange(BossCycle.Weekly));
        var gap = BossIncome.Calculate([Snapshot(oct1, Boss("스우", false)), Snapshot(oct3, Boss("스우", true))], oct3);
        Assert.Empty(gap.Records);
        Assert.Equal(0, gap.Weekly);
        Assert.Equal(0, gap.Monthly);
        Assert.Equal(0, gap.Total);
        Assert.False(gap.CompleteRange(BossCycle.Monthly));
    }

    [Fact]
    public void EstimatedWeeklyAndMonthlyClearsDisappearUntilEarlierSnapshotConfirmsTheirDate()
    {
        var reset = new DateOnly(2026, 10, 1);
        var clearDate = reset.AddDays(3);
        var snapshots = new List<SchedulerSnapshot>
        {
            Snapshot(reset, Boss("스우", false), Boss("데미안", true), Boss("검은 마법사", false, "hard", "monthly")),
            Snapshot(clearDate, Boss("스우", true), Boss("데미안", true), Boss("검은 마법사", true, "hard", "monthly"))
        };
        var partial = BossIncome.Calculate(snapshots, clearDate);
        Assert.Equal("데미안", Assert.Single(partial.Records).Name);
        Assert.Equal(8750000L, partial.Weekly);
        Assert.Equal(8750000L, partial.Monthly);
        Assert.Equal(8750000L, partial.Total);
        Assert.All(partial.Periods(BossCycle.Monthly), period => Assert.Equal(1, period.Count));
        Assert.Equal(2, snapshots.Count);

        snapshots.Add(Snapshot(clearDate.AddDays(-1), Boss("스우", false), Boss("데미안", true),
            Boss("검은 마법사", false, "hard", "monthly")));
        var confirmed = BossIncome.Calculate(snapshots, clearDate);
        Assert.Equal(3, confirmed.Records.Count);
        Assert.All(confirmed.Records.Where(record => record.Name != "데미안"), record => Assert.Equal(clearDate, record.Date));
        Assert.Equal(8750000L + 8350000L + 465000000L, confirmed.Total);
    }

    [Fact]
    public void UnknownPriceIsReportedAndExcludedInsteadOfInventingIncome()
    {
        var date = new DateOnly(2026, 8, 20);
        var income = BossIncome.Calculate([Snapshot(date, Boss("없는 보스", true))], date);
        Assert.Equal(1, income.Unpriced);
        Assert.Null(income.Records[0].Meso);
        Assert.Equal(0, income.Total);
    }

    [Fact]
    public void CachedOldWeeksUseOldPricesAndRemainInCumulativeIncome()
    {
        var june = new DateOnly(2026, 6, 25);
        var oct = new DateOnly(2026, 10, 1);
        var result = BossIncome.Calculate([Snapshot(june, Boss("스우", true)), Snapshot(oct, Boss("스우", true))], oct);
        Assert.Equal(16700000L + 8350000L, result.Total);
        Assert.Equal(8350000L, result.Weekly);
        Assert.True(result.CompleteRange(BossCycle.Weekly));
        Assert.Equal("0.08억 메소", BossIncome.Money(result.Weekly));
    }

    [Fact]
    public void ObservedZeroIncomePeriodsRemainVisibleAndMissingDaysAreMarkedPartial()
    {
        var today = new DateOnly(2026, 10, 3);
        var result = BossIncome.Calculate([Snapshot(new(2026, 10, 1)), Snapshot(today)], today);
        var period = Assert.Single(result.Periods(BossCycle.Weekly));
        Assert.Equal(0, period.Meso);
        Assert.False(period.CompleteRange);
    }

    [Fact]
    public void NewestSameDaySnapshotWinsWithoutDoubleCounting()
    {
        var today = new DateOnly(2026, 10, 1);
        var stale = Snapshot(today, Boss("스우", false)) with { FetchedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var result = BossIncome.Calculate([Snapshot(today, Boss("스우", true)), stale], today);
        Assert.Equal(8350000L, result.Total);
        Assert.Single(result.Records);
    }

    private static SchedulerBoss Boss(string name, bool clear, string difficulty = "normal", string cycle = "weekly", bool registered = true)
        => new() { Name = name, Cycle = cycle, Difficulty = difficulty, Complete = JsonSerializer.SerializeToElement(clear), Registration = JsonSerializer.SerializeToElement(registered) };
    private static SchedulerSnapshot Snapshot(DateOnly date, params SchedulerBoss[] bosses)
        => new(date, new SchedulerState { Name = "캐릭터", Bosses = bosses.ToList() }, true, DateTimeOffset.UtcNow);
}
