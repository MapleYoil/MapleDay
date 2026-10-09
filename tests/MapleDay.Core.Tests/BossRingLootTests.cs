using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class BossRingLootTests
{
    [Theory]
    [InlineData("스우", "hard,extreme")]
    [InlineData("데미안", "hard")]
    [InlineData("가디언 엔젤 슬라임", "chaos")]
    [InlineData("루시드", "hard")]
    [InlineData("윌", "hard")]
    [InlineData("더스크", "chaos")]
    [InlineData("진 힐라", "normal,hard")]
    [InlineData("듄켈", "hard")]
    [InlineData("검은 마법사", "hard,extreme")]
    [InlineData("선택받은 세렌", "normal,hard,extreme")]
    [InlineData("감시자 칼로스", "easy,normal,chaos,extreme")]
    [InlineData("최초의 대적자", "easy,normal,hard,extreme")]
    [InlineData("카링", "easy,normal,hard,extreme")]
    [InlineData("벨로나", "easy,normal,hard")]
    [InlineData("찬란한 흉성", "normal,hard")]
    [InlineData("림보", "normal,hard")]
    [InlineData("발드릭스", "normal,hard")]
    [InlineData("유피테르", "normal,hard")]
    public void Fourth_level_rings_require_a_box_that_can_contain_level_four(string boss, string eligible)
    {
        foreach (var difficulty in new[] { "easy", "normal", "hard", "chaos", "extreme", "unknown" })
        {
            var rings = BossLootCatalog.ForBoss(boss, difficulty).Where(item => item.Name.EndsWith("링 4레벨")).Select(item => item.Name).Order().ToArray();
            if (eligible.Split(',').Contains(difficulty))
                Assert.Equal(new[] { "리스트레인트 링 4레벨", "컨티뉴어스 링 4레벨" }.Order(), rings);
            else Assert.Empty(rings);
        }
    }

    [Fact]
    public void Rings_do_not_appear_for_unrelated_bosses_or_as_other_levels()
    {
        var rings = BossLootCatalog.Items.Where(item => item.Name.Contains("리스트레인트") || item.Name.Contains("컨티뉴어스")).ToArray();
        Assert.Equal(2, rings.Length);
        Assert.All(rings, item => Assert.EndsWith("4레벨", item.Name));
        Assert.DoesNotContain(BossLootCatalog.ForBoss("파풀라투스"), item => item.Name.EndsWith("링 4레벨"));
        var ring = rings.First();
        var record = new BossLootRecord("r", "c", new(2026, 10, 9), "스우", ring.Id, ring.Name, "equal", 1_000_000_000, 2, Difficulty: "hard");
        Assert.True(BossLoot.Valid(record)); Assert.Equal(500_000_000, record.Received);
        Assert.False(BossLoot.Valid(record with { Difficulty = "normal" }));
    }
}
