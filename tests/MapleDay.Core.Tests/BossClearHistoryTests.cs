using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class BossClearHistoryTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);
    private static SchedulerSnapshot Snapshot(params SchedulerBoss[] bosses) => new(Day,
        new SchedulerState { Bosses = bosses.ToList() }, true, DateTimeOffset.UtcNow);
    private static SchedulerBoss Boss(string name, string difficulty = "hard") => new() { Name = name,
        Difficulty = difficulty, Cycle = "weekly", Complete = JsonSerializer.SerializeToElement(true) };

    [Fact]
    public void Api_edit_survives_fresh_snapshots_and_moves_date_and_income()
    {
        var snapshots = new[] { Snapshot(Boss("스우")) };
        var original = Assert.Single(BossIncome.Calculate(snapshots, Day).Records);
        var replacement = new ManualWeeklyClear("c", "데미안", "normal", Day.AddDays(1));
        var plan = BossClearHistory.Edit("c", original, replacement, [], []);
        var edited = Assert.Single(BossIncome.Calculate(snapshots, Day.AddDays(1), _ => 2, changes: plan.Changes).Records);
        Assert.Equal("데미안", edited.Name); Assert.Equal(Day.AddDays(1), edited.Date);
        Assert.Equal(8750000 / 2, edited.Meso); Assert.True(edited.Manual);
    }

    [Fact]
    public void Repeated_edit_and_delete_do_not_restore_api_original_or_other_character_loot()
    {
        var snapshots = new[] { Snapshot(Boss("스우")) };
        var source = Assert.Single(BossIncome.Calculate(snapshots, Day).Records);
        var loot = new BossLootRecord("one", "c", Day.AddDays(4), "스우", "", "정산", "received", 123, 1,
            Difficulty: "hard", ClearId: source.Id);
        var other = loot with { Id = "two", Ocid = "d" };
        var moved = new ManualWeeklyClear("c", "데미안", "normal", Day.AddDays(7));
        var plan = BossClearHistory.Edit("c", source, moved, [], [loot, other]);
        var current = Assert.Single(BossIncome.Calculate(snapshots, moved.Date, changes: plan.Changes).Records);
        Assert.True(BossLoot.ForClear(plan.Loot[0], "c", current));
        Assert.Equal(123, plan.Loot[0].Received); Assert.Equal(loot.Date, plan.Loot[0].Date);
        var again = BossClearHistory.Edit("c", current, moved with { Name = "스우" }, plan.Changes, plan.Loot);
        var final = Assert.Single(BossIncome.Calculate(snapshots, moved.Date, changes: again.Changes).Records);
        var deleted = BossClearHistory.Edit("c", final, null, again.Changes, again.Loot);
        Assert.Empty(BossIncome.Calculate(snapshots, moved.Date, changes: deleted.Changes).Records);
        Assert.Equal(other, Assert.Single(deleted.Loot));
        Assert.Contains(deleted.Changes, change => change.OriginalId == source.Id && change.Replacement is null);
        var later = Snapshot(Boss("데미안", "normal"), Boss("스우", "normal")) with { Date = moved.Date };
        Assert.Empty(BossIncome.Calculate(snapshots.Append(later), moved.Date, changes: deleted.Changes).Records);
    }

    [Fact]
    public void Wrong_reward_difficulty_is_rejected_without_mutating_records()
    {
        var source = Assert.Single(BossIncome.Calculate([Snapshot(Boss("스우"))], Day).Records);
        var item = BossLootCatalog.ForBoss("스우", "hard").First();
        var loot = new BossLootRecord("loot", "c", Day, "스우", item.Id, item.Name, "equal", 100, 2,
            Difficulty: "hard", ClearId: source.Id);
        List<BossClearChange> changes = []; List<BossLootRecord> stored = [loot];
        Assert.Throws<ArgumentException>(() => BossClearHistory.Edit("c", source,
            new("c", "스우", "normal", Day), changes, stored));
        Assert.Empty(changes); Assert.Equal(loot, Assert.Single(stored));
    }

    [Fact]
    public void Deletion_recalculates_weekly_cap_before_summing()
    {
        var manual = ManualWeeklyHistory.Choices.DistinctBy(price => SchedulerBossHistory.BossKey(price.Name)).Take(13)
            .Select(price => new ManualWeeklyClear("c", price.Name, price.Difficulty, Day)).ToArray();
        var before = BossIncome.Calculate([], Day, manual: manual);
        Assert.Equal(1, before.CapExcluded);
        var removed = before.Records.First(record => record.Included);
        var deleted = BossClearHistory.Edit("c", removed, null, [], []);
        var after = BossIncome.Calculate([], Day, manual: manual, changes: deleted.Changes);
        Assert.Equal(12, after.Records.Count); Assert.Equal(0, after.CapExcluded);
        Assert.Equal(after.Records.Sum(record => record.Meso ?? 0), after.Total);
    }

    [Fact]
    public void Explicit_add_restores_a_deleted_period_without_reintroducing_api_value()
    {
        var source = Assert.Single(BossIncome.Calculate([Snapshot(Boss("스우"))], Day).Records);
        var deleted = BossClearHistory.Edit("c", source, null, [], []);
        var manual = new ManualWeeklyClear("c", "스우", "normal", Day.AddDays(1));
        var restored = BossClearHistory.Restore(deleted.Changes, [manual]);
        var result = Assert.Single(BossIncome.Calculate([Snapshot(Boss("스우"))], Day.AddDays(1), manual: [manual], changes: restored).Records);
        Assert.Equal("normal", result.Difficulty); Assert.Equal(manual.Date, result.Date);
    }
}
