using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class CalendarSettingsTests
{
    [Fact]
    public void CalendarSettingsRoundTripAndOldSettingsLoadWithEmptyLists()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"StartPage\":\"income\"}")!;
        Assert.Empty(legacy.CalendarSchedules); Assert.Empty(legacy.SundayNotices); Assert.Null(legacy.EventNoticesCheckedAt);
        Assert.False(legacy.CalendarHideSunday);
        var date = new DateOnly(2026, 10, 10);
        var settings = new AppSettings { StartPage = "calendar", CalendarHideSunday = true, EventNoticesCheckedAt = DateTimeOffset.UtcNow,
            CalendarSchedules = [new("id", ScheduleKind.Boss, "보스 약속", date, new(20, 0), "메모", "ocid", "캐릭터", "스우", "hard", true, PartySize: 3)
                { CompletedDates = [date] }],
            SundayNotices = [new(1397, date.AddDays(1), date.AddDays(1), "https://maplestory.nexon.com/News/Event/1397", ["https://lwi.nexon.com/full.png"])] };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("calendar", restored.ValidStartPage);
        Assert.True(restored.CalendarHideSunday);
        var plan = Assert.Single(restored.CalendarSchedules);
        Assert.True(plan.Weekly); Assert.Contains(date, plan.CompletedDates); Assert.Equal(new TimeOnly(20, 0), plan.Time);
        Assert.Equal("메모", plan.Note); Assert.Equal("ocid", plan.Ocid);
        Assert.Equal(3, plan.PartySize);
        var oldPlan = JsonSerializer.Deserialize<CalendarSchedule>("{\"Id\":\"old\",\"Kind\":1,\"Title\":\"기존\",\"Date\":\"2026-10-10\",\"Boss\":\"스우\",\"Difficulty\":\"normal\"}")!;
        Assert.Equal(1, oldPlan.PartySize); Assert.True(ScheduleCalendar.Valid(oldPlan));
        Assert.Equal(settings.EventNoticesCheckedAt, restored.EventNoticesCheckedAt);
        Assert.Equal("https://lwi.nexon.com/full.png", Assert.Single(Assert.Single(restored.SundayNotices).Images));
    }
}
