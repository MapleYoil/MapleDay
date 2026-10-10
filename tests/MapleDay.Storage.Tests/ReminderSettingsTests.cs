using System.Text.Json;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class ReminderSettingsTests
{
    [Fact]
    public void AnnouncementNotificationsDefaultOffAndChoicesCheckpointsRoundTrip()
    {
        var defaults = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.False(defaults.UpdateNotifications); Assert.False(defaults.SundayNotifications);
        var settings = new AppSettings { SundayNotifications = true, UpdateNotifications = true, NotifiedUpdateVersion = "1.2.0.0",
            CheckedReminders = new() { ["sunday:2026-10-11"] = DateTimeOffset.UtcNow },
            ReminderNotices = [new() { AnnouncementTitle = "썬데이 메이플", AnnouncementText = "혜택 공개", AnnouncementUrl = "https://maplestory.nexon.com/News/Event/1397" }],
            PublicHolidayDates = new() { [2026] = [new(2026, 10, 9)] } };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(restored.UpdateNotifications); Assert.True(restored.SundayNotifications);
        Assert.Equal("1.2.0.0", restored.NotifiedUpdateVersion);
        Assert.True(restored.CheckedReminders.ContainsKey("sunday:2026-10-11"));
        Assert.Equal("썬데이 메이플", Assert.Single(restored.ReminderNotices).Title);
        Assert.Equal("혜택 공개", restored.ReminderNotices[0].Summary);
        Assert.Empty(restored.ReminderNotices[0].Characters);
        Assert.Equal(new DateOnly(2026, 10, 9), Assert.Single(restored.PublicHolidayDates[2026]));
    }
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
