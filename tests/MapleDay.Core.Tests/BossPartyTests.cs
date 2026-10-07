using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class BossPartyTests
{
    [Theory]
    [InlineData("최초의 대적자")]
    [InlineData("찬란한 흉성")]
    [InlineData("림보")]
    [InlineData("벨로나")]
    [InlineData("유피테르")]
    [InlineData("발드릭스")]
    public void LimitedBossesClampOldAndNewSettingsToThree(string name)
    {
        Assert.Equal(3, BossParty.Maximum(name));
        Assert.Equal(3, BossParty.Clamp(name, 6));
        Assert.Equal(1, BossParty.Clamp(name, 0));
    }
    [Fact] public void OtherBossesStillAllowSix() => Assert.Equal(6, BossParty.Clamp("스우", 6));
    [Fact]
    public void BossButtonAndIncomeUseTheSamePeriodIdentity()
    {
        Assert.Equal("Weekly:2026-10-01:Bosses/icon_35.png", BossParty.RecordId("최초의 대적자", BossCycle.Weekly, new(2026, 10, 6)));
        Assert.Equal("Monthly:2026-10-01:Bosses/icon_25.png", BossParty.RecordId("검은 마법사", BossCycle.Monthly, new(2026, 10, 6)));
    }
}
