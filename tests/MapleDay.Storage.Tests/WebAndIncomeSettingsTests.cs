using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class WebAndIncomeSettingsTests
{
    [Fact]
    public void Single_monthly_clear_and_associated_loot_preserve_links_and_legacy_defaults()
    {
        var clear = new ManualWeeklyClear("c", "검은 마법사", "hard", new(2026, 10, 1), BossCycle.Monthly);
        var loot = new BossLootRecord("a", "c", new(2026, 10, 9), "검은 마법사", "", "정산", "received", 123, 2, Difficulty: "hard", ClearId: ManualWeeklyHistory.Id(clear));
        var settings = new AppSettings { ManualWeeklyClears = [clear], BossLootRecords = [loot] };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(clear, Assert.Single(loaded.ManualWeeklyClears));
        Assert.Equal(loot, Assert.Single(loaded.BossLootRecords));
        var oldClear = JsonSerializer.Deserialize<ManualWeeklyClear>("{\"Ocid\":\"c\",\"Name\":\"스우\",\"Difficulty\":\"hard\",\"Date\":\"2026-10-01\"}")!;
        Assert.Equal(BossCycle.Weekly, oldClear.Cycle);
        var oldLoot = JsonSerializer.Deserialize<BossLootRecord>("{\"Id\":\"a\",\"Ocid\":\"c\",\"Date\":\"2026-10-01\",\"Boss\":\"스우\",\"ItemId\":\"\",\"ItemName\":\"정산\",\"Mode\":\"received\",\"Amount\":1,\"PartySize\":1}")!;
        Assert.True(string.IsNullOrEmpty(oldLoot.ClearId));
    }
    [Fact]
    public void Older_settings_keep_safe_web_and_currency_defaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(17831, settings.WebPort); Assert.Empty(settings.WebPasswordHash);
        Assert.False(settings.WebHttps); Assert.Equal("meso", settings.IncomeDisplayMode); Assert.Equal(1500, settings.MesoCashRate);
        Assert.Empty(settings.ManualWeeklyClears);
        Assert.Empty(settings.BossLootRecords);
    }
    [Fact]
    public void LootSettlementModesAndDatesRoundTripWithoutLosingOwnership()
    {
        var original = new AppSettings { BossLootRecords = [new("a", "c", new(2026, 1, 1), "스우", "100", "물욕템", "ratio", 100, 3, "2:1:1", 2, "hard"),
            new("b", "d", new(2026, 10, 9), "벨로나", "", "직접 입력", "received", 999, 3),
            new("c", "c", new(2026, 10, 8), "스우", "", "물욕템", "equal", 100, 3)] };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original))!;
        Assert.Equal(original.BossLootRecords, loaded.BossLootRecords);
        Assert.Equal(new long[] { 25, 999, 33 }, loaded.BossLootRecords.Select(record => record.Received));
    }
    [Fact]
    public void Manually_added_clears_round_trip_independently_of_api_snapshots()
    {
        var clear = new ManualWeeklyClear("c", "스우", "hard", new(2026, 10, 1));
        var settings = new AppSettings { ManualWeeklyClears = [clear], BossPartySizes = new() { [ManualWeeklyHistory.Key(clear)] = 2 } };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(clear, Assert.Single(loaded.ManualWeeklyClears));
        Assert.Equal(2, loaded.BossPartySizes[ManualWeeklyHistory.Key(clear)]);
    }
    [Fact]
    public void Web_hash_and_currency_preferences_round_trip_without_plaintext_password()
    {
        var settings = new AppSettings { WebPasswordSalt = "salt", WebPasswordHash = "hash", WebPort = 18000, WebHttps = true,
            WebPublicHost = "maple.example", IncomeDisplayMode = "both", MesoCashRate = 1700 };
        var json = JsonSerializer.Serialize(settings); var loaded = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal("both", loaded.IncomeDisplayMode); Assert.Equal(1700, loaded.MesoCashRate); Assert.Equal(18000, loaded.WebPort);
        Assert.DoesNotContain("CertificatePassword", json); Assert.Equal(1700, new IncomeDisplay(loaded.IncomeDisplayMode, loaded.MesoCashRate).Cash(100_000_000));
    }
}
