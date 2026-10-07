using System.Text.Json;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class ReminderSettingsTests
{
    [Fact]
    public void NewAndExistingSettingsDefaultUnionQuestRemindersOff()
    {
        Assert.False(new AppSettings().UnionQuestReminderEnabled);
        var existing = JsonSerializer.Deserialize<AppSettings>("""{"RemindersEnabled":true,"DailyQuestReminder":{"Enabled":true,"HoursBefore":4},"SchedulerOcids":["saved-character"]}""")!;
        Assert.False(existing.UnionQuestReminderEnabled);
        Assert.True(existing.RemindersEnabled);
        Assert.True(existing.DailyQuestReminder.Enabled);
        Assert.Equal(4, existing.DailyQuestReminder.HoursBefore);
        Assert.Equal(["saved-character"], existing.SchedulerOcids);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitUnionPreferenceSurvivesSavingAndReloading(bool enabled)
    {
        var settings = new AppSettings { UnionQuestReminderEnabled = enabled };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(enabled, restored.UnionQuestReminderEnabled);
    }
}
