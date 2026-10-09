using System.Text.Json;
using MapleDay.Core;
namespace MapleDay.Core.Tests;
public class LootAndForecastTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static SchedulerBoss Boss(string name, string difficulty = "normal", bool complete = false, bool registered = true, string cycle = "weekly")
        => new() { Name = name, Difficulty = difficulty, Cycle = cycle, Complete = JsonSerializer.SerializeToElement(complete), Registration = JsonSerializer.SerializeToElement(registered) };
    [Fact]
    public void ForecastUsesOnlyEligibleRegisteredWeeklyBossesAndPersonalShare()
    {
        var state = new SchedulerState { Bosses = [Boss("스우"), Boss("데미안", registered: false), Boss("힐라", cycle: "daily"), Boss("검은 마법사", "hard", cycle: "monthly"), Boss("벨로나", "hard")] };
        var result = WeeklyIncomeForecast.Calculate(state, [], [], Today, _ => 6, 291);
        Assert.Equal(2, result.Bosses.Count);
        Assert.Equal(8_350_000L / 6 + 2_950_000_000L / 3, result.Meso);
        Assert.DoesNotContain(WeeklyIncomeForecast.Calculate(state, [], [], Today, level: 190).Bosses, boss => boss.Name == "벨로나");
    }
    [Fact]
    public void CompletedUnregisteredAndManualBossesConsumeSlotsWithoutDuplicateDifficulty()
    {
        var state = new SchedulerState { Bosses = [Boss("스우"), Boss("데미안"), Boss("루시드", "hard"), Boss("윌", "hard")], WeeklyBossClearCount = 11 };
        var snapshot = new SchedulerSnapshot(Today.AddDays(-1), new SchedulerState { Bosses = [Boss("스우", complete: true, registered: false), Boss("스우", "hard", true)] }, true, DateTimeOffset.UtcNow);
        var records = new[] { new BossIncomeRecord("manual", "데미안", "normal", BossCycle.Weekly,
            SchedulerBossHistory.Start(BossCycle.Weekly, Today), Today, Today, false, null, 1, 8_750_000, true) { Manual = true } };
        var result = WeeklyIncomeForecast.Calculate(state, [snapshot], records, Today);
        Assert.Equal(1, result.RemainingSlots); Assert.Equal(2, result.Bosses.Count);
        Assert.Single(result.Bosses, boss => boss.Included);
        Assert.Equal(result.Bosses.Max(boss => boss.Meso), result.Meso);
    }
    [Fact]
    public void ThursdayResetDropsLastWeekAndHighestDifficultyWins()
    {
        var state = new SchedulerState { Bosses = [Boss("스우"), Boss("스우", "hard")] };
        var previous = new SchedulerSnapshot(new(2026, 10, 7), new SchedulerState { Bosses = [Boss("스우", complete: true)] }, true, DateTimeOffset.UtcNow);
        var result = WeeklyIncomeForecast.Calculate(state, [previous], [], Today);
        Assert.Equal(48_900_000, result.Meso); Assert.Single(result.Bosses);
        Assert.Equal(12, result.RemainingSlots);
    }
    [Fact]
    public void UnknownPriceIsReportedAndCapIsPerCharacter()
    {
        var state = new SchedulerState { Bosses = [Boss("새 보스"), Boss("스우")], WeeklyBossClearCount = 12 };
        var result = WeeklyIncomeForecast.Calculate(state, [], [], Today);
        Assert.Equal(0, result.Meso); Assert.Equal(1, result.Unpriced); Assert.All(result.Bosses, boss => Assert.False(boss.Included));
        Assert.Equal(8_350_000, WeeklyIncomeForecast.Calculate(new() { Bosses = [Boss("스우")] }, [], [], Today).Meso);
    }
    [Theory]
    [InlineData("equal", 100L, 3, "", 1, 33L)]
    [InlineData("received", 100L, 6, "", 1, 100L)]
    [InlineData("ratio", 100L, 3, "2:1:1", 1, 50L)]
    [InlineData("ratio", 101L, 3, "2:1:1", 2, 25L)]
    [InlineData("ratio", 100L, 2, "0.5:1.5", 2, 75L)]
    [InlineData("ratio", 100L, 2, "0:1", 1, 0L)]
    [InlineData("ratio", 1000000000000000L, 2, "999999:1", 1, 999999000000000L)]
    public void DistributionCalculatesPersonalWholeMeso(string mode, long amount, int party, string weights, int member, long expected)
        => Assert.Equal(expected, BossLoot.Calculate(mode, amount, party, weights, member));
    [Theory]
    [InlineData("equal", -1L, 3, "", 1)]
    [InlineData("received", 1000000000000001L, 1, "", 1)]
    [InlineData("equal", 100L, 0, "", 1)]
    [InlineData("ratio", 100L, 3, "1:1", 1)]
    [InlineData("ratio", 100L, 2, "0:0", 1)]
    [InlineData("ratio", 100L, 2, "-1:2", 1)]
    [InlineData("ratio", 100L, 2, "1:2", 3)]
    [InlineData("ratio", 100L, 2, "1,000:2", 1)]
    [InlineData("other", 100L, 2, "", 1)]
    public void InvalidSettlementIsRejected(string mode, long amount, int party, string weights, int member)
        => Assert.Throws<ArgumentException>(() => BossLoot.Calculate(mode, amount, party, weights, member));
    [Fact]
    public void ManualLootCanPredateApiAndCalendarCountsOnlyReceipt()
    {
        var day = new DateOnly(2026, 1, 1);
        var good = new BossLootRecord("id", "c", day, "스우", "", "루즈 컨트롤 머신 마크", "equal", 100, 3);
        var invalid = good with { Id = "bad", Boss = "벨로나", PartySize = 6 };
        Assert.False(BossLoot.Valid(invalid));
        var calendar = IncomeCalendar.Month(day, [], [], [new("테스트", good), new("테스트", invalid)]).Single(row => row.Date == day);
        Assert.Equal(33, calendar.Meso); Assert.Single(calendar.Loot!); Assert.Equal(0, calendar.KnownCharacters);
        Assert.Equal(33, BossLoot.Sum([good, invalid, good with { Date = day.AddMonths(1) }], day, day));
    }
    [Fact]
    public void DifficultySpecificDropsDoNotLeakFromHarderOrLowerModes()
    {
        Assert.Empty(BossLootCatalog.ForBoss("스우", "normal"));
        Assert.Equal(new[] { "루즈 컨트롤 머신 마크", "리스트레인트 링 4레벨", "컨티뉴어스 링 4레벨" }.Order(), BossLootCatalog.ForBoss("스우", "하드").Select(item => item.Name).Order());
        Assert.Equal(4, BossLootCatalog.ForBoss("스우", "extreme").Count);
        Assert.Empty(BossLootCatalog.ForBoss("루시드", "easy"));
        Assert.DoesNotContain(BossLootCatalog.ForBoss("루시드", "normal"), item => item.Name == "몽환의 벨트");
        Assert.DoesNotContain(BossLootCatalog.ForBoss("찬란한 흉성", "hard"), item => item.Name == "황홀한 환상의 단편 조각");
        Assert.All(BossLootCatalog.ForBoss("벨로나", "easy"), item => Assert.EndsWith("링 4레벨", item.Name));
        var item = BossLootCatalog.ForBoss("스우", "hard").Single(item => item.Name == "루즈 컨트롤 머신 마크");
        var record = new BossLootRecord("id", "c", Today, "스우", item.Id, item.Name, "equal", 100, 1, Difficulty: "normal");
        Assert.False(BossLoot.Valid(record)); Assert.True(BossLoot.Valid(record with { Difficulty = "hard" }));
    }
    [Fact]
    public void EveryCatalogRewardHasAnExplicitSupportedDifficulty()
    {
        foreach (var item in BossLootCatalog.Items)
            foreach (var boss in item.Bosses)
                Assert.Contains(BossLootCatalog.Difficulties(boss), difficulty => BossLootCatalog.ForBoss(boss, difficulty).Any(reward => reward.Id == item.Id));
        Assert.Empty(BossLootCatalog.ForBoss("스우", "unknown"));
    }
    [Fact]
    public void ExtractedCatalogHasUniqueSafeResourcesAndMatchesBossAliases()
    {
        Assert.Equal(52, BossLootCatalog.Items.Count);
        Assert.Equal(52, BossLootCatalog.Items.DistinctBy(item => item.Id).Count());
        Assert.All(BossLootCatalog.Items, item => { Assert.NotEmpty(item.Bosses); Assert.DoesNotContain("로이드", item.Name); Assert.DoesNotContain("앱솔랩스", item.Name); Assert.DoesNotContain("아케인셰이드", item.Name); Assert.DoesNotContain(item.Name.Replace(" ", ""), new[] { "태초의정수", "루인포스실드", "저주받은카이세리움" }); Assert.Matches(@"^BossLoot/\d+\.png$", item.Icon); });
        Assert.Equal(BossLootCatalog.ForBoss("검은 마법사"), BossLootCatalog.ForBoss("검은마법사"));
    }
}
