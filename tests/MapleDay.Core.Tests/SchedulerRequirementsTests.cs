using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerRequirementsTests
{
    [Theory]
    [InlineData("[일일 퀘스트] 기어드락 크로노스의 잔...", 291, 295, true)]
    [InlineData("[일일 퀘스트] 기어드락 크로노스의 잔...", 295, 295, false)]
    [InlineData("[일일 퀘스트] 탈라하트 고대신의 힘 조사", 289, 290, true)]
    [InlineData("[일일 퀘스트] 탈라하트 고대신의 힘 조사", 290, 290, false)]
    [InlineData("[일일 퀘스트] 카르시온 복구 지원", 285, 285, false)]
    [InlineData("[일일 퀘스트] 호텔 아르크스 주변 청소", 264, 265, true)]
    [InlineData("[일일 퀘스트] 소멸의 여로 조사", 199, 200, true)]
    public void DailyQuestLevelGatesUseTheSuppliedRegionsAndInclusiveBoundary(string name, int level, int required, bool blocked)
    {
        var entry = new SchedulerEntry(name, "0 / 100", false, SchedulerSection.Daily, Type: "quest");
        Assert.Equal(required, SchedulerRequirements.RequiredLevel(entry));
        Assert.Equal(blocked, SchedulerRequirements.IsLevelBlocked(entry, level));
    }

    [Theory]
    [InlineData("유피테르", "bossWeekly", 291, 295, true)]
    [InlineData("유피테르", "bossWeekly", 295, 295, false)]
    [InlineData("최초의 대적자", "bossWeekly", 270, 270, false)]
    [InlineData("시즌 보스 벨로나", "bossWeekly", 279, 280, true)]
    [InlineData("검은 마법사", "bossMonthly", 254, 255, true)]
    [InlineData("검은 마법사", "bossMonthly", 255, 255, false)]
    [InlineData("매그너스", "bossDaily", 100, 0, false)]
    [InlineData("매그너스", "daily", 100, 0, false)]
    public void BossGatesApplyToWeeklyAndMonthlyButNotDaily(string name, string cycle, int level, int required, bool blocked)
    {
        var entry = new SchedulerEntry(name, "", false, SchedulerSection.Boss, Cycle: cycle);
        Assert.Equal(required, SchedulerRequirements.RequiredLevel(entry));
        Assert.Equal(blocked, SchedulerRequirements.IsLevelBlocked(entry, level));
    }

    [Theory]
    [InlineData("0", 0, false, false)]
    [InlineData("1", 99, false, false)]
    [InlineData("1", 100, true, false)]
    [InlineData("2", 100, false, true)]
    public void ReadyButtonDoesNotTurnInAQuestUntilState2(string state, long now, bool ready, bool complete)
    {
        var entry = Assert.Single(SchedulerEntries.Create(new SchedulerState { Daily = [new SchedulerContent
        {
            Name = "[일일 퀘스트] 카르시온", Type = "quest", QuestState = state, Now = now, Maximum = 100,
            Registration = System.Text.Json.JsonSerializer.SerializeToElement(true)
        }] }));
        Assert.Equal(ready, SchedulerRequirements.IsQuestReady(entry));
        Assert.Equal(complete, entry.Complete);
    }
}
