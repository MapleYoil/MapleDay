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
    public void LatestPartySizeCarriesIntoTheNewWeekWithoutChangingOlderHistory()
    {
        var today = new DateOnly(2026, 10, 30);
        var previous = BossParty.RecordId("스우", BossCycle.Weekly, today.AddDays(-7));
        var current = BossParty.RecordId("스우", BossCycle.Weekly, today);
        var olderMissing = BossParty.RecordId("스우", BossCycle.Weekly, today.AddDays(-14));
        var sizes = new Dictionary<string, int> { ["character|" + previous] = 4 };

        Assert.Equal(4, BossParty.Resolve(sizes, "character", previous, today));
        Assert.Equal(4, BossParty.Resolve(sizes, "character", current, today));
        Assert.Equal(1, BossParty.Resolve(sizes, "character", olderMissing, today));
        Assert.Equal(1, BossParty.Resolve(sizes, "other-character", current, today));
    }

    [Fact]
    public void ExplicitPeriodValuesTakePrecedenceOverAnUndatedDefault()
    {
        var today = new DateOnly(2026, 10, 30);
        var current = BossParty.RecordId("스우", BossCycle.Weekly, today);
        var sizes = new Dictionary<string, int>
        {
            ["character|" + BossParty.PreferenceId(current)] = 5,
            ["character|" + current] = 2
        };

        Assert.Equal("Default:Weekly:Bosses/icon_13.png", BossParty.PreferenceId(current));
        Assert.Equal(2, BossParty.Resolve(sizes, "character", current, today));
        var previous = BossParty.RecordId("스우", BossCycle.Weekly, today.AddDays(-7));
        sizes["character|" + previous] = 2;
        Assert.Equal(2, BossParty.Resolve(sizes, "character", previous, today));
        sizes.Remove("character|" + current);
        Assert.Equal(5, BossParty.Resolve(sizes, "character", current, today));
    }

    [Fact]
    public void SettingCurrentPartySizeWritesBothHistoryAndPersistentPreference()
    {
        var today = new DateOnly(2026, 11, 1);
        var current = BossParty.RecordId("스우", BossCycle.Weekly, today);
        var previous = BossParty.RecordId("스우", BossCycle.Weekly, today.AddDays(-7));
        var sizes = new Dictionary<string, int>();

        BossParty.SetCurrentPreference(sizes, "character", current, 4, today);
        BossParty.SetPeriodSize(sizes, "character", previous, 2);

        Assert.Equal(4, sizes["character|" + current]);
        Assert.Equal(4, sizes["character|" + BossParty.PreferenceId(current)]);
        Assert.Equal(2, sizes["character|" + previous]);
        Assert.Equal(4, BossParty.Resolve(sizes, "character", current, today.AddDays(7)));
        Assert.Equal(4, BossParty.Resolve(sizes, "character",
            BossParty.RecordId("스우", BossCycle.Weekly, today.AddDays(7)), today.AddDays(7)));
    }

    [Theory]
    [InlineData("스우", BossCycle.Weekly, "weekly")]
    [InlineData("검은 마법사", BossCycle.Monthly, "monthly")]
    public void InheritedPartySizeDoesNotBecomeSoloAfterTheNextReset(string boss, BossCycle cycle, string apiCycle)
    {
        const string difficulty = "hard";
        var before = new DateOnly(2026, 10, 1);
        var clearDate = SchedulerBossHistory.End(cycle, before).AddDays(1);
        var nextPeriod = SchedulerBossHistory.End(cycle, clearDate).AddDays(1);
        var oldId = BossParty.RecordId(boss, cycle, before);
        var clearId = BossParty.RecordId(boss, cycle, clearDate);
        var sizes = new Dictionary<string, int> { ["character|" + oldId] = 2, ["character|" + BossParty.PreferenceId(oldId)] = 2 };
        var snapshot = new SchedulerSnapshot(clearDate, new()
        {
            Bosses = [new() { Name = boss, Difficulty = difficulty, Cycle = apiCycle, Complete = JsonSerializer.SerializeToElement(true) }]
        }, true, DateTimeOffset.UtcNow);

        Assert.Equal(2, BossParty.Resolve(sizes, "character", clearId, clearDate));
        var result = BossIncome.Calculate([snapshot], nextPeriod, id => BossParty.Resolve(sizes, "character", id, nextPeriod));
        var record = Assert.Single(result.Records);
        Assert.Equal(2, record.PartySize);
        Assert.Equal(CrystalPrices.Find(boss, difficulty, clearDate)!.Meso / 2, record.Meso);
    }

    [Fact]
    public void LaterDefaultsAndOneOffEditsDoNotChangeInheritedPastSizes()
    {
        var first = new DateOnly(2026, 11, 5);
        var intervening = first.AddDays(7);
        var today = first.AddDays(21);
        string Id(DateOnly date) => BossParty.RecordId("스우", BossCycle.Weekly, date);
        var sizes = new Dictionary<string, int>();
        BossParty.SetCurrentPreference(sizes, "character", Id(first), 2, first);
        BossParty.SetPeriodSize(sizes, "character", Id(intervening), 6);
        // The scheduler can still display a tile from the prior week at reset.
        BossParty.SetCurrentPreference(sizes, "character", Id(today.AddDays(-7)), 3, today);

        Assert.Equal(6, BossParty.Resolve(sizes, "character", Id(intervening), today));
        Assert.Equal(2, BossParty.Resolve(sizes, "character", Id(intervening.AddDays(7)), today));
        Assert.Equal(3, BossParty.Resolve(sizes, "character", Id(today), today));
        Assert.Equal(1, BossParty.Resolve(sizes, "character", Id(first.AddDays(-7)), today));
        BossParty.SetPeriodSize(sizes, "character", Id(today), 4);
        Assert.Equal(4, BossParty.Resolve(sizes, "character", Id(today), today));
        Assert.Equal(3, BossParty.Resolve(sizes, "character", Id(today.AddDays(7)), today.AddDays(7)));
    }

    [Fact]
    public void EditingAnOldPeriodKeepsItsLegacySizeForMissingWeeks()
    {
        var first = new DateOnly(2026, 11, 5);
        var today = first.AddDays(28);
        string Id(int week) => BossParty.RecordId("스우", BossCycle.Weekly, first.AddDays(7 * week));
        var sizes = new Dictionary<string, int> { ["character|" + Id(0)] = 2, ["character|" + Id(2)] = 3 };
        BossParty.SetPeriodSize(sizes, "character", Id(0), 6);
        Assert.Equal(6, BossParty.Resolve(sizes, "character", Id(0), today));
        Assert.Equal(2, BossParty.Resolve(sizes, "character", Id(1), today));
        Assert.Equal(3, BossParty.Resolve(sizes, "character", Id(3), today));
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
