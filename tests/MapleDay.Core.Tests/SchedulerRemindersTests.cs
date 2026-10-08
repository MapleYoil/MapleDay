using System.Text.Json;

namespace MapleDay.Core.Tests;

public sealed class SchedulerRemindersTests
{
    [Theory]
    [InlineData(ReminderKind.DailyQuest, "2026-10-06T21:59:59+09:00", "2026-10-06T22:00:00+09:00", "2026-10-07T00:00:00+09:00")]
    [InlineData(ReminderKind.WeeklyQuest, "2026-10-07T21:59:59+09:00", "2026-10-07T22:00:00+09:00", "2026-10-08T00:00:00+09:00")]
    [InlineData(ReminderKind.WeeklyBoss, "2026-10-07T21:59:59+09:00", "2026-10-07T22:00:00+09:00", "2026-10-08T00:00:00+09:00")]
    public void DefaultWindowsUseKoreaMidnightAndThursday(ReminderKind kind, string before, string due, string reset)
    {
        var window = SchedulerReminders.Window(kind, DateTimeOffset.Parse(before).ToUniversalTime(), 2);
        Assert.Equal(DateTimeOffset.Parse(due), window.Due);
        Assert.Equal(DateTimeOffset.Parse(reset), window.Reset);
        Assert.False(window.IsDue(DateTimeOffset.Parse(before)));
        Assert.True(window.IsDue(window.Due));
        Assert.True(window.IsDue(window.Reset.AddSeconds(-1)));
        Assert.False(window.IsDue(window.Reset));
        Assert.NotEqual(window.PeriodKey, SchedulerReminders.Window(kind, window.Reset, 2).PeriodKey);
    }

    [Fact]
    public void LeadTimeCanCrossDayBoundaryAndNextScheduleDoesNotShowPastTime()
    {
        var now = DateTimeOffset.Parse("2026-10-06T12:00:00+09:00");
        var options = new ReminderOptions { RestrictToDay = false, HoursBefore = 30 };
        var weekly = SchedulerReminders.Window(ReminderKind.WeeklyBoss, now, options);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T18:00:00+09:00"), weekly.Due);
        Assert.Equal(weekly.Due.AddDays(7), SchedulerReminders.NextDue(ReminderKind.WeeklyBoss, weekly.Due.AddSeconds(1), options));
        Assert.Equal(23, (SchedulerReminders.Window(ReminderKind.DailyQuest, now, 1000).Reset - SchedulerReminders.Window(ReminderKind.DailyQuest, now, 1000).Due).TotalHours);
    }

    [Theory]
    [InlineData("2026-10-08T10:40:00+09:00", false)]
    [InlineData("2026-10-09T22:00:00+09:00", false)]
    [InlineData("2026-10-10T22:00:00+09:00", false)]
    [InlineData("2026-10-11T22:00:00+09:00", false)]
    [InlineData("2026-10-12T22:00:00+09:00", false)]
    [InlineData("2026-10-13T22:00:00+09:00", false)]
    [InlineData("2026-10-14T09:00:00+09:00", true)]
    [InlineData("2026-10-13T14:59:59Z", false)]
    [InlineData("2026-10-13T15:00:00Z", true)]
    [InlineData("2026-10-14T14:59:59Z", true)]
    [InlineData("2026-10-14T15:00:00Z", false)]
    public void DefaultWeeklyBossStartupOnlyChecksOnKoreanWednesday(string timestamp, bool allowed)
    {
        var now = DateTimeOffset.Parse(timestamp);
        var window = SchedulerReminders.Window(ReminderKind.WeeklyBoss, now, new ReminderOptions());
        Assert.Equal(allowed, window.AllowsDay(now));
        Assert.Equal(allowed, SchedulerReminders.ShouldCheck(window, now, false, true));
        Assert.Equal(allowed, SchedulerReminders.ShouldCheck(window, now, true, true));
    }

