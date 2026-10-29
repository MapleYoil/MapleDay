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

    public static SchedulerSnapshot? BestRoster(IEnumerable<SchedulerSnapshot> snapshots, DateOnly today) => snapshots
        .Where(snapshot => snapshot.Date < today && !string.IsNullOrWhiteSpace(snapshot.State?.Name))
        .OrderByDescending(snapshot => RosterSize(snapshot.State!)).ThenByDescending(snapshot => snapshot.Date)
        .ThenByDescending(snapshot => snapshot.FetchedAt).FirstOrDefault();

    public static SchedulerState? FillMissingRoster(SchedulerState current, SchedulerSnapshot source, DateOnly today)
    {
        var projected = Project(source, today);
        if (!LooksPartial(current, projected)) return null;
        return new()
        {
            Date = current.Date, Name = current.Name, World = current.World,
            Daily = Merge(current.Daily, projected.Daily, content => $"{content.Name}\n{content.Type}"),
            Weekly = Merge(current.Weekly, projected.Weekly, content => $"{content.Name}\n{content.Type}"),
            Bosses = Merge(current.Bosses, projected.Bosses,
                boss => $"{boss.Name}\n{boss.Difficulty}\n{boss.Cycle}"),
            WeeklyBossClearCount = current.WeeklyBossClearCount ?? projected.WeeklyBossClearCount,
            WeeklyBossClearLimit = current.WeeklyBossClearLimit ?? projected.WeeklyBossClearLimit
        };
    }

    private static bool LooksPartial(SchedulerState current, SchedulerState source)
    {
        var currentCounts = new[] { current.Daily?.Count ?? 0, current.Weekly?.Count ?? 0, current.Bosses?.Count ?? 0 };
        var sourceCounts = new[] { source.Daily?.Count ?? 0, source.Weekly?.Count ?? 0, source.Bosses?.Count ?? 0 };
        return currentCounts.Zip(sourceCounts).Any(pair => pair.Second - pair.First >= 3 && pair.First * 2 < pair.Second)
            || sourceCounts.Sum() - currentCounts.Sum() >= 10 && currentCounts.Sum() * 2 < sourceCounts.Sum();
    }

    private static int RosterSize(SchedulerState state) => (state.Daily?.Count ?? 0) + (state.Weekly?.Count ?? 0) + (state.Bosses?.Count ?? 0);

    private static List<T>? Merge<T>(List<T>? current, List<T>? source, Func<T, string> key)
    {
        if (current is null && source is null) return null;
        var currentByKey = (current ?? []).ToDictionary(key, StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        var result = (source ?? []).Select(item =>
        {
            var itemKey = key(item);
            known.Add(itemKey);
            return currentByKey.GetValueOrDefault(itemKey) ?? item;
        }).ToList();
        result.AddRange((current ?? []).Where(item => known.Add(key(item))));
        return result;
    }

    private static List<SchedulerContent>? Contents(List<SchedulerContent>? contents, bool reset) => contents?.Select(content => new SchedulerContent
    {
        Name = content.Name, Type = content.Type, Registration = content.Registration, Maximum = content.Maximum,
        Now = reset ? 0 : content.Now, QuestState = reset && content.QuestState is not null ? "0" : content.QuestState
    }).ToList();
}
