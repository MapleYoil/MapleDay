using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public class HuntingSettingsTests
{
    [Fact]
    public void Legacy_settings_default_to_no_hunting_and_saved_rates_survive_round_trip()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Empty(legacy.HuntingIncomeRecords); Assert.Equal(0, legacy.HuntingMesoBonus); Assert.Equal(1, legacy.HuntingFragmentPriceMan);
        var record = new HuntingIncomeRecord("record", "owner", new(2026, 10, 9), 420000000, 20, 1230000, 100, 100, 291);
        var settings = new AppSettings { HuntingIncomeRecords = [record], HuntingMesoBonus = 100, HuntingFragmentPriceMan = 123 };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(record, Assert.Single(restored.HuntingIncomeRecords));
        restored.HuntingFragmentPriceMan = 9999;
        Assert.Equal(444600000, restored.HuntingIncomeRecords[0].Total);
    }
}
