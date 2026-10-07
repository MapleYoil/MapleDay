using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerBossHistoryTests
{
    [Theory]
    [InlineData("2026-10-07T14:59:59Z", "2026-10-07", "2026-10-01")]
    [InlineData("2026-10-07T15:00:00Z", "2026-10-08", "2026-10-08")]
    [InlineData("2026-09-30T14:59:59Z", "2026-09-30", "2026-09-24")]
    [InlineData("2026-09-30T15:00:00Z", "2026-10-01", "2026-10-01")]
    public void DatesAndThursdayResetUseKoreanTime(string utc, string expectedDate, string expectedWeek)
    {
        var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.Parse(utc));
        Assert.Equal(DateOnly.Parse(expectedDate), today);
        Assert.Equal(DateOnly.Parse(expectedWeek), SchedulerBossHistory.Start(BossCycle.Weekly, today));
        Assert.Equal(new DateOnly(today.Year, today.Month, 1), SchedulerBossHistory.Start(BossCycle.Monthly, today));
    }

    [Fact]
    public void WeeklyCountsDeduplicateAndIncludeUnregisteredClearsWithTwelveMaximum()
    {
        var today = new DateOnly(2026, 10, 6);
        var roster = State(Boss("스우", "weekly", false), Boss("스우", "weekly", false, difficulty: "hard"),
            Boss("데미안", "weekly", false), Boss("카링", "weekly", true, registered: false));
        var snapshots = Enumerable.Range(0, 6).Select(offset => Snapshot(new DateOnly(2026, 10, 1).AddDays(offset),
            State(Boss("스우", "weekly", true), Boss("카링", "weekly", true, registered: false)))).ToArray();
        var count = SchedulerBossHistory.Count(BossCycle.Weekly, today, snapshots, roster, today);
        Assert.Equal(2, count.Cleared);
        Assert.Equal(12, count.Maximum);
        Assert.True(count.CompleteRange);
    }

    [Fact]
    public void ClearsBeforeWeeklyAndMonthlyResetsDoNotLeakIntoTheNewPeriod()
    {
        var today = new DateOnly(2026, 10, 1);
        var roster = State(Boss("스우", "bossWeekly", false), Boss("검은 마법사", "bossMonthly", false));
        var snapshots = new[] { Snapshot(today.AddDays(-1), State(Boss("스우", "bossWeekly", true), Boss("검은 마법사", "bossMonthly", true))), Snapshot(today, roster, final: false) };
        foreach (var cycle in new[] { BossCycle.Weekly, BossCycle.Monthly })
        {
            var count = SchedulerBossHistory.Count(cycle, today, snapshots, roster, today);
            Assert.Equal(0, count.Cleared);
            Assert.Equal(cycle == BossCycle.Weekly ? 12 : 1, count.Maximum);
            Assert.True(count.CompleteRange);
        }
    }

    [Fact]
    public void DailyCountUsesOnlyToday()
    {
        var today = new DateOnly(2026, 10, 6);
        var roster = State(Boss("자쿰", "bossDaily", false));
        var count = SchedulerBossHistory.Count(BossCycle.Daily, today,
            [Snapshot(today.AddDays(-1), State(Boss("자쿰", "daily", true))), Snapshot(today, roster, final: false)], roster, today);
        Assert.Equal(0, count.Cleared);
        Assert.Equal(1, count.Maximum);
        Assert.True(count.CompleteRange);
    }

    [Fact]
    public void NoLoginDateIsKnownButMissingOrProvisionalPastDateMakesRangePartial()
    {
        var today = new DateOnly(2026, 10, 3);
        var roster = State(Boss("스우", "weekly", true));
        var days = new List<SchedulerSnapshot> { Snapshot(new(2026, 10, 1), null), Snapshot(new(2026, 10, 2), roster), Snapshot(today, roster, false) };
        Assert.True(SchedulerBossHistory.Count(BossCycle.Weekly, today, days, roster, today).CompleteRange);
        days[1] = days[1] with { Final = false };
        Assert.False(SchedulerBossHistory.Count(BossCycle.Weekly, today, days, roster, today).CompleteRange);
        days.RemoveAt(1);
        Assert.False(SchedulerBossHistory.Count(BossCycle.Weekly, today, days, roster, today).CompleteRange);
    }

    [Fact]
    public void CachedOldDatesCompleteMonthlyCountsBeyondTheApiWindow()
    {
        var today = new DateOnly(2026, 10, 30);
        var roster = State(Boss("검은 마법사", "monthly", false));
        var days = Enumerable.Range(1, 30).Select(day => Snapshot(new DateOnly(2026, 10, day),
            day == 2 ? State(Boss("검은 마법사", "monthly", true)) : roster)).ToArray();
        var count = SchedulerBossHistory.Count(BossCycle.Monthly, today, days, roster, today);
        Assert.Equal(1, count.Cleared);
        Assert.True(count.CompleteRange);
        Assert.False(SchedulerBossHistory.Count(BossCycle.Monthly, today, days.Skip(15), roster, today).CompleteRange);
    }

    [Fact]
    public void PreviousPeriodsUseTheRegistrationAtThatPeriodRatherThanTodaysRoster()
    {
        var today = new DateOnly(2026, 10, 8);
        var state = State(Boss("스우", "weekly", true), Boss("데미안", "weekly", true));
        var snapshots = Enumerable.Range(0, 7).Select(offset => Snapshot(new DateOnly(2026, 10, 1).AddDays(offset), state)).Append(Snapshot(today, State())).ToArray();
        var week = Assert.Single(SchedulerBossHistory.PreviousPeriods(snapshots, today), period => period.Cycle == BossCycle.Weekly);
        Assert.Equal(2, week.Cleared);
        Assert.Equal(12, week.Maximum);
        Assert.True(week.CompleteRange);
    }

    private static SchedulerBoss Boss(string name, string cycle, bool clear, bool registered = true, string difficulty = "normal")
        => new() { Name = name, Cycle = cycle, Difficulty = difficulty, Registration = JsonSerializer.SerializeToElement(registered), Complete = JsonSerializer.SerializeToElement(clear) };
    private static SchedulerState State(params SchedulerBoss[] bosses) => new() { Name = "캐릭터", Bosses = bosses.ToList() };
    private static SchedulerSnapshot Snapshot(DateOnly date, SchedulerState? state, bool final = true) => new(date, state, final, DateTimeOffset.UtcNow);
}
