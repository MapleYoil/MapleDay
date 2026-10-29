namespace MapleDay.Core.Tests;

public sealed class IntegerInputFormatterTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("999", "999")]
    [InlineData("1000", "1,000")]
    [InlineData("123456789", "123,456,789")]
    [InlineData("1,234,567", "1,234,567")]
    public void FormatsThousandsWhileTyping(string input, string expected)
    {
        Assert.Equal(expected, IntegerInputFormatter.Format(input, input.Length).Text);
    }

    [Fact]
    public void KeepsCaretAtTheSameDigitBoundary()
    {
        var result = IntegerInputFormatter.Format("1234", 1);

        Assert.Equal("1,234", result.Text);
        Assert.Equal(1, result.Caret);
    }
}
