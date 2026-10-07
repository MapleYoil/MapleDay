namespace MapleDay.Core;

public enum ReminderKind { DailyQuest, WeeklyQuest, WeeklyBoss }
public sealed class ReminderOptions
{
    public bool Enabled { get; set; } = true;
    public int HoursBefore { get; set; } = 2;
}
public sealed record ReminderWindow(ReminderKind Kind, DateTimeOffset Due, DateTimeOffset Reset)
{
    public bool IsDue(DateTimeOffset now) => now >= Due && now < Reset;
    public string PeriodKey => $"{Kind}:{Reset:yyyy-MM-dd}";
}
public sealed class ReminderCharacter
{
    public string Ocid { get; set; } = "";
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public IReadOnlyList<string> Pending { get; set; } = [];
    public ReminderCharacter() { }
    public ReminderCharacter(string ocid, string name, string world, IReadOnlyList<string> pending)
    { Ocid = ocid; Name = name; World = world; Pending = pending; }
}
public sealed class ReminderNotice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ReminderKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset Reset { get; set; }
    public List<ReminderCharacter> Characters { get; set; } = [];
    public bool Read { get; set; }
    public bool IsStartup { get; set; }
    public string CreatedText => CreatedAt.ToOffset(SchedulerReminders.Korea).ToString("yyyy-MM-dd HH:mm");
    public string ReadLabel => Read ? "읽음" : "읽음으로 표시";
    public string Title => IsStartup ? $"앱 시작 · {SchedulerReminders.Label(Kind)} 미완료 알림" : $"{SchedulerReminders.Label(Kind)} 초기화 알림";
    public string Summary => $"{Characters.Count}명 · 미완료 {Characters.Sum(character => character.Pending.Count)}개 · {Reset.ToOffset(SchedulerReminders.Korea):MM-dd HH:mm} 초기화";
    public string Details => string.Join("\n", Characters.Select(character => $"{character.Name} ({character.World}) · {string.Join(", ", character.Pending)}"));
}

public static class SchedulerReminders
{
    public static readonly TimeSpan Korea = TimeSpan.FromHours(9);
    public static int MaximumHours(ReminderKind kind) => kind == ReminderKind.DailyQuest ? 23 : 167;
    public static bool ShouldCheck(ReminderWindow window, DateTimeOffset now, bool periodChecked, bool startupPending) =>
        startupPending || window.IsDue(now) && !periodChecked;
    public static bool HasData(ReminderKind kind, SchedulerState state) => kind switch
    {
        ReminderKind.DailyQuest => state.Daily is not null,
        ReminderKind.WeeklyQuest => state.Weekly is not null,
        _ => state.Bosses is not null
    };
    public static string Label(ReminderKind kind) => kind switch
    {
        ReminderKind.DailyQuest => "일일 퀘스트", ReminderKind.WeeklyQuest => "주간 퀘스트", _ => "주간 보스"
    };
    public static ReminderWindow Window(ReminderKind kind, DateTimeOffset now, int hoursBefore)
    {
        var korean = now.ToOffset(Korea);
        var date = DateOnly.FromDateTime(korean.DateTime);
        var resetDate = kind == ReminderKind.DailyQuest ? date.AddDays(1)
            : SchedulerBossHistory.Start(BossCycle.Weekly, date).AddDays(7);
        var reset = new DateTimeOffset(resetDate.ToDateTime(TimeOnly.MinValue), Korea);
        return new(kind, reset.AddHours(-Math.Clamp(hoursBefore, 1, MaximumHours(kind))), reset);
    }
    public static DateTimeOffset NextDue(ReminderKind kind, DateTimeOffset now, int hoursBefore)
    {
        var window = Window(kind, now, hoursBefore);
        return window.Due > now ? window.Due : window.Due.AddDays(kind == ReminderKind.DailyQuest ? 1 : 7);
    }
    public static bool IsExtremeMonsterParkQuest(string name) => name.Contains("익스트림 몬스터파커", StringComparison.Ordinal);
    public static bool ExtremeMonsterParkComplete(SchedulerState? state) => state?.Weekly?.Any(content =>
        content.Name is { } name && IsExtremeMonsterParkQuest(name) && content.QuestState == "2") == true;
    public static bool ExtremeMonsterParkCompletedThisWeek(IEnumerable<SchedulerSnapshot> snapshots, DateOnly today, string world)
    {
        var start = SchedulerBossHistory.Start(BossCycle.Weekly, today);
        return snapshots.Any(snapshot => snapshot.Date >= start && snapshot.Date <= today
            && snapshot.State?.World == world
            && (snapshot.ExtremeMonsterParkCompleted || ExtremeMonsterParkComplete(snapshot.State)));
    }

    public static IReadOnlyList<string> Pending(ReminderKind kind, SchedulerState state, int level, bool includeUnionQuests = false,
        bool extremeMonsterParkCompleted = false)
    {
        if (kind == ReminderKind.WeeklyBoss && state.WeeklyBossClearCount >= BossIncome.WeeklyCap) return [];
        return SchedulerEntries.Create(state).Where(entry => !entry.Complete && !SchedulerRequirements.IsLevelBlocked(entry, level)
            && (includeUnionQuests || entry.Type != "quest" || !entry.Name.Contains("메이플 유니온", StringComparison.Ordinal))
            && (!extremeMonsterParkCompleted || kind != ReminderKind.WeeklyQuest || !IsExtremeMonsterParkQuest(entry.Name))
            && (kind switch
            {
                ReminderKind.DailyQuest => entry.Section == SchedulerSection.Daily && entry.Type == "quest",
                ReminderKind.WeeklyQuest => entry.Section == SchedulerSection.Weekly && entry.Type == "quest",
                _ => entry.Section == SchedulerSection.Boss && SchedulerBossHistory.Cycle(entry.Cycle) == BossCycle.Weekly
            })).Select(entry => kind == ReminderKind.WeeklyBoss ? $"{entry.Name} ({entry.Difficulty})" : entry.Name)
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}
