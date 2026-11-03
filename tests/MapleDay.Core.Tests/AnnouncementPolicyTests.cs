using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class AnnouncementPolicyTests
{
    [Fact]
    public void NoticesBeforeInstallationDoNotNotify()
    {
        var installed = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        Assert.False(AnnouncementPolicy.IsForInstallation(1_799_999_999, installed));
        Assert.True(AnnouncementPolicy.IsForInstallation(1_800_000_000, installed));
        Assert.True(AnnouncementPolicy.IsForInstallation(1_800_000_001, installed));
        Assert.False(AnnouncementPolicy.IsForInstallation(long.MaxValue, installed));
    }
}
