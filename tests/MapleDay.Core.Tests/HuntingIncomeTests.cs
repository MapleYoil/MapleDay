namespace MapleDay.Core.Tests;

public class HuntingIncomeTests
{
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
