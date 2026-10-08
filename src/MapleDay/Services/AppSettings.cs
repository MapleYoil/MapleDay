using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed class AppSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool DarkMode { get; set; }
    public bool AutoStartWindows { get; set; } = true;
    public bool WindowsStartupToTray { get; set; } = true;
    public bool AutomaticUpdates { get; set; } = true;
    public bool UsageAnalyticsEnabled { get; set; } = true;
    public bool AnonymousUsageAnalytics { get; set; }
    public bool AutomaticErrorReports { get; set; } = true;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public int ValidWindowWidth => WindowWidth is >= 420 and <= 10000 ? WindowWidth : 1200;
    public int ValidWindowHeight => WindowHeight is >= 520 and <= 10000 ? WindowHeight : 800;
    public long NotifiedReplyId { get; set; }
    public Dictionary<string, long> ReadReplies { get; set; } = [];
    public HashSet<string> ReadTickets { get; set; } = [];
    public List<string> SchedulerOcids { get; set; } = [];
    public Dictionary<string, int> BossPartySizes { get; set; } = [];
    public bool RemindersEnabled { get; set; } = true;
    public bool RemindOnStartup { get; set; }
    public bool UnionQuestReminderEnabled { get; set; }
    public ReminderOptions DailyQuestReminder { get; set; } = new();
    public ReminderOptions WeeklyQuestReminder { get; set; } = new();
    public ReminderOptions WeeklyBossReminder { get; set; } = new();
    public Dictionary<string, DateTimeOffset> CheckedReminders { get; set; } = [];
    public List<ReminderNotice> ReminderNotices { get; set; } = [];
    public string StartPage { get; set; } = "characters";
    public string ValidStartPage => StartPage is "characters" or "scheduler" or "income" or "level" or "support" or "notifications" or "settings" or "key" ? StartPage : "characters";
    public bool MarkRemindersRead()
    {
        var changed = false;
        foreach (var notice in ReminderNotices.Where(notice => !notice.Read)) { notice.Read = true; changed = true; }
        return changed;
    }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
