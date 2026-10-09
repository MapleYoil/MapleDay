namespace MapleDay.Core.Tests;

public class HuntingIncomeTests
{
    [Fact]
    public void Bulk_adds_daily_meso_and_fragments_inclusive_and_preserves_existing_by_default()
    {
        var daily = new HuntingIncomeRecord("draft", "owner", new(2026, 10, 9), 874000000, 150, 5000000, 280, 100, 300);
        var old = daily with { Id = "old", Date = new(2026, 10, 8), Meso = 100, FragmentUnitPrice = 10000 };
        var other = old with { Id = "other", Ocid = "other" };
        var result = HuntingIncome.Bulk([old, other], daily, new(2026, 10, 7), new(2026, 10, 9), new(2026, 10, 9));
        Assert.Equal((2, 0, 1), (result.Added, result.Updated, result.Skipped));
        Assert.Contains(old, result.Records); Assert.Contains(other, result.Records);
        Assert.Equal(4, result.Records.Select(row => row.Id).Distinct().Count());
        Assert.Equal(3, result.Records.Count(row => row.Ocid == "owner"));
        Assert.All(result.Records.Where(row => row.Id is not "old" and not "other"), row => {
            Assert.Equal(874000000, row.Meso); Assert.Equal(150, row.Fragments);
            Assert.Equal(5000000, row.FragmentUnitPrice); Assert.Equal(280, row.MesoBonus); Assert.Equal(300, row.Level);
        });
        var repeat = HuntingIncome.Bulk(result.Records, daily, new(2026, 10, 7), new(2026, 10, 9), new(2026, 10, 9));
        Assert.Equal((0, 0, 3), (repeat.Added, repeat.Updated, repeat.Skipped));
        Assert.Equal(result.Records, repeat.Records);
    }
    [Fact]
    public void Bulk_overwrite_preserves_record_ids_and_cleans_same_day_duplicates()
    {
        var daily = new HuntingIncomeRecord("draft", "owner", new(2026, 10, 9), 10, 20, 5000000);
        var old = daily with { Id = "old", Meso = 200 };
        var result = HuntingIncome.Bulk([old, old with { Id = "duplicate" }], daily, old.Date, old.Date, old.Date, true);
        Assert.Equal((0, 1, 0), (result.Added, result.Updated, result.Skipped));
        Assert.Equal(daily with { Id = "old" }, Assert.Single(result.Records));
        Assert.Equal(200, old.Meso);
        Assert.Throws<ArgumentException>(() => HuntingIncome.Bulk([], daily, old.Date.AddDays(1), old.Date, old.Date));
        Assert.Throws<ArgumentException>(() => HuntingIncome.Bulk([], daily, old.Date, old.Date.AddDays(1), old.Date));
        Assert.Throws<ArgumentException>(() => HuntingIncome.Bulk([], daily with { Meso = -1 }, old.Date, old.Date, old.Date));
    }
    [Theory]
    [InlineData(99, 20000000)] [InlineData(100, 40000000)]
    [InlineData(200, 80000000)] [InlineData(204, 80000000)] [InlineData(205, 85000000)]
    [InlineData(259, 135000000)] [InlineData(260, 150000000)]
    [InlineData(291, 210000000)] [InlineData(300, 230000000)]
    public void Level_boundaries_match_basic_daily_limit(int level, long expected)
        => Assert.Equal(expected, HuntingIncome.DailyLimit(level));
    [Fact]
    public void Bonus_adds_to_base_and_does_not_change_limit_usage()
    {
        Assert.Equal(210000000, HuntingIncome.FromLimit(291, 100, 0));
        Assert.Equal(420000000, HuntingIncome.FromLimit(291, 100, 100));
        Assert.Equal(210000000, HuntingIncome.FromLimit(291, 50, 100));
        Assert.Equal(10000, HuntingIncome.FragmentPrice(1));
        Assert.Equal(99990000, HuntingIncome.FragmentPrice(9999));
        Assert.Throws<ArgumentOutOfRangeException>(() => HuntingIncome.FragmentPrice(10000));
        Assert.Throws<ArgumentOutOfRangeException>(() => HuntingIncome.FromLimit(291, 101, 0));
    }
    [Fact]
    public void Stored_prices_are_fixed_and_combined_periods_do_not_double_count()
    {
        var record = new HuntingIncomeRecord("one", "character", new(2026, 10, 8), 420000000, 20, 1230000, 100, 100, 291);
        var rows = new[] { record, record, record with { Id = "old", Date = new(2026, 10, 1) }, record with { Id = "invalid", FragmentUnitPrice = 1 } };
        Assert.Equal(444600000, HuntingIncome.Sum(rows, new(2026, 10, 8), new(2026, 10, 9)));
        Assert.Equal(889200000, HuntingIncome.Sum(rows, new(2026, 10, 1), new(2026, 10, 9)));
    }
    [Fact]
    public void Hunting_replay_counts_fragments_without_inflating_record_count_or_meso()
    {
        var records = Enumerable.Range(0, 200).Select(day => new HuntingIncomeRecord(day.ToString(), "character",
            new DateOnly(2026, 3, 24).AddDays(day), HuntingIncome.FromLimit(300, 100, 280), 150, 5000000, 280, 100, 300));
        var replay = HuntingIncome.Replay(records, _ => "사냥");
        Assert.Equal("hunting", replay.Category);
        var final = replay.At(1);
        Assert.Equal(324800000000, final.Total);
        Assert.Equal(200, final.Count);
        Assert.Equal(30000, Assert.Single(final.CollectedLoot!).Count);
        Assert.Equal(150000000000, final.CollectedLoot![0].Meso);
    }
}
