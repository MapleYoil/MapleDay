using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class ManualWeeklyHistoryTests
{
    [Fact]
    public void Unspecified_weekday_uses_thursday_with_inclusive_range()
    {
        Assert.Equal(new[] { new DateOnly(2026, 9, 17), new DateOnly(2026, 9, 24), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8) },
            ManualWeeklyHistory.Dates(new(2026, 9, 16), new(2026, 10, 8), new(2026, 10, 8)));
        Assert.Equal(new[] { new DateOnly(2026, 10, 7) }, ManualWeeklyHistory.Dates(new(2026, 10, 1), new(2026, 10, 8), new(2026, 10, 8), DayOfWeek.Wednesday));
        Assert.Empty(ManualWeeklyHistory.Dates(new(2026, 10, 2), new(2026, 10, 3), new(2026, 10, 8)));
        Assert.Throws<ArgumentException>(() => ManualWeeklyHistory.Dates(new(2026, 10, 8), new(2026, 10, 1), new(2026, 10, 8)));
        Assert.Throws<ArgumentException>(() => ManualWeeklyHistory.Dates(new(2026, 10, 8), new(2026, 10, 15), new(2026, 10, 8)));
    }
    [Fact]
    public void Manual_records_use_historic_prices_and_party_sizes_without_faking_api_coverage()
    {
        var manual = new[] { new ManualWeeklyClear("c", "스우", "normal", new(2026, 9, 10)), new ManualWeeklyClear("c", "스우", "normal", new(2026, 9, 17)) };
        var income = BossIncome.Calculate([], new(2026, 10, 8), _ => 2, manual);
        Assert.Equal(2, income.Records.Count); Assert.Equal((16700000L + 8350000L) / 2, income.Total);
        Assert.All(income.Records, record => { Assert.True(record.Manual); Assert.False(record.DateEstimated); Assert.Equal(2, record.PartySize); });
        Assert.Empty(income.KnownDates); Assert.Equal(new DateOnly(2026, 9, 10), income.FirstDate);
    }
    [Fact]
    public void Confirmed_api_record_wins_and_aliases_or_difficulties_do_not_duplicate_weekly_rewards()
    {
        var snapshot = new SchedulerSnapshot(new(2026, 10, 1), new SchedulerState { Bosses = [new SchedulerBoss { Name = "스우", Difficulty = "normal", Cycle = "bossWeekly", Complete = System.Text.Json.JsonSerializer.SerializeToElement(true) }] }, true, DateTimeOffset.UtcNow);
        var manual = new[] { new ManualWeeklyClear("c", "스우", "hard", new(2026, 10, 3)), new ManualWeeklyClear("c", "스우", "normal", new(2026, 10, 1)) };
        var income = BossIncome.Calculate([snapshot], new(2026, 10, 8), manual: manual);
        var record = Assert.Single(income.Records); Assert.False(record.Manual); Assert.Equal("normal", record.Difficulty);
        Assert.Equal(ManualWeeklyHistory.Key(manual[0]), ManualWeeklyHistory.Key(manual[1]));
    }
    [Fact]
    public void Manual_weekly_records_share_the_twelve_reward_cap_with_api_records()
    {
        var selected = ManualWeeklyHistory.Choices.GroupBy(price => SchedulerBossHistory.BossKey(price.Name)).Take(15)
            .Select(group => new ManualWeeklyClear("c", group.First().Name, group.First().Difficulty, new(2026, 10, 1))).ToArray();
        var income = BossIncome.Calculate([], new(2026, 10, 8), manual: selected);
        Assert.Equal(15, income.Records.Count); Assert.Equal(12, income.Records.Count(record => record.Included));
        Assert.DoesNotContain(ManualWeeklyHistory.Choices, choice => choice.Name.Contains("검은"));
    }
}
