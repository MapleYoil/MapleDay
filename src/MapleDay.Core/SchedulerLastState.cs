using System.Text.Json;

namespace MapleDay.Core;

/// <summary>A display projection only; never store this as an observed API response.</summary>
public static class SchedulerLastState
{
    public static SchedulerSnapshot? Latest(IEnumerable<SchedulerSnapshot> snapshots, DateOnly today) => snapshots
        .Where(snapshot => snapshot.Date <= today && !string.IsNullOrWhiteSpace(snapshot.State?.Name))
        .OrderBy(snapshot => snapshot.Date).ThenBy(snapshot => snapshot.FetchedAt).LastOrDefault();

    public static SchedulerState Project(SchedulerSnapshot source, DateOnly today)
    {
        var state = source.State ?? throw new ArgumentException("A saved scheduler state is required.", nameof(source));
        var dailyReset = source.Date != today;
        var weeklyReset = Reset(BossCycle.Weekly);
        return new()
        {
            Date = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Name = state.Name, World = state.World,
            Daily = Contents(state.Daily, dailyReset), Weekly = Contents(state.Weekly, weeklyReset),
            Bosses = state.Bosses?.Select(boss => new SchedulerBoss
            {
                Name = boss.Name, Difficulty = boss.Difficulty, Cycle = boss.Cycle,
                Order = boss.Order, Registration = boss.Registration,
                Complete = Reset(SchedulerBossHistory.Cycle(boss.Cycle) ?? BossCycle.Daily)
                    ? JsonSerializer.SerializeToElement(false) : boss.Complete
            }).ToList(),
            WeeklyBossClearCount = weeklyReset ? 0 : state.WeeklyBossClearCount,
            WeeklyBossClearLimit = state.WeeklyBossClearLimit ?? BossIncome.WeeklyCap
        };
        bool Reset(BossCycle cycle) => SchedulerBossHistory.Start(cycle, source.Date) != SchedulerBossHistory.Start(cycle, today);
    }

    private static List<SchedulerContent>? Contents(List<SchedulerContent>? contents, bool reset) => contents?.Select(content => new SchedulerContent
    {
        Name = content.Name, Type = content.Type, Registration = content.Registration, Maximum = content.Maximum,
        Now = reset ? 0 : content.Now, QuestState = reset && content.QuestState is not null ? "0" : content.QuestState
    }).ToList();
}
