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
        Assert.True(settings.UpdateNotifications);
        Assert.Equal(["saved"], settings.SchedulerOcids);
        Assert.Equal("level", settings.StartPage);
    }

    [Fact]
    public void DisabledAutomaticUpdatesSurviveReload()
    {
        var settings = new AppSettings { AutomaticUpdates = false };
        Assert.False(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.AutomaticUpdates);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NotificationChoicePersistsIndependentlyFromAutomaticDownloads(bool automatic, bool notify)
    {
        var settings = new AppSettings { AutomaticUpdates = automatic, UpdateNotifications = notify, SchedulerOcids = ["saved"] };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(automatic, restored.AutomaticUpdates);
        Assert.Equal(notify, restored.UpdateNotifications);
        Assert.Equal(["saved"], restored.SchedulerOcids);
    }
}
