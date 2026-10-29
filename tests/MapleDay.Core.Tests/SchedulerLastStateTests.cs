using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerLastStateTests
{
    private static SchedulerState Saved() => new()
    {
        Name = "캐릭터", World = "오로라", WeeklyBossClearCount = 3, WeeklyBossClearLimit = 12,
        Daily = [new() { Name = "[일일 퀘스트] 세르니움 조사", Type = "quest", Now = 100, Maximum = 100,
            QuestState = "2", Registration = JsonSerializer.SerializeToElement(true) }],
        Weekly = [new() { Name = "에픽 던전 : 악몽선경", Now = 5, Maximum = 5, Registration = JsonSerializer.SerializeToElement(true) }],
        Bosses = new[] { "bossDaily", "bossWeekly", "bossMonthly" }.Select((cycle, index) => new SchedulerBoss
            { Name = $"보스{index}", Cycle = cycle, Difficulty = "hard", Order = index,
                Registration = JsonSerializer.SerializeToElement(true), Complete = JsonSerializer.SerializeToElement(true) }).ToList()
    };

    [Theory]
    [InlineData("2026-10-07", "2026-10-08", true, false)]
    [InlineData("2026-10-08", "2026-10-09", false, false)]
    [InlineData("2026-09-30", "2026-10-01", true, true)]
    [InlineData("2026-12-31", "2027-01-01", false, true)]
    public void ResetsOnlyExpiredPeriodsAndPreservesRosterWithoutChangingSavedEvidence(string before, string after, bool weeklyReset, bool monthlyReset)
    {
        var saved = Saved();
        var projected = SchedulerLastState.Project(new(DateOnly.Parse(before), saved, true, DateTimeOffset.UtcNow), DateOnly.Parse(after));
        Assert.Equal(0, projected.Daily![0].Now);
        Assert.Equal("0", projected.Daily[0].QuestState);
        Assert.Equal(weeklyReset ? 0 : 5, projected.Weekly![0].Now);
        Assert.False(SchedulerEntries.Flag(projected.Bosses![0].Complete));
        Assert.Equal(!weeklyReset, SchedulerEntries.Flag(projected.Bosses[1].Complete));
        Assert.Equal(!monthlyReset, SchedulerEntries.Flag(projected.Bosses[2].Complete));
        Assert.Equal(weeklyReset ? 0 : 3, projected.WeeklyBossClearCount);
        Assert.Equal(12, projected.WeeklyBossClearLimit);
        Assert.All(projected.Bosses, boss => { Assert.True(SchedulerEntries.Flag(boss.Registration)); Assert.Equal("hard", boss.Difficulty); });
        Assert.Equal("2", saved.Daily![0].QuestState);
        Assert.Equal(5, saved.Weekly![0].Now);
        Assert.All(saved.Bosses!, boss => Assert.True(SchedulerEntries.Flag(boss.Complete)));
    }

    [Fact]
    public void SameDayKeepsCompletionAndFutureOrEmptySnapshotsAreNotUsed()
    {
        var today = new DateOnly(2026, 10, 8);
        var snapshot = new SchedulerSnapshot(today, Saved(), false, DateTimeOffset.UtcNow);
        Assert.Equal("2", SchedulerLastState.Project(snapshot, today).Daily![0].QuestState);
        Assert.Same(snapshot, SchedulerLastState.Latest([snapshot, new(today.AddDays(1), Saved(), true, DateTimeOffset.UtcNow),
            new(today, null, false, DateTimeOffset.UtcNow.AddHours(1))], today));
    }

    [Fact]
    public void SparseLiveRosterIsFilledFromMostCompleteSavedRosterWithoutOverwritingLiveState()
    {
        var today = new DateOnly(2026, 10, 29);
        var sourceDate = today.AddDays(-1);
        var source = Saved();
        source.Daily!.AddRange(Enumerable.Range(1, 4).Select(index => new SchedulerContent
            { Name = $"일일 {index}", Type = "quest", Now = 1, Maximum = 1 }));
        source.Bosses!.AddRange(Enumerable.Range(3, 4).Select(index => new SchedulerBoss
            { Name = $"보스{index}", Cycle = "bossWeekly", Difficulty = "hard",
                Complete = JsonSerializer.SerializeToElement(true) }));
        var current = new SchedulerState
        {
            Date = today.ToString("yyyy-MM-dd"), Name = "캐릭터", World = "오로라",
            Daily = [new() { Name = source.Daily[0].Name, Type = source.Daily[0].Type, QuestState = "2" }],
            Bosses = [new() { Name = "보스1", Cycle = "bossWeekly", Difficulty = "hard",
                Complete = JsonSerializer.SerializeToElement(true) }]
        };

        var filled = SchedulerLastState.FillMissingRoster(current,
            new(sourceDate, source, true, DateTimeOffset.UtcNow), today);

        Assert.NotNull(filled);
        Assert.Equal(source.Daily.Count, filled.Daily!.Count);
        Assert.Equal(source.Bosses.Count, filled.Bosses!.Count);
        Assert.Equal("2", filled.Daily.Single(item => item.Name == source.Daily[0].Name).QuestState);
        Assert.True(SchedulerEntries.Flag(filled.Bosses.Single(boss => boss.Name == "보스1").Complete));
        Assert.All(filled.Daily.Where(item => item.Name != source.Daily[0].Name), item => Assert.Equal(0, item.Now));
        Assert.False(SchedulerEntries.Flag(filled.Bosses.Single(boss => boss.Name == "보스0").Complete));
        Assert.True(SchedulerEntries.Flag(filled.Bosses.Single(boss => boss.Name == "보스2").Complete));
        Assert.All(filled.Bosses.Where(boss => boss.Name is not ("보스0" or "보스1" or "보스2")),
            boss => Assert.False(SchedulerEntries.Flag(boss.Complete)));
        Assert.True(SchedulerEntries.Flag(source.Bosses.Single(boss => boss.Name == "보스1").Complete));
    }

    [Fact]
    public void BestRosterUsesLargestPastObservationAndSmallEmptyLiveRosterIsNotTreatedAsPartial()
    {
        var today = new DateOnly(2026, 10, 29);
        var small = new SchedulerSnapshot(today.AddDays(-1), new SchedulerState
            { Name = "캐릭터", Daily = [new() { Name = "하나" }] }, true, DateTimeOffset.UtcNow);
        var largeState = Saved();
        var large = new SchedulerSnapshot(today.AddDays(-2), largeState, true, DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Same(large, SchedulerLastState.BestRoster([small, large], today));
        Assert.Null(SchedulerLastState.FillMissingRoster(new SchedulerState
            { Name = "캐릭터", Daily = [], Weekly = [], Bosses = [] }, small, today));
    }
}
