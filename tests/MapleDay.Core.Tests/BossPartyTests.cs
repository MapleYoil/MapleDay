using MapleDay.Core;
using System.Text.Json;

namespace MapleDay.Core.Tests;

public sealed class BossPartyTests
{
    [Theory]
    [InlineData("최초의 대적자")]
    [InlineData("찬란한 흉성")]
    [InlineData("림보")]
    [InlineData("벨로나")]
    [InlineData("유피테르")]
    [InlineData("발드릭스")]
    public void LimitedBossesClampOldAndNewSettingsToThree(string name)
    {
        Assert.Equal(3, BossParty.Maximum(name));
        Assert.Equal(3, BossParty.Clamp(name, 6));
        Assert.Equal(1, BossParty.Clamp(name, 0));
    }
    [Fact] public void OtherBossesStillAllowSix() => Assert.Equal(6, BossParty.Clamp("스우", 6));
    [Fact]
    public void BossButtonAndIncomeUseTheSamePeriodIdentity()
    {
        Assert.Equal("Weekly:2026-10-01:Bosses/icon_35.png", BossParty.RecordId("최초의 대적자", BossCycle.Weekly, new(2026, 10, 6)));
        Assert.Equal("Monthly:2026-10-01:Bosses/icon_25.png", BossParty.RecordId("검은 마법사", BossCycle.Monthly, new(2026, 10, 6)));
    }

    [Fact]
    public void BulkPartyIdsStartAtFirstSavedDayAndKeepOtherBossesAndCyclesSeparate()
    {
        SchedulerSnapshot Day(DateOnly date, params SchedulerBoss[] bosses) => new(date, new() { Bosses = bosses.ToList() }, true, DateTimeOffset.UtcNow);
        SchedulerBoss Boss(string name, string cycle = "weekly", string difficulty = "normal") => new() { Name = name, Cycle = cycle, Difficulty = difficulty, Complete = JsonSerializer.SerializeToElement(true) };
        var first = SchedulerBossHistory.FirstDate;
        var today = new DateOnly(2026, 10, 8);
        var snapshots = new[] {
            Day(first, Boss("스우", difficulty: "hard"), Boss("데미안")),
            Day(first.AddDays(1), Boss("스우")),
            Day(today.AddDays(-7), Boss("스우")),
            Day(today, Boss("스우")),
            Day(today.AddDays(7), Boss("스우")),
            Day(first.AddDays(-7), Boss("스우")) };
        var ids = BossParty.SavedRecordIds(snapshots, "스우", BossCycle.Weekly, today);
        Assert.Equal(3, ids.Count);
        Assert.Contains(BossParty.RecordId("스우", BossCycle.Weekly, first), ids);
        Assert.DoesNotContain(BossParty.RecordId("데미안", BossCycle.Weekly, first), ids);
        Assert.Empty(BossParty.SavedRecordIds(snapshots, "스우", BossCycle.Monthly, today));
        Assert.Empty(BossParty.SavedRecordIds(snapshots, "스우", BossCycle.Daily, today));
        var overrides = ids.ToDictionary(id => id, _ => 2);
        var result = BossIncome.Calculate(snapshots, today, id => overrides.GetValueOrDefault(id, 1));
        Assert.All(result.Records.Where(row => row.Name == "스우"), row => Assert.Equal(2, row.PartySize));
        Assert.Equal(1, result.Records.Single(row => row.Name == "데미안").PartySize);
        Assert.Equal(CrystalPrices.Find("스우", "hard", first)!.Meso / 2 + CrystalPrices.Find("데미안", "normal", first)!.Meso, result.Sum(first, first));
    }
}
