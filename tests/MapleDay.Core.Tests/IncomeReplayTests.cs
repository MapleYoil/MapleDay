namespace MapleDay.Core.Tests;

public sealed class IncomeReplayTests
{
    [Fact]
    public void Large_replay_income_uses_trillion_units_without_changing_the_amount()
    {
        Assert.Equal("4.250조", IncomeReplay.CompactMeso(4_250_000_000_000));
        Assert.Equal("1.000조", IncomeReplay.CompactMeso(1_000_000_000_000));
        Assert.Equal("40.50억", IncomeReplay.CompactMeso(4_050_000_000));
        Assert.Equal("150.0만", IncomeReplay.CompactMeso(1_500_000));
    }
    [Fact]
    public void Cash_rate_and_display_modes_keep_exact_meso_and_round_won()
    {
        Assert.Equal(1500m, new IncomeDisplay().Cash(100_000_000));
        Assert.Equal("1,500원", new IncomeDisplay("cash").Format(100_000_000));
        Assert.Contains("1,500원", new IncomeDisplay("both").Format(100_000_000));
        Assert.Equal(2000m, new IncomeDisplay("cash", 2000).Cash(100_000_000));
        Assert.Equal(1m, new IncomeDisplay().Cash(50_000));
        Assert.Equal(1500m, new IncomeDisplay("cash", -1).Rate);
    }
    [Fact]
    public void Replay_follows_dates_and_increases_counts_and_totals_to_exact_final_values()
    {
        var replay = new IncomeReplay(new ReplayClear[] { new(new(2026, 10, 8), "스우", 200), new(new(2026, 10, 7), "루시드", 100) });
        Assert.Equal(0, replay.At(0).Total);
        Assert.Equal(100, replay.At(.5).Total);
        Assert.Equal("루시드", replay.At(.5).Bosses[0].Name);
        Assert.Equal(1, replay.At(.5).Count);
        Assert.Equal(200, replay.At(.75).Total);
        Assert.Equal(300, replay.At(1).Total);
        Assert.Equal(2, replay.At(1).Count);
        Assert.Equal("스우", replay.At(1).Bosses[0].Name);
        Assert.Equal(300, replay.At(1).Bosses.Sum(row => row.Meso));
    }
    [Fact]
    public void Replay_excludes_daily_monthly_unpriced_estimated_and_cap_excluded_records()
    {
        var date = new DateOnly(2026, 10, 8);
        var record = new BossIncomeRecord("id", "스우", "hard", BossCycle.Weekly, date, date, date, false, null, 2, 123, true);
        var replay = new IncomeReplay(new[] { record, record with { Cycle = BossCycle.Daily }, record with { Cycle = BossCycle.Monthly },
            record with { DateEstimated = true }, record with { Included = false }, record with { Meso = null } });
        Assert.Single(replay.Clears); Assert.Equal(123, replay.Total);
        Assert.Equal(1, replay.At(1).Count);
    }
    [Fact]
    public void Empty_and_invalid_clock_values_are_safe()
    {
        var empty = new IncomeReplay(Array.Empty<ReplayClear>());
        Assert.Empty(empty.At(1).Bosses); Assert.Null(empty.At(1).Date);
        var replay = new IncomeReplay(new[] { new ReplayClear(new(2026, 10, 8), "스우", 100) });
        Assert.Equal(0, replay.At(double.NaN).Total); Assert.Equal(100, replay.At(2).Total);
    }
}
