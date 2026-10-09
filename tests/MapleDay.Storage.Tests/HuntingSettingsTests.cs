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
    [Fact]
    public void Absolute_limit_usage_round_trips_and_legacy_percent_remains_readable()
    {
        var record = new HuntingIncomeRecord("record", "owner", new(2026, 10, 9), 874000000, 20, 5990000, 280, null, 300, 230000000);
        var restored = JsonSerializer.Deserialize<HuntingIncomeRecord>(JsonSerializer.Serialize(record))!;
        Assert.Equal(record, restored); Assert.Equal(230000000, HuntingIncome.LimitUsage(restored));
        var legacy = JsonSerializer.Serialize(record with { LimitPercent = 100, LimitMeso = null });
        using var parsed = JsonDocument.Parse(legacy);
        var fields = parsed.RootElement.EnumerateObject().Where(field => field.Name != "LimitMeso").ToDictionary(field => field.Name, field => field.Value.Clone());
        restored = JsonSerializer.Deserialize<HuntingIncomeRecord>(JsonSerializer.Serialize(fields))!;
        Assert.Null(restored.LimitMeso); Assert.Equal(230000000, HuntingIncome.LimitUsage(restored)); Assert.Equal(874000000, restored.Meso);
    }

}
