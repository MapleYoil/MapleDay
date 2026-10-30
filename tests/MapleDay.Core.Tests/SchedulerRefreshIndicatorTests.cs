using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerRefreshIndicatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 20, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void RefreshBecomesDueAtTwoHours()
    {
        Assert.False(SchedulerRefreshIndicator.IsDue([Now.AddHours(-2).AddSeconds(1)], Now));
        Assert.True(SchedulerRefreshIndicator.IsDue([Now.AddHours(-2)], Now));
    }

    [Fact]
    public void AnyMissingOrStaleCharacterMakesRefreshDue()
    {
        Assert.True(SchedulerRefreshIndicator.IsDue([Now.AddMinutes(-10), null], Now));
        Assert.True(SchedulerRefreshIndicator.IsDue([Now.AddMinutes(-10), Now.AddHours(-3)], Now));
        Assert.False(SchedulerRefreshIndicator.IsDue([Now.AddMinutes(-10), Now.AddHours(1)], Now));
    }

    [Fact]
    public void EmptySchedulerDoesNotRequestRefresh() =>
        Assert.False(SchedulerRefreshIndicator.IsDue([], Now));
}
