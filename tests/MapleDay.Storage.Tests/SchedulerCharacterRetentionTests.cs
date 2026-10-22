using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class SchedulerCharacterRetentionTests
{
    [Fact]
    public void MissingCharacterKeepsRegistrationAndSavedIdentity()
    {
        var saved = new SavedSchedulerCharacter("old", "캐릭터", "오로라", "아델", 290);
        var settings = new AppSettings { SchedulerOcids = ["old"], SchedulerCharacters = [saved] };

        var migrations = SchedulerCharacterRetention.Reconcile(settings,
            [new CharacterSummary { Ocid = "other", Name = "다른캐릭터", World = "루나" }]);

        Assert.Empty(migrations);
        Assert.Equal("old", Assert.Single(settings.SchedulerOcids));
        Assert.Equal(saved, Assert.Single(settings.SchedulerCharacters));
    }

    [Fact]
    public void SameNicknameOnNewOcidMovesEveryCharacterReference()
    {
        var members = new[] { new PartyMember("one", "캐릭터", "old"), new PartyMember("two", "파티원") };
        var party = new PartyPreset("party", "파티", members, "one", []);
        var settlement = new LootSettlement(members, "one", "one");
        var clear = new ManualWeeklyClear("old", "스우", "hard", new(2026, 10, 22));
        var settings = new AppSettings
        {
            SchedulerOcids = ["old"],
            SchedulerCharacters = [new("old", "캐릭터", "오로라", "아델", 290)],
            ManualWeeklyClears = [clear],
            BossClearChanges = [new("old", "original", clear)],
            BossLootRecords = [new BossLootRecord("loot", "old", new(2026, 10, 22), "스우", "", "아이템", "settlement", 100, 2)
                { Settlement = settlement }],
            HuntingIncomeRecords = [new("hunt", "old", new(2026, 10, 22), 100, 1, 10)],
            CalendarSchedules = [new CalendarSchedule("schedule", ScheduleKind.Boss, "일정", new(2026, 10, 22),
                Ocid: "old", CharacterName: "캐릭터", Boss: "스우", Difficulty: "hard", PartySize: 2) { Party = party }],
            Parties = [party],
            SharedPartyLinks = [new("party", "one", "old")],
            BossPartySizes = new() { ["old|스우|hard"] = 2 }
        };
        var current = new CharacterSummary { Ocid = "new", Name = "캐릭터", World = "루나", Class = "아델", Level = 291 };

        var migration = Assert.Single(SchedulerCharacterRetention.Reconcile(settings, [current]));

        Assert.Equal(new SchedulerCharacterMigration("old", "new"), migration);
        Assert.Equal("new", Assert.Single(settings.SchedulerOcids));
        Assert.Equal("new", Assert.Single(settings.ManualWeeklyClears).Ocid);
        var change = Assert.Single(settings.BossClearChanges);
        Assert.Equal("new", change.Ocid); Assert.Equal("new", change.Replacement!.Ocid);
        var loot = Assert.Single(settings.BossLootRecords);
        Assert.Equal("new", loot.Ocid); Assert.Equal("new", loot.Settlement!.Members[0].Ocid);
        Assert.Equal("new", Assert.Single(settings.HuntingIncomeRecords).Ocid);
        var schedule = Assert.Single(settings.CalendarSchedules);
        Assert.Equal("new", schedule.Ocid); Assert.Equal("new", schedule.Party!.Members[0].Ocid);
        Assert.Equal("new", Assert.Single(settings.Parties).Members[0].Ocid);
        Assert.Equal("new", Assert.Single(settings.SharedPartyLinks).LocalOcid);
        Assert.Equal(2, settings.BossPartySizes["new|스우|hard"]);
        var identity = Assert.Single(settings.SchedulerCharacters);
        Assert.Equal(("new", "루나", 291), (identity.Ocid, identity.World, identity.Level));
    }

    [Fact]
    public async Task HistoryCopyKeepsOldBackupAndUsesNewestSnapshotPerDate()
    {
        var root = Path.Combine(Path.GetTempPath(), "MapleDay-Retention-Test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SchedulerHistoryStore(root);
            var date = new DateOnly(2026, 10, 22);
            var older = new SchedulerSnapshot(date, new SchedulerState { Name = "이전" }, true, DateTimeOffset.Parse("2026-10-22T01:00:00Z"));
            var newer = new SchedulerSnapshot(date, new SchedulerState { Name = "최신" }, true, DateTimeOffset.Parse("2026-10-22T02:00:00Z"));
            await store.SaveAsync("key", "new", older, default);
            await store.SaveAsync("key", "old", newer, default);

            await store.CopyAsync("key", "old", "new", default);

            Assert.Equal("최신", Assert.Single(await store.LoadAsync("key", "new", default)).State!.Name);
            Assert.Equal("최신", Assert.Single(await store.LoadAsync("key", "old", default)).State!.Name);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void SyncRoundTripIncludesSavedCharacterIdentity()
    {
        var source = new AppSettings
        {
            SchedulerOcids = ["ocid"],
            SchedulerCharacters = [new("ocid", "캐릭터", "오로라", "아델", 290)]
        };

        var restored = new AppSettings();
        SyncRecords.Apply(restored, SyncRecords.Capture(source));

        Assert.Equal(source.SchedulerCharacters, restored.SchedulerCharacters);
    }
}
