using System.Text.Json;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class BossPartySettingsTests
{
    [Fact]
    public void PartyOverridesSurviveSerializationWithoutSharingAcrossCharactersOrWeeks()
    {
        var settings = new AppSettings { BossPartySizes = new()
        {
            ["char-a|Weekly:2026-10-01:Bosses/icon_13.png"] = 3,
            ["char-b|Weekly:2026-10-01:Bosses/icon_13.png"] = 6,
            ["char-a|Weekly:2026-10-08:Bosses/icon_13.png"] = 1
        } };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(3, restored.BossPartySizes["char-a|Weekly:2026-10-01:Bosses/icon_13.png"]);
        Assert.Equal(6, restored.BossPartySizes["char-b|Weekly:2026-10-01:Bosses/icon_13.png"]);
        Assert.Equal(1, restored.BossPartySizes["char-a|Weekly:2026-10-08:Bosses/icon_13.png"]);
    }

    [Fact]
    public void OldSettingsNeedNoMigrationAndDefaultToSolo()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("{\"StartPage\":\"scheduler\",\"SchedulerOcids\":[\"char-a\"]}")!;
        Assert.Empty(restored.BossPartySizes);
        Assert.Equal(1, restored.BossPartySizes.GetValueOrDefault("char-a|missing", 1));
        Assert.Equal("scheduler", restored.ValidStartPage);
    }
}
