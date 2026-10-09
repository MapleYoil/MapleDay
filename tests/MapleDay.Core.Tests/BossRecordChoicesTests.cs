using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class BossRecordChoicesTests
{
    [Theory]
    [InlineData("진 힐라","진힐라",true)]
    [InlineData("감시자 칼로스"," 칼로스 ",true)]
    [InlineData("스우","윌",false)]
    [InlineData("스우","",true)]
    public void Search_matches_names_without_spaces(string name,string query,bool expected)
        => Assert.Equal(expected,BossRecordChoices.MatchesSearch(name,query));
    [Fact]
    public void Every_priced_boss_difficulty_has_verified_health_and_alias_lookup()
    {
        Assert.Equal(54, BossRecordChoices.Health.Count);
        Assert.All(ManualWeeklyHistory.SingleChoices, choice => Assert.True(BossRecordChoices.FindHealth(choice.Name, choice.Difficulty) > 0));
        Assert.Equal(BossRecordChoices.FindHealth("찬란한 흉성", "normal"), BossRecordChoices.FindHealth("시즌 보스 찬란한 흉성", "노멀"));
        Assert.False(BossRecordChoices.Allows("알 수 없는 보스", "hard"));
    }

    [Fact]
    public void Add_choices_start_at_hard_suu_and_omit_lower_difficulties()
    {
        Assert.Equal(22_800_000_000_000L, BossRecordChoices.MinimumHealth);
        Assert.Equal(("스우", "hard"), (BossRecordChoices.Single[0].Name, BossRecordChoices.Single[0].Difficulty));
        Assert.Equal(("데미안", "hard"), (BossRecordChoices.Single[1].Name, BossRecordChoices.Single[1].Difficulty));
        Assert.Equal(("진 힐라", "normal"), (BossRecordChoices.Single[2].Name, BossRecordChoices.Single[2].Difficulty));
        Assert.All(BossRecordChoices.Single, choice => Assert.True(choice.Health >= BossRecordChoices.MinimumHealth));
        Assert.DoesNotContain(BossRecordChoices.Single, choice => choice.Name == "윌" && choice.Difficulty != "hard");
        Assert.DoesNotContain(BossRecordChoices.Single, choice => choice.Name == "루시드" && choice.Difficulty != "hard");
        Assert.DoesNotContain(BossRecordChoices.Single, choice => choice.Name == "더스크" && choice.Difficulty == "normal");
    }

    [Fact]
    public void Single_and_bulk_choices_follow_health_across_bosses_and_difficulties()
    {
        Assert.Equal(38, BossRecordChoices.Single.Count);
        Assert.Equal(36, BossRecordChoices.Weekly.Count);
        foreach (var choices in new[] { BossRecordChoices.Single, BossRecordChoices.Weekly })
        {
            Assert.Equal(choices.Select(choice => choice.Health).Order(), choices.Select(choice => choice.Health));
            Assert.Equal(choices.Count, choices.DistinctBy(choice => (choice.Name, choice.Difficulty)).Count());
            var selected = choices.Where(choice => choice.Label is "하드 윌" or "카오스 더스크" or "하드 루시드").ToArray();
            Assert.Equal(new[] { "하드 윌", "카오스 더스크", "하드 루시드" }, selected.Select(choice => choice.Label));
            Assert.True(selected[1].Price.Meso > selected[2].Price.Meso);
        }
        Assert.All(BossRecordChoices.Weekly, choice => Assert.Equal(BossCycle.Weekly, ManualWeeklyHistory.CycleFor(choice.Name)));
        Assert.Contains(BossRecordChoices.Single, choice => choice.Name == "검은 마법사" && choice.Difficulty == "hard");
    }

    [Fact]
    public void Existing_lower_boss_records_stay_valid_and_keep_their_income()
    {
        var clear = new ManualWeeklyClear("c", "스우", "normal", new(2026, 10, 1));
        Assert.False(BossRecordChoices.Allows(clear.Name, clear.Difficulty));
        Assert.True(ManualWeeklyHistory.Valid(clear));
        var record = Assert.Single(BossIncome.Calculate([], clear.Date, manual: [clear]).Records);
        Assert.Equal(8_350_000L, record.Meso);
    }
}
