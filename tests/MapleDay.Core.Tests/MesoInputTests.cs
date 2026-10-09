namespace MapleDay.Core.Tests;

public sealed class MesoInputTests
{
    [Theory]
    [InlineData(0, "0억 메소")]
    [InlineData(1, "0.00000001억 메소")]
    [InlineData(8_350_000, "0.0835억 메소")]
    [InlineData(132_500_000, "1.325억 메소")]
    [InlineData(2_000_000_000, "20억 메소")]
    [InlineData(1_000_000_000_000, "10000억 메소")]
    [InlineData(1_000_000_000_000_000, "10000000억 메소")]
    public void Eok_display_retains_the_full_meso_amount(long amount, string expected)
        => Assert.Equal(expected, BossIncome.Money(amount));

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("2000000000", true, 2_000_000_000)]
    [InlineData(" 2,000,000,000 ", true, 2_000_000_000)]
    [InlineData("1000000000000000", true, BossLoot.MaximumAmount)]
    [InlineData("1000000000000001", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("2.5", false, 0)]
    [InlineData("2e9", false, 0)]
    [InlineData("20억", false, 0)]
    [InlineData("2,00,000", false, 0)]
    [InlineData("9223372036854775808", false, 0)]
    public void Live_input_accepts_exact_integer_meso_and_rejects_incomplete_or_invalid_amounts(string input, bool valid, long amount)
    {
        Assert.Equal(valid, BossLoot.TryParseAmount(input, out var parsed));
        if (valid) Assert.Equal(amount, parsed);
    }

    [Fact]
    public void Cash_and_combined_modes_keep_the_configured_exchange_rate()
    {
        Assert.Equal("30,000원", new IncomeDisplay("cash").Format(2_000_000_000));
        Assert.Equal("20억 메소 · 30,000원", new IncomeDisplay("both").Format(2_000_000_000));
    }
}
