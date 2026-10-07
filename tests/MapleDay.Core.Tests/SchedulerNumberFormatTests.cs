using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerNumberFormatTests
{
    [Theory]
    [InlineData(29165, "29165")]
    [InlineData(1000, "1000")]
    [InlineData(100, "100")]
    public void Scheduler_counts_never_include_group_separators(long value, string expected)
        => Assert.Equal(expected, SchedulerNumberFormat.Count(value));
}
