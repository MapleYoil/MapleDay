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
}
