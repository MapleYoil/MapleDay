using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.Win32;

namespace MapleDay.Storage.Tests;

public sealed class WindowsStartupTests
{
    [Fact]
    public void UnpackagedTestHostDoesNotUseTheMSIXStartupTask()
    {
        Assert.False(WindowsStartup.IsPackaged);
    }

    [Fact]
    public void DefaultIsEnabledAndDisabledPreferenceSurvivesRestart()
    {
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{}")!.AutoStartWindows);
        var saved = JsonSerializer.Serialize(new AppSettings { AutoStartWindows = false });
        Assert.False(JsonSerializer.Deserialize<AppSettings>(saved)!.AutoStartWindows);
    }

    [Fact]
    public void ExistingSettingsDefaultToTrayAndWindowPreferencesSurviveRestart()
    {
        var existing = JsonSerializer.Deserialize<AppSettings>("{\"CloseToTray\":false}")!;
        Assert.True(existing.WindowsStartupToTray);
        Assert.False(existing.CloseToTray);
        Assert.Equal(1200, existing.ValidWindowWidth);
        Assert.Equal(800, existing.ValidWindowHeight);
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings
        { WindowsStartupToTray = false, WindowWidth = 1536, WindowHeight = 960 }))!;
        Assert.False(restored.WindowsStartupToTray);
        Assert.Equal(1536, restored.ValidWindowWidth);
        Assert.Equal(960, restored.ValidWindowHeight);
    }

    [Fact]
    public void InvalidSavedWindowDimensionsFallBackIndependently()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"WindowWidth\":-1,\"WindowHeight\":960}")!;
        Assert.Equal(1200, settings.ValidWindowWidth);
        Assert.Equal(960, settings.ValidWindowHeight);
        settings.WindowWidth = 420;
        settings.WindowHeight = int.MaxValue;
        Assert.Equal(420, settings.ValidWindowWidth);
        Assert.Equal(800, settings.ValidWindowHeight);
    }

    [Fact]
    public void OnlyStartupTaskOrExactStartupArgumentIsAutomatic()
    {
        Assert.True(WindowsStartup.IsStartupLaunch(true, []));
        Assert.True(WindowsStartup.IsStartupLaunch(false, ["--startup"]));
        Assert.False(WindowsStartup.IsStartupLaunch(false, []));
        Assert.False(WindowsStartup.IsStartupLaunch(false, ["----AppNotificationActivated:"]));
        Assert.False(WindowsStartup.IsStartupLaunch(false, ["C:\\MapleDay--startup.exe", "--startup-other"]));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    public void StartupTrayPolicyDoesNotHideDirectLaunchOrAnAppWithoutATray(bool automatic, bool preference, bool available, bool hidden)
        => Assert.Equal(hidden, WindowsStartup.ShouldStartInTray(automatic, preference, available));

    [Fact]
    public void RegisterQuotedOuterLauncherAndRemoveOnlyOurValue()
    {
        var registryPath = @"Software\MapleDay.Tests\" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay Startup " + Guid.NewGuid().ToString("N"));
        var launcher = Path.Combine(directory, "MapleDay.exe");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "App"));
            File.WriteAllText(launcher, "test fixture; never execute");
            Assert.Equal(launcher, WindowsStartup.FindLauncher(Path.Combine(directory, "App")));
            Assert.Null(WindowsStartup.FindLauncher(directory));
            using (var testKey = Registry.CurrentUser.CreateSubKey(registryPath))
            {
                testKey.SetValue("OtherApp", "keep");
                testKey.SetValue("MapleDay", $"\"{launcher}\""); // Migrate the old unmarked startup command.
            }
            var startup = new WindowsStartup(registryPath);
            startup.Enable(launcher);
            using (var testKey = Registry.CurrentUser.OpenSubKey(registryPath)) Assert.Equal($"\"{launcher}\" --startup", testKey!.GetValue("MapleDay"));
            startup.Disable(); startup.Disable();
            using (var testKey = Registry.CurrentUser.OpenSubKey(registryPath))
            {
                Assert.Null(testKey!.GetValue("MapleDay"));
                Assert.Equal("keep", testKey.GetValue("OtherApp"));
            }
            Assert.Throws<FileNotFoundException>(() => startup.Enable(Path.Combine(directory, "missing.exe")));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReminderPreferencesAndDeduplicationSurviveSerialization()
    {
        var reset = DateTimeOffset.Parse("2026-10-08T00:00:00+09:00");
        var settings = new AppSettings
        {
            StartPage = "notifications", RemindersEnabled = false, RemindOnStartup = false,
            WeeklyBossReminder = new() { Enabled = false, HoursBefore = 8 },
            CheckedReminders = new() { ["account|WeeklyBoss:2026-10-08|ocid"] = reset },
            ReminderNotices = [new() { Id = "notice", Kind = ReminderKind.WeeklyBoss, Read = true, IsStartup = true, Reset = reset,
                Characters = [new("ocid", "캐릭터", "루나", ["스우 (hard)"])] }]
        };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("notifications", restored.ValidStartPage);
        Assert.False(restored.RemindersEnabled);
        Assert.False(restored.RemindOnStartup);
        Assert.False(restored.WeeklyBossReminder.Enabled);
        Assert.Equal(8, restored.WeeklyBossReminder.HoursBefore);
        Assert.True(restored.WeeklyBossReminder.RestrictToDay);
        Assert.Equal(DayOfWeek.Wednesday, restored.WeeklyBossReminder.Day);
        Assert.Equal(reset, restored.CheckedReminders.Single().Value);
        Assert.True(restored.ReminderNotices[0].Read);
        Assert.True(restored.ReminderNotices[0].IsStartup);
        Assert.Contains("앱 시작", restored.ReminderNotices[0].Title);
        Assert.Contains("스우", restored.ReminderNotices[0].Details);
    }

    [Fact]
    public void ExistingSettingsDefaultToStartupRemindersOffWithoutLosingKindPreferences()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("{\"WeeklyBossReminder\":{\"Enabled\":false,\"HoursBefore\":8}}")!;
        Assert.False(restored.RemindOnStartup);
        Assert.False(restored.WeeklyBossReminder.Enabled);
        Assert.Equal(8, restored.WeeklyBossReminder.HoursBefore);
        Assert.True(restored.WeeklyBossReminder.RestrictToDay);
        Assert.Equal(DayOfWeek.Wednesday, restored.WeeklyBossReminder.Day);
    }

    [Theory]
    [InlineData(true, DayOfWeek.Friday)]
    [InlineData(false, DayOfWeek.Wednesday)]
    public void WeeklyBossDayPreferencesSurviveSerialization(bool restricted, DayOfWeek day)
    {
        var settings = new AppSettings { WeeklyBossReminder = new() { RestrictToDay = restricted, Day = day, HoursBefore = 2 } };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(restricted, restored.WeeklyBossReminder.RestrictToDay);
        Assert.Equal(day, restored.WeeklyBossReminder.Day);
        Assert.Equal(2, restored.WeeklyBossReminder.HoursBefore);
        Assert.True(new AppSettings().WeeklyBossReminder.RestrictToDay);
    }

    [Fact]
    public void ExplicitStartupReminderPreferenceIsPreserved()
    {
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{\"RemindOnStartup\":true}")!.RemindOnStartup);
        Assert.False(new AppSettings().RemindOnStartup);
    }

    [Fact]
    public void ExistingWeeklyQuestPreferencesDefaultToWednesdayAndKindsRemainIndependent()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"WeeklyQuestReminder\":{\"Enabled\":false,\"HoursBefore\":8},\"WeeklyBossReminder\":{\"RestrictToDay\":false,\"Day\":5}}")!;
        Assert.True(settings.WeeklyQuestReminder.RestrictToDay);
        Assert.Equal(DayOfWeek.Wednesday, settings.WeeklyQuestReminder.Day);
        Assert.False(settings.WeeklyQuestReminder.Enabled);
        Assert.Equal(8, settings.WeeklyQuestReminder.HoursBefore);
        settings.WeeklyQuestReminder.Day = DayOfWeek.Monday;
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(restored.WeeklyQuestReminder.RestrictToDay);
        Assert.Equal(DayOfWeek.Monday, restored.WeeklyQuestReminder.Day);
        Assert.False(restored.WeeklyBossReminder.RestrictToDay);
        Assert.Equal(DayOfWeek.Friday, restored.WeeklyBossReminder.Day);
    }
}
