using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class ExperienceHistoryTests
{
    private static readonly DateOnly Start = new(2026, 9, 29);
    private static ExperienceSnapshot Snapshot(DateOnly date, int level, long exp, decimal rate, bool final = true)
        => new(date, new(level, exp, rate), final, DateTimeOffset.Parse("2026-10-07T03:00:00Z"));

    [Fact]
    public void SevenCompletedDaysProduceTenTrillionDailyAverageAndOneDayUntilLevelUp()
    {
        var snapshots = Enumerable.Range(0, 8).Select(day => Snapshot(Start.AddDays(day), 291,
            20_000_000_000_000L + day * 10_000_000_000_000L, 20 + day * 10)).ToList();
        snapshots.Add(Snapshot(Start.AddDays(8), 291, 99_000_000_000_000L, 99, final: false));
        var current = new ExperienceValue(291, 90_000_000_000_000L, 90);
        var trend = ExperienceHistory.Trend(snapshots, current, Start.AddDays(8), Start.AddDays(7));
        Assert.Equal(7, trend.KnownDays);
        Assert.Equal(10_000_000_000_000m, trend.Average);
        Assert.Equal(10m, trend.AveragePercent);
        Assert.Equal(1m, trend.LevelUpDays);
        Assert.Equal("+10.0조", ExperienceHistory.Amount(trend.Average!.Value));
    }

    [Fact]
    public void TodayGainUsesLatestLiveReadingAndDoesNotFinalizeOrChangeSevenDayForecastSample()
    {
        var today = Start.AddDays(8);
        var snapshots = Enumerable.Range(0, 8).Select(day => Snapshot(Start.AddDays(day), 291, 100 + day * 10, 10 + day)).ToList();
        snapshots.Add(Snapshot(today, 291, 175, 17.5m, final: false));
        snapshots.Add(Snapshot(today, 291, 200, 20, final: false) with { FetchedAt = DateTimeOffset.Parse("2026-10-07T04:00:00Z") });
        var current = snapshots[^1].Value;
        var before = ExperienceHistory.Trend(snapshots, current, today, today.AddDays(-1));
        var gain = ExperienceHistory.TodayGain(snapshots, today);
        var after = ExperienceHistory.Trend(snapshots, current, today, today.AddDays(-1));
        Assert.Equal(30m, gain.Gained);
        Assert.Equal(0, gain.LevelsGained);
        Assert.Equal(10m, after.Average);
        Assert.Equal(before.LevelUpDays, after.LevelUpDays);
        Assert.DoesNotContain(after.Days, day => day.Date == today);
        Assert.All(snapshots.Where(snapshot => snapshot.Date == today), snapshot => Assert.False(snapshot.Final));
    }

    [Theory]
    [InlineData(291, 200, 20, 30, 0)]
    [InlineData(291, 170, 17, 0, 0)]
    [InlineData(291, 150, 15, -20, 0)]
    [InlineData(292, 100, 5, 930, 1)]
    public void TodayGainHandlesIncreaseIdleLossAndLevelUp(int level, long exp, int percent, int expected, int levels)
    {
        var gain = ExperienceHistory.TodayGain([
            Snapshot(Start, 291, 170, 17), Snapshot(Start.AddDays(1), level, exp, percent, final: false)
        ], Start.AddDays(1));
        Assert.Equal(expected, gain.Gained);
        Assert.Equal(levels, gain.LevelsGained);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("partial")]
    public void TodayGainDoesNotBridgeMissingEmptyOrUnfinalizedYesterday(string baseline)
    {
        var snapshots = new List<ExperienceSnapshot> { Snapshot(Start, 291, 100, 10), Snapshot(Start.AddDays(2), 291, 200, 20, final: false) };
        if (baseline == "empty") snapshots.Add(new(Start.AddDays(1), null, true, DateTimeOffset.UtcNow));
        if (baseline == "partial") snapshots.Add(Snapshot(Start.AddDays(1), 291, 170, 17, final: false));
        var gain = ExperienceHistory.TodayGain(snapshots, Start.AddDays(2));
        Assert.NotNull(gain.Value);
        Assert.Null(gain.Gained);
        Assert.Null(gain.LevelsGained);
    }

    [Fact]
    public void TodayGainNeedsTodaysLiveSnapshotAndDoesNotInventIntermediateLevels()
    {
        Assert.Null(ExperienceHistory.TodayGain([Snapshot(Start, 291, 100, 10)], Start.AddDays(1)).Value);
        var gain = ExperienceHistory.TodayGain([Snapshot(Start, 200, 500, 50), Snapshot(Start.AddDays(1), 205, 0, 0, final: false)], Start.AddDays(1));
        Assert.Null(gain.Gained);
        Assert.Equal(5, gain.LevelsGained);
    }

    [Fact]
    public void LevelUpIncludesRemainingOldLevelExperienceBeforeNewLevelExperience()
    {
        var snapshots = new List<ExperienceSnapshot> { Snapshot(Start, 290, 900, 90) };
        snapshots.AddRange(Enumerable.Range(1, 7).Select(day => Snapshot(Start.AddDays(day), 291, day * 100, day * 5)));
        var trend = ExperienceHistory.Trend(snapshots, snapshots[^1].Value, Start.AddDays(8), Start.AddDays(7));
        Assert.Equal(200, trend.Days[1].Gained);
        Assert.Equal(1, trend.Days[1].LevelsGained);
        Assert.Equal(800m / 7, trend.Average);
        Assert.Equal(trend.Average * 100m / 2000m, trend.AveragePercent);
        Assert.Equal(1300m / trend.Average, trend.LevelUpDays);
    }

    [Fact]
    public void MissingBaselineOrDayNeverBecomesZeroGainAndDoesNotProduceForecast()
    {
        var snapshots = Enumerable.Range(0, 8).Where(day => day != 3)
            .Select(day => Snapshot(Start.AddDays(day), 280, day * 10, day)).ToArray();
        var trend = ExperienceHistory.Trend(snapshots, snapshots[^1].Value, Start.AddDays(8), Start.AddDays(7));
        Assert.Null(trend.Days.Single(day => day.Date == Start.AddDays(4)).Gained);
        Assert.Equal(5, trend.KnownDays);
        Assert.Null(trend.Average);
        Assert.Null(trend.LevelUpDays);
    }

    [Fact]
    public void InactiveDaysAreZeroButExperienceLossRemainsNegative()
    {
        var snapshots = Enumerable.Range(0, 8).Select(day => Snapshot(Start.AddDays(day), 280, 500, 50)).ToArray();
        var idle = ExperienceHistory.Trend(snapshots, snapshots[^1].Value, Start.AddDays(8), Start.AddDays(7));
        Assert.Equal(0, idle.Average);
        Assert.Null(idle.LevelUpDays);
        snapshots[^1] = Snapshot(Start.AddDays(7), 280, 400, 40);
        var loss = ExperienceHistory.Trend(snapshots, snapshots[^1].Value, Start.AddDays(8), Start.AddDays(7));
        Assert.Equal(-100, loss.Days[^1].Gained);
        Assert.True(loss.Average < 0);
        Assert.Null(loss.LevelUpDays);
    }

    [Fact]
    public void MissingIntermediateLevelsAreNotInventedForBurningLevelJumps()
    {
        var days = ExperienceHistory.Days([Snapshot(Start, 200, 500, 50), Snapshot(Start.AddDays(1), 205, 0, 0)]);
        Assert.Equal(5, days[^1].LevelsGained);
        Assert.Null(days[^1].Gained);
    }

    [Fact]
    public void ZeroPercentUsesAnObservedRequirementForSameLevelAndMaximumLevelHasNoForecast()
    {
        var snapshots = new[] { Snapshot(Start, 280, 500, 50) };
        Assert.Equal(1000, ExperienceHistory.Required(new(280, 0, 0), Start.AddDays(1), snapshots));
        Assert.Null(ExperienceHistory.Required(new(281, 0, 0), Start.AddDays(1), snapshots));
        Assert.Null(ExperienceHistory.Required(new(300, 500, 50), Start.AddDays(1), snapshots));
    }

    [Theory]
    [InlineData("2026-10-06T16:59:59Z", "2026-10-05")]
    [InlineData("2026-10-06T17:00:00Z", "2026-10-06")]
    public void HistoricalAvailabilityUsesKoreanTwoAm(string now, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ExperienceHistory.LatestFinalDate(DateTimeOffset.Parse(now)));

    [Theory]
    [InlineData(null, "2023-12-21")]
    [InlineData("2006-01-01T00:00+09:00", "2023-12-21")]
    [InlineData("2026-01-02T10:00+09:00", "2026-01-02")]
    public void QueryStartsAtApiLaunchOrCharacterCreation(string? created, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ExperienceHistory.StartDate(created));

    [Fact]
    public void MissingExperienceIsNotStoredAsZero()
    {
        Assert.Null(ExperienceValue.From(null));
        Assert.Throws<InvalidDataException>(() => ExperienceValue.From(new() { Level = 280, ExpRate = "5.000" }));
    }

    [Theory]
    [InlineData("0.01", "약 0일 1시간")]
    [InlineData("1.99", "약 2일 0시간")]
    [InlineData("31.5", "약 1개월 0일 12시간")]
    [InlineData("36524", "약 100년 0개월 0일 0시간")]
    [InlineData("146097", "약 400년 0개월 0일 0시간")]
    [InlineData("4694827", "약 12853년 11개월 29일 0시간")]
    public void LevelUpDurationUsesCalendarUnitsWithoutHundredYearCap(string days, string expected) =>
        Assert.Equal(expected, ExperienceHistory.LevelUpDuration(decimal.Parse(days, System.Globalization.CultureInfo.InvariantCulture), new(2026, 10, 7)));

    [Fact]
    public void LeapYearDurationAndHugeYearCountsDoNotOverflow()
    {
        Assert.Equal("약 1년 0개월 1일 0시간", ExperienceHistory.LevelUpDuration(366, new(2024, 2, 29)));
        Assert.Equal("약 400000000000000000년 0개월 0일 0시간", ExperienceHistory.LevelUpDuration(146097000000000000000m, new(2026, 10, 7)));
    }

    [Fact]
    public void EstimatedDateSupportsOverHundredYearsAndOmitsOnlyUnrepresentableDates()
    {
        var now = DateTimeOffset.Parse("2026-10-07T13:00:00+09:00");
        Assert.Equal(DateTimeOffset.Parse("2426-10-07T13:00:00+09:00"), ExperienceHistory.LevelUpAt(146097, now));
        Assert.Null(ExperienceHistory.LevelUpAt(4694478, now));
    }
}
