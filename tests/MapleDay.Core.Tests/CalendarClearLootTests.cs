using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class CalendarClearLootTests
{
    [Fact]
    public void Monthly_single_clears_deduplicate_by_month_and_do_not_consume_weekly_cap()
    {
        var weekly = ManualWeeklyHistory.Choices.GroupBy(price => SchedulerBossHistory.BossKey(price.Name)).Take(15)
            .Select(group => new ManualWeeklyClear("c", group.First().Name, group.First().Difficulty, new(2026, 10, 1)));
        var monthly = new[] {
            new ManualWeeklyClear("c", "검은 마법사", "hard", new(2026, 9, 30), BossCycle.Monthly),
            new ManualWeeklyClear("c", "검은 마법사", "hard", new(2026, 10, 1), BossCycle.Monthly),
            new ManualWeeklyClear("c", "검은 마법사", "extreme", new(2026, 10, 8), BossCycle.Monthly) };
        var result = BossIncome.Calculate([], new(2026, 10, 9), _ => 3, weekly.Concat(monthly));
        Assert.Equal(12, result.Records.Count(clear => clear.Included && clear.Cycle == BossCycle.Weekly));
        var months = result.Records.Where(clear => clear.Cycle == BossCycle.Monthly).ToArray();
        Assert.Equal(2, months.Length);
        Assert.All(months, clear => { Assert.True(clear.Manual); Assert.True(clear.Included); Assert.Equal(clear.Price!.Meso / 3, clear.Meso); });
        Assert.Contains(months, clear => clear.PeriodStart == new DateOnly(2026, 9, 1));
        Assert.Empty(result.KnownDates);
    }

    [Fact]
    public void Incorrect_cycle_or_difficulty_cannot_create_income()
    {
        var date = new DateOnly(2026, 10, 9);
        ManualWeeklyClear[] invalid = [new("c", "스우", "hard", date, BossCycle.Daily),
            new("c", "검은 마법사", "hard", date), new("c", "스우", "easy", date), new("", "스우", "hard", date)];
        Assert.All(invalid, clear => Assert.False(ManualWeeklyHistory.Valid(clear)));
        Assert.Empty(BossIncome.Calculate([], date, manual: invalid).Records);
    }

    [Fact]
    public void Linked_loot_survives_later_settlement_and_replacement_by_api()
    {
        var date = new DateOnly(2026, 10, 1);
        var manual = new ManualWeeklyClear("c", "스우", "hard", date);
        var clear = Assert.Single(BossIncome.Calculate([], date, manual: [manual]).Records);
        var loot = new BossLootRecord("l", "c", date.AddDays(8), "스우", "", "물욕템 정산", "received", 999, 2, Difficulty: "hard", ClearId: clear.Id);
        Assert.True(BossLoot.ForClear(loot, "c", clear));
        Assert.False(BossLoot.ForClear(loot, "other", clear));
        Assert.False(BossLoot.ForClear(loot with { Boss = "루시드" }, "c", clear));
        Assert.False(BossLoot.ForClear(loot with { Difficulty = "normal" }, "c", clear));
        Assert.False(BossLoot.ForClear(loot with { ClearId = "other-week" }, "c", clear));
        var api = new SchedulerSnapshot(date, new SchedulerState { Bosses = [new SchedulerBoss {
            Name = "스우", Difficulty = "hard", Cycle = "bossWeekly", Complete = JsonSerializer.SerializeToElement(true) }] }, true, DateTimeOffset.UtcNow);
        var confirmed = Assert.Single(BossIncome.Calculate([api], date, manual: [manual]).Records);
        Assert.False(confirmed.Manual);
        Assert.True(BossLoot.ForClear(loot, "c", confirmed));
        var calendar = IncomeCalendar.Month(new(2026, 10, 1), [new("c", "캐릭터", clear)], [], [new("캐릭터", loot)]);
        Assert.Equal(999, calendar.Single(day => day.Date == loot.Date).Meso);
        Assert.Equal(clear.Meso, calendar.Single(day => day.Date == clear.Date).Meso);
    }

    [Fact]
    public void Legacy_loot_matches_only_same_boss_character_period_and_difficulty()
    {
        var date = new DateOnly(2026, 10, 1);
        var clear = Assert.Single(BossIncome.Calculate([], date, manual: [new("c", "스우", "hard", date)]).Records);
        var legacy = new BossLootRecord("l", "c", date.AddDays(2), "스우", "", "물욕템 정산", "received", 999, 1);
        Assert.True(BossLoot.ForClear(legacy, "c", clear));
        Assert.False(BossLoot.ForClear(legacy with { Date = date.AddDays(7) }, "c", clear));
        var second = legacy with { Id = "second", Mode = "equal", Amount = 600, PartySize = 3 };
        Assert.Equal(1199, BossLoot.Sum([legacy, second], date, date.AddDays(6)));
    }
}