    [Fact]
    public void ScheduledWednesdayWindowStopsAtKoreanThursdayMidnight()
    {
        var before = DateTimeOffset.Parse("2026-10-14T21:59:59+09:00");
        var window = SchedulerReminders.Window(ReminderKind.WeeklyBoss, before, 2);
        Assert.False(SchedulerReminders.ShouldCheck(window, before, false, false));
        Assert.True(SchedulerReminders.ShouldCheck(window, window.Due, false, false));
        Assert.False(SchedulerReminders.ShouldCheck(window, window.Due, true, false));
        Assert.False(window.IsDue(window.Reset));
        Assert.False(SchedulerReminders.ShouldCheck(window, window.Reset, false, true));
        Assert.False(SchedulerReminders.ShouldCheck(SchedulerReminders.Window(ReminderKind.WeeklyBoss, window.Reset, 2), window.Reset, false, true));
        Assert.Equal(window.Due.AddDays(7), SchedulerReminders.NextDue(ReminderKind.WeeklyBoss, window.Reset, 2));
    }

    [Theory]
    [InlineData(DayOfWeek.Thursday, 8)]
    [InlineData(DayOfWeek.Friday, 9)]
    [InlineData(DayOfWeek.Saturday, 10)]
    [InlineData(DayOfWeek.Sunday, 11)]
    [InlineData(DayOfWeek.Monday, 12)]
    [InlineData(DayOfWeek.Tuesday, 13)]
    [InlineData(DayOfWeek.Wednesday, 14)]
    public void ChosenWeekdaySchedulesAtTenPmWithinItsThursdayResetPeriod(DayOfWeek day, int date)
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00+09:00");
        var options = new ReminderOptions { Day = day };
        var window = SchedulerReminders.Window(ReminderKind.WeeklyBoss, now, options);
        Assert.Equal(new DateTimeOffset(2026, 10, date, 22, 0, 0, SchedulerReminders.Korea), window.Due);
        Assert.Equal(DateTimeOffset.Parse("2026-10-15T00:00:00+09:00"), window.Reset);
        Assert.Equal(window.Due, SchedulerReminders.NextDue(ReminderKind.WeeklyBoss, now, options));
        Assert.True(window.IsDue(window.Due));
        Assert.False(window.IsDue(window.Due.AddDays(1)));
        Assert.False(SchedulerReminders.ShouldCheck(window, window.Due.AddDays(1), false, true));
        Assert.Equal(window.Due.AddDays(7), SchedulerReminders.NextDue(ReminderKind.WeeklyBoss, window.Due.AddMinutes(1), options));
    }

    [Fact]
    public void DayRestrictionClampsLongLeadTimeAndUnrestrictedModeAllowsThursdayStartup()
    {
        var now = DateTimeOffset.Parse("2026-10-08T10:40:00+09:00");
        var options = new ReminderOptions { HoursBefore = 167 };
        var window = SchedulerReminders.Window(ReminderKind.WeeklyBoss, now, options);
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T00:00:00+09:00"), window.Due);
        Assert.False(SchedulerReminders.ShouldCheck(window, now, false, true));
        options.RestrictToDay = false;
        window = SchedulerReminders.Window(ReminderKind.WeeklyBoss, now, options);
        Assert.Null(window.AllowedDay);
        Assert.True(SchedulerReminders.ShouldCheck(window, now, false, true));
        Assert.True(window.IsDue(now));
        Assert.True(SchedulerReminders.ShouldCheck(window, now, false, false));
        Assert.False(SchedulerReminders.ShouldCheck(window, now, true, false));
        Assert.True(SchedulerReminders.ShouldCheck(window, now, true, true));
    }

    [Theory]
    [InlineData(ReminderKind.DailyQuest)]
    [InlineData(ReminderKind.WeeklyQuest)]
    public void BossDayPreferenceDoesNotRestrictQuestStartup(ReminderKind kind)
    {
        var now = DateTimeOffset.Parse("2026-10-08T10:40:00+09:00");
        var window = SchedulerReminders.Window(kind, now, new ReminderOptions());
        Assert.Null(window.AllowedDay);
        Assert.True(SchedulerReminders.ShouldCheck(window, now, false, true));
    }

    [Fact]
    public void InvalidStoredWeekdayFallsBackToWednesday()
    {
        var now = DateTimeOffset.Parse("2026-10-08T10:40:00+09:00");
        var window = SchedulerReminders.Window(ReminderKind.WeeklyBoss, now, new ReminderOptions { Day = (DayOfWeek)123 });
        Assert.Equal(DayOfWeek.Wednesday, window.AllowedDay);
        Assert.False(SchedulerReminders.ShouldCheck(window, now, false, true));
    }

    [Fact]
    public void OnlyRegisteredUnfinishedEligibleQuestsAreIncluded()
    {
        SchedulerContent Quest(string name, string state, bool registered = true) => new()
        { Name = name, Type = "quest", Registration = Flag(registered), Now = 100, Maximum = 100, QuestState = state };
        var state = new SchedulerState
        {
            Daily = [Quest("[일일 퀘스트] 카르시온 복구 지원", "1"), Quest("완료 퀘스트", "2"), Quest("미등록", "1", false),
                Quest("[일일 퀘스트] 기어드락 크로노스", "1"), new() { Name = "몬스터파크", Type = "contents", Registration = Flag(true) }],
            Weekly = [Quest("[주간 퀘스트] 헤이븐", "0"), Quest("주간 완료", "2")]
        };
        Assert.Equal(["[일일 퀘스트] 카르시온 복구 지원"], SchedulerReminders.Pending(ReminderKind.DailyQuest, state, 291));
        Assert.Equal(["[주간 퀘스트] 헤이븐"], SchedulerReminders.Pending(ReminderKind.WeeklyQuest, state, 291));
    }

    [Theory]
    [InlineData(11, 1)]
    [InlineData(12, 0)]
    public void WeeklyBossesExcludeCompletedUnregisteredMonthlyDailyAndBlocked(long cleared, int pending)
    {
        SchedulerBoss Boss(string name, string cycle = "bossWeekly", bool registered = true, bool complete = false) => new()
        { Name = name, Cycle = cycle, Difficulty = "hard", Registration = Flag(registered), Complete = Flag(complete) };
        var state = new SchedulerState { WeeklyBossClearCount = cleared,
            Bosses = [Boss("스우"), Boss("유피테르"), Boss("루시드", complete: true), Boss("윌", registered: false), Boss("매그너스", "bossDaily"), Boss("검은 마법사", "bossMonthly")] };
        var result = SchedulerReminders.Pending(ReminderKind.WeeklyBoss, state, 291);
        Assert.Equal(pending, result.Count);
        if (pending > 0) Assert.Equal("스우 (hard)", result[0]);
    }

    [Theory]
    [InlineData(ReminderKind.DailyQuest)]
    [InlineData(ReminderKind.WeeklyQuest)]
    public void UnionQuestsAreOptInAndDoNotDisableOtherQuestsOrSchedulerRows(ReminderKind kind)
    {
        SchedulerContent Quest(string name, string questState = "1", bool registered = true) => new()
        { Name = name, Type = "quest", Registration = Flag(registered), QuestState = questState };
        const string union = "[메이플 유니온] 거대 드래곤 처치";
        const string pcUnion = "[메이플 유니온] PC방 거대 드래곤 처치";
        const string ordinary = "[일일 퀘스트] 세르니움 조사";
        var quests = new List<SchedulerContent> { Quest(union), Quest(pcUnion), Quest(ordinary),
            Quest("[메이플 유니온] 완료", "2"), Quest("[메이플 유니온] 미등록", registered: false) };
        var state = kind == ReminderKind.DailyQuest ? new SchedulerState { Daily = quests } : new SchedulerState { Weekly = quests };
        Assert.Equal([ordinary], SchedulerReminders.Pending(kind, state, 291));
        Assert.Equal([ordinary], SchedulerReminders.Pending(kind, state, 291, includeUnionQuests: false));
        Assert.Equal([union, pcUnion, ordinary], SchedulerReminders.Pending(kind, state, 291, includeUnionQuests: true));
        Assert.Contains(SchedulerEntries.Create(state), entry => entry.Name == union);
        Assert.Contains(SchedulerEntries.Create(state), entry => entry.Name == pcUnion);
    }

    private static JsonElement Flag(bool value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void CompletedExtremeQuestHistorySurvivesCurrentStateZeroUntilThursdayReset()
    {
        const string name = "[몬스터파크] 익스트림 몬스터파커에 도전해보겠나?";
        SchedulerState State(string questState) => new() { World = "오로라", Weekly = [new()
            { Name = name, Type = "quest", Registration = Flag(true), QuestState = questState }, new()
            { Name = "[주간 퀘스트] 헤이븐", Type = "quest", Registration = Flag(true), QuestState = "0" }] };
        var history = new[] { new SchedulerSnapshot(new(2026, 10, 2), State("2"), true, DateTimeOffset.UtcNow),
            new SchedulerSnapshot(new(2026, 10, 7), State("0"), false, DateTimeOffset.UtcNow) };
        Assert.True(SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, new(2026, 10, 7), "오로라"));
        Assert.False(SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, new(2026, 10, 8), "오로라"));
        Assert.False(SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, new(2026, 10, 7), "루나"));
        Assert.Equal(["[주간 퀘스트] 헤이븐"], SchedulerReminders.Pending(ReminderKind.WeeklyQuest, State("0"), 291, extremeMonsterParkCompleted: true));
        Assert.Equal([name, "[주간 퀘스트] 헤이븐"], SchedulerReminders.Pending(ReminderKind.WeeklyQuest, State("0"), 291));
        var observed = history[^1] with { ExtremeMonsterParkCompleted = true };
        Assert.True(SchedulerReminders.ExtremeMonsterParkCompletedThisWeek([observed], new(2026, 10, 7), "오로라"));
    }

    [Fact]
    public void WorldCapCountsDistinctCharactersRatherThanCompletionDates()
    {
        SchedulerSnapshot Snapshot(string world, int day) => new(new(2026, 10, day), new() { World = world }, true, DateTimeOffset.UtcNow)
            { ExtremeMonsterParkCompleted = true };
        IReadOnlyList<SchedulerSnapshot>[] histories = [[Snapshot("오로라", 2), Snapshot("오로라", 4)], [Snapshot("루나", 2)]];
        var today = new DateOnly(2026, 10, 7);
        Assert.Equal(1, histories.Count(history => SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, today, "오로라")));
        histories = [.. histories, new[] { Snapshot("오로라", 5) }];
        Assert.Equal(2, histories.Count(history => SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, today, "오로라")));
        Assert.Equal(0, histories.Count(history => SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(history, new(2026, 10, 8), "오로라")));
    }

    [Fact]
    public void StartupChecksIgnoreDueTimeButKeepLaterScheduledChecksAndOneCheckPerLaunch()
    {
        var morning = DateTimeOffset.Parse("2026-10-06T09:00:00+09:00");
        var window = SchedulerReminders.Window(ReminderKind.DailyQuest, morning, 2);
        Assert.False(SchedulerReminders.ShouldCheck(window, morning, false, false));
        Assert.True(SchedulerReminders.ShouldCheck(window, morning, false, true));
        // Once startup succeeds, timers do not send it again before the due time.
        Assert.False(SchedulerReminders.ShouldCheck(window, morning.AddMinutes(1), false, false));
        // The ordinary 22:00 reminder remains available after a morning startup check.
        Assert.True(SchedulerReminders.ShouldCheck(window, window.Due, false, false));
        Assert.False(SchedulerReminders.ShouldCheck(window, window.Due, true, false));
        // Restarting the app allows a startup check even if the scheduled period was checked.
        Assert.True(SchedulerReminders.ShouldCheck(window, window.Due, true, true));
    }

    [Fact]
    public void MissingSectionsAreUnknownWhileEmptySectionsAreConfirmedForTheirOwnReminderKinds()
    {
        var state = new SchedulerState { Daily = [], Bosses = [] };
        Assert.True(SchedulerReminders.HasData(ReminderKind.DailyQuest, state));
        Assert.False(SchedulerReminders.HasData(ReminderKind.WeeklyQuest, state));
        Assert.True(SchedulerReminders.HasData(ReminderKind.WeeklyBoss, state));
        Assert.Empty(SchedulerReminders.Pending(ReminderKind.DailyQuest, state, 291));
        Assert.Empty(SchedulerReminders.Pending(ReminderKind.WeeklyBoss, state, 291));
    }
}
