using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SoulEtherTests
{
    [Theory]
    [InlineData("최초의 대적자", 1, true)]
    [InlineData("카링", 1, true)]
    [InlineData("벨로나", 2, false)]
    [InlineData("찬란한 흉성", 2, false)]
    [InlineData("림보", 3, false)]
    [InlineData("발드릭스", 3, false)]
    [InlineData("유피테르", 4, false)]
    public void Each_boss_exposes_only_its_ether_stage_and_supported_difficulties(string boss, int stage, bool extreme)
    {
        foreach (var difficulty in new[] { "easy", "normal", "hard", "extreme", "chaos", "unknown" })
        {
            var ether = BossLootCatalog.ForBoss(boss, difficulty).Where(item => item.Name.Contains("소울 에테르")).ToArray();
            if (difficulty is "normal" or "hard" || difficulty == "extreme" && extreme)
                Assert.Equal($"{stage}단계 소울 에테르", Assert.Single(ether).Name);
            else Assert.Empty(ether);
        }
    }

    [Fact]
    public void Ether_is_not_a_drop_for_unrelated_bosses_and_wrong_stage_cannot_be_saved()
    {
        var ethers = BossLootCatalog.Items.Where(item => item.Name.Contains("소울 에테르")).ToArray();
        Assert.Equal(4, ethers.Length);
        Assert.DoesNotContain(BossLootCatalog.ForBoss("스우"), item => item.Name.Contains("소울 에테르"));
        var first = ethers.Single(item => item.Name.StartsWith("1단계"));
        var record = new BossLootRecord("l", "c", new(2026, 10, 9), "카링", first.Id, first.Name, "ratio", 1_000_000_000, 3, "2:1:1", 1, "hard");
        Assert.True(BossLoot.Valid(record)); Assert.Equal(500_000_000, record.Received);
        Assert.False(BossLoot.Valid(record with { Boss = "벨로나" }));
        Assert.False(BossLoot.Valid(record with { Difficulty = "easy" }));
    }
}
