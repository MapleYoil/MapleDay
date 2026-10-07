using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerTests
{
    [Fact]
    public void FlagsUseTheirValueAndUnregisteredClearsHaveTheirOwnSection()
    {
        var state = Parse("""
            {
              "daily_contents":[
                {"content_name":"등록","registration_flag":"true","now_count":0,"max_count":2},
                {"content_name":"미등록","registration_flag":"false"},
                {"content_name":"누락"},
                {"content_name":"null","registration_flag":null}
              ],
              "boss_contents":[
                {"content_name":"등록 완료","registration_flag":true,"complete_flag":"true","list_order_no":2,"difficulty":"hard","cycle":"bossWeekly"},
                {"content_name":"미등록 완료","registration_flag":"false","complete_flag":true,"list_order_no":1},
                {"content_name":"미등록 미완료","registration_flag":"false","complete_flag":"false"},
                {"content_name":"등록 미완료","registration_flag":"true","complete_flag":"false","list_order_no":3}
              ]
            }
            """);
        var entries = SchedulerEntries.Create(state);
        Assert.Equal(4, entries.Count);
        Assert.Equal("등록", entries[0].Name);
        Assert.False(entries[0].Complete);
        Assert.Equal(SchedulerSection.UnregisteredBoss, entries[1].Section);
        Assert.True(entries[1].Complete);
        Assert.Equal("하드 · 주간", entries[2].Detail);
        Assert.Equal(SchedulerSection.Boss, entries[2].Section);
        Assert.Equal("bossWeekly", entries[2].Cycle);
        Assert.True(entries[2].Complete);
        Assert.False(entries[3].Complete);
    }

    [Theory]
    [InlineData(0, 0, "0", false, 0)]
    [InlineData(37, 0, "1", false, 37)]
    [InlineData(100, 100, "1", false, 100)]
    [InlineData(100, 100, null, false, 100)]
    [InlineData(0, 0, "2", true, 100)]
    [InlineData(100, 17, "2", true, 100)]
    public void DailyQuestsAlwaysHaveMaximum100AndRequireQuestState2(long now, long maximum, string? questState, bool complete, long shownNow)
    {
        var state = new SchedulerState { Daily = [new SchedulerContent
        {
            Name = "[일일 퀘스트] 소멸의 여로", Type = "quest", Registration = Flag(true), Now = now, Maximum = maximum, QuestState = questState
        }] };
        var entry = Assert.Single(SchedulerEntries.Create(state));
        Assert.Equal(100, entry.Maximum);
        Assert.Equal(shownNow, entry.Now);
        Assert.Equal($"{shownNow} / 100", entry.Detail);
        Assert.Equal(complete, entry.Complete);
        Assert.Equal("quest", entry.Type);
        Assert.Equal(questState, entry.QuestState);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void EpicDungeonShowsStagesAndIsCompleteOnlyAtStage5(long stage, bool complete)
    {
        var state = new SchedulerState { Weekly = [new SchedulerContent
        {
            Name = "에픽 던전 : 악몽선경", Type = "contents", Registration = Flag(true), Now = stage, Maximum = 0
        }] };
        var entry = Assert.Single(SchedulerEntries.Create(state));
        Assert.Equal(5, entry.Maximum);
        Assert.Equal($"STAGE {stage}", entry.Detail);
        Assert.Equal(complete, entry.Complete);
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("2", true)]
    public void WeeklyQuestsAlsoRequireQuestState2(string questState, bool complete)
    {
        var state = new SchedulerState { Weekly = [new SchedulerContent
        {
            Name = "[몬스터파크] 익스트림 몬스터파커에 도전해보겠나?", Type = "quest", Registration = Flag(true), Now = 1, Maximum = 1, QuestState = questState
        }] };
        Assert.Equal(complete, Assert.Single(SchedulerEntries.Create(state)).Complete);
    }

    [Theory]
    [InlineData(0, 14, false)]
    [InlineData(13, 14, false)]
    [InlineData(14, 14, true)]
    [InlineData(0, 0, false)]
    [InlineData(29165, 0, false)]
    public void CountsAndScoreContentsHaveDifferentCompletionThresholds(long now, long maximum, bool complete)
    {
        var state = new SchedulerState { Daily = [new SchedulerContent
        {
            Name = "콘텐츠", Type = "contents", Registration = Flag(true), Now = now, Maximum = maximum
        }] };
        Assert.Equal(complete, Assert.Single(SchedulerEntries.Create(state)).Complete);
    }

    [Theory]
    [InlineData("[길드] 주간 미션 포인트", 10, 10)]
    [InlineData("[길드] 지하 수로", 29165, 0)]
    [InlineData("[길드] 플래그 레이스", 1500, 0)]
    [InlineData("무릉도장", 80, 0)]
    public void RecordedPointsAndFloorsAreNotClearFlags(string name, long now, long maximum)
    {
        var state = new SchedulerState { Weekly = [new SchedulerContent { Name = name, Type = "contents", Registration = Flag(true), Now = now, Maximum = maximum }] };
        Assert.False(Assert.Single(SchedulerEntries.Create(state)).Complete);
    }

    [Fact]
    public void BossesFollowEntryLevelsInsteadOfApiOrder()
    {
        var state = new SchedulerState { Bosses = [
            new() { Name = "감시자 칼로스", Difficulty = "normal", Order = 58, Registration = Flag(true) },
            new() { Name = "카링", Difficulty = "easy", Order = 61, Registration = Flag(true) },
            new() { Name = "최초의 대적자", Difficulty = "normal", Order = 71, Registration = Flag(true) },
            new() { Name = "찬란한 흉성", Difficulty = "normal", Order = 76, Registration = Flag(true) }
        ] };
        Assert.Equal(["감시자 칼로스", "최초의 대적자", "카링", "찬란한 흉성"], SchedulerEntries.Create(state).Select(entry => entry.Name));
    }

    [Fact]
    public void BossesBelow200UseTheRegistrationScreenshotOrder()
    {
        string[] expected = ["자쿰", "매그너스", "파풀라투스", "피에르", "반반", "블러디 퀸", "벨룸", "스우", "데미안"];
        var state = new SchedulerState { Bosses = expected.Reverse().Select((name, index) => new SchedulerBoss
        {
            Name = name, Cycle = "bossWeekly", Order = index, Registration = Flag(true)
        }).ToList() };
        Assert.Equal(expected, SchedulerEntries.Create(state).Select(entry => entry.Name));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LevelOrderAndBellonaTieApplyToRegisteredAndUnregisteredClears(bool registered)
    {
        string[] expected = ["가디언 엔젤 슬라임", "루시드", "윌", "더스크", "진 힐라", "듄켈", "선택받은 세렌",
            "감시자 칼로스", "최초의 대적자", "카링", "찬란한 흉성", "벨로나", "림보", "발드릭스", "유피테르"];
        var state = new SchedulerState { Bosses = expected.Reverse().Select((name, index) => new SchedulerBoss
        {
            Name = name, Cycle = "bossWeekly", Order = index, Registration = Flag(registered), Complete = Flag(true)
        }).ToList() };
        var entries = SchedulerEntries.Create(state);
        Assert.Equal(expected, entries.Select(entry => entry.Name));
        Assert.All(entries, entry => Assert.Equal(registered ? SchedulerSection.Boss : SchedulerSection.UnregisteredBoss, entry.Section));
    }

    [Fact]
    public void DifficultiesOfTheSameBossRetainTheirApiOrder()
    {
        var state = new SchedulerState { Bosses = [
            new() { Name = "스우", Difficulty = "extreme", Order = 30, Registration = Flag(true) },
            new() { Name = "데미안", Difficulty = "normal", Order = 1, Registration = Flag(true) },
            new() { Name = "스우", Difficulty = "normal", Order = 10, Registration = Flag(true) },
            new() { Name = "스우", Difficulty = "hard", Order = 20, Registration = Flag(true) }
        ] };
        var entries = SchedulerEntries.Create(state);
        Assert.Equal(["스우", "스우", "스우", "데미안"], entries.Select(entry => entry.Name));
        Assert.Equal(["normal", "hard", "extreme", "normal"], entries.Select(entry => entry.Difficulty));
    }

    [Fact]
    public void MissingAndNullContentArraysAreSafe() => Assert.Empty(SchedulerEntries.Create(Parse("""{"daily_contents":null,"weekly_contents":null}""")));

    [Fact]
    public void AllViewContainsOnlyAddedCharactersAvailableToCurrentAccount()
    {
        Assert.Equal(["one", "three"], SchedulerSelection.Visible(["one", "old-account", "one", "three"], ["one", "two", "three"], null));
        Assert.Equal(["three"], SchedulerSelection.Visible(["one", "three"], ["one", "two", "three"], "three"));
        Assert.Empty(SchedulerSelection.Visible(["one"], ["one", "two"], "two"));
        Assert.Empty(SchedulerSelection.Visible(["old-account"], ["one"], null));
    }

    [Fact]
    public void ReorderingRegisteredCharactersPreservesOtherAccountsAndMembership()
    {
        var registered = new[] { "one", "other-account", "two", "three" };
        var order = SchedulerSelection.Reorder(registered, ["three", "one", "two"]);
        Assert.Equal(["three", "other-account", "one", "two"], order);
        Assert.Equal(["three", "one", "two"], SchedulerSelection.Visible(order, ["one", "two", "three"], null));
        Assert.Equal(["two"], SchedulerSelection.Visible(order, ["one", "two", "three"], "two"));
        Assert.Equal(registered.Order(), order.Order());
        Assert.Equal(registered, SchedulerSelection.Reorder(order, ["one", "two", "three"]));
    }

    [Fact]
    public void ReorderingIgnoresUnknownDuplicatesAndKeepsUnavailableRegistrations()
    {
        Assert.Equal(["two", "hidden", "one"], SchedulerSelection.Reorder(["one", "hidden", "two", "one"], ["two", "unknown", "two", "one"]));
        Assert.Equal(["one", "two"], SchedulerSelection.Reorder(["one", "two"], []));
        Assert.Equal(["one", "two"], SchedulerSelection.Reorder(["one", "two"], ["unknown"]));
        Assert.Empty(SchedulerSelection.Reorder([], ["unknown"]));
    }

    private static JsonElement Flag(bool value) => JsonSerializer.SerializeToElement(value);
    private static SchedulerState Parse(string json) => JsonSerializer.Deserialize<SchedulerState>(json)!;
}
