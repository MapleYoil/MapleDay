namespace MapleDay.Core.Tests;

public class HuntingIncomeTests
{
    [Fact]
    public void FragmentOnlyInputCalculatesWhileTypingWithEmptyMesoAndUnusedLimit()
    {
        var inputs = new HuntingInputValues("", "1", 599, LimitAmount: double.NaN, Bonus: double.NaN);
        var first = HuntingIncome.Draft("id", "owner", new(2026, 10, 10), 300, inputs);
        Assert.True(HuntingIncome.Valid(first)); Assert.Equal(5_990_000, first.Total); Assert.Equal(0, first.Meso); Assert.Null(first.LimitMeso);
        var next = HuntingIncome.Draft("id", "owner", first.Date, 300, inputs with { Fragments = "15" });
        Assert.Equal(89_850_000, next.Total);
        Assert.Equal(89_850_000, Assert.Single(HuntingIncome.Bulk([], next, next.Date, next.Date, next.Date).Records).Total);
    }

    [Fact]
    public void SimultaneousMesoAndFragmentInputsUseCurrentTextAndValidateOnlyActiveLimitFields()
    {
        var inputs = new HuntingInputValues("200,000,000", "15", 599, LimitAmount: double.NaN);
        var row = HuntingIncome.Draft("id", "owner", new(2026, 10, 10), 300, inputs, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(289_850_000, row.Total);
        var maximum = HuntingIncome.Draft("id", "owner", row.Date, 300,
            inputs with { Meso = "invalid", UseLimit = true, MaximumLimit = true, Bonus = 280 });
        Assert.Equal(874_000_000, maximum.Meso);
        Assert.Throws<ArgumentException>(() => HuntingIncome.Draft("id", "owner", row.Date, 300, inputs with { UseLimit = true }));
        Assert.Throws<ArgumentException>(() => HuntingIncome.Draft("id", "owner", row.Date, 300, inputs with { Fragments = "1.5" }));
    }
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
    public void Direct_limit_amount_applies_bonus_and_rejects_usage_above_character_maximum()
    {
        Assert.Equal(380000000, HuntingIncome.FromLimitAmount(300, 100000000, 280));
        Assert.Equal(874000000, HuntingIncome.FromLimitAmount(300, HuntingIncome.DailyLimit(300), 280));
        Assert.Equal(100000000, HuntingIncome.FromLimitAmount(300, 100000000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => HuntingIncome.FromLimitAmount(300, 230000001, 280));
        Assert.Throws<ArgumentOutOfRangeException>(() => HuntingIncome.FromLimitAmount(300, -1, 0));
        var record = new HuntingIncomeRecord("id", "owner", new(2026, 10, 9), 380000000, 0, 10000, 280, null, 300, 100000000);
        Assert.True(HuntingIncome.Valid(record));
        Assert.Equal(100000000, HuntingIncome.LimitUsage(record));
        Assert.False(HuntingIncome.Valid(record with { LimitMeso = 230000001 }));
        Assert.False(HuntingIncome.Valid(record with { Level = 0 }));
        var result = HuntingIncome.Bulk([], record, record.Date.AddDays(-1), record.Date, record.Date);
        Assert.All(result.Records, row => { Assert.Equal(100000000, row.LimitMeso); Assert.Equal(380000000, row.Meso); });
    }
    [Fact]
    public void Existing_percent_records_restore_base_usage_without_changing_saved_income()
    {
        var old = new HuntingIncomeRecord("id", "owner", new(2026, 10, 9), 399000000, 0, 10000, 280, 50, 291);
        Assert.Equal(105000000, HuntingIncome.LimitUsage(old));
        Assert.Equal(old.Meso, HuntingIncome.FromLimitAmount(old.Level, HuntingIncome.LimitUsage(old)!.Value, old.MesoBonus));
        Assert.Equal(210000000, HuntingIncome.LimitUsage(old with { LimitPercent = 100 }));
        Assert.Null(HuntingIncome.LimitUsage(old with { LimitPercent = null }));
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
