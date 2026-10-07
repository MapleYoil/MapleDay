using System.Text.Json;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class UpdateSettingsTests
{
    [Fact]
    public void ExistingSettingsEnableUpdatesWithoutChangingSavedCharacters()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"SchedulerOcids":["saved"],"StartPage":"level"}""")!;
        Assert.True(settings.AutomaticUpdates);
        Assert.Equal(["saved"], settings.SchedulerOcids);
        Assert.Equal("level", settings.StartPage);
    }

    [Fact]
    public void DisabledAutomaticUpdatesSurviveReload()
    {
        var settings = new AppSettings { AutomaticUpdates = false };
        Assert.False(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.AutomaticUpdates);
    }
}
