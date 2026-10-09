namespace MapleDay.Core;

public sealed record ManualWeeklyClear(string Ocid, string Name, string Difficulty, DateOnly Date, BossCycle Cycle = BossCycle.Weekly);

public static class ManualWeeklyHistory
{
    public static IReadOnlyList<DateOnly> Dates(DateOnly start, DateOnly end, DateOnly today, DayOfWeek? weekday = null)
    {
        if (start < SchedulerBossHistory.FirstDate || end > today || start > end)
            throw new ArgumentException("2026.06.25부터 오늘까지의 기간을 선택하세요.");
        var day = weekday ?? DayOfWeek.Thursday;
        if ((int)day is < 0 or > 6) throw new ArgumentException("요일을 확인하세요.");
        var first = start.AddDays(((int)day - (int)start.DayOfWeek + 7) % 7);
        var dates = new List<DateOnly>();
        for (var date = first; date <= end; date = date.AddDays(7)) dates.Add(date);
        return dates;
    }
    public static string Id(ManualWeeklyClear clear) => BossParty.RecordId(clear.Name, clear.Cycle, clear.Date);
    public static string Key(ManualWeeklyClear clear) => clear.Ocid + "|" + Id(clear);
    public static BossCycle CycleFor(string name) => SchedulerBossHistory.BossKey(name) == SchedulerBossHistory.BossKey("검은 마법사") ? BossCycle.Monthly : BossCycle.Weekly;
    public static IReadOnlyList<CrystalPrice> SingleChoices { get; } = CrystalPrices.All
        .GroupBy(price => (Name: SchedulerBossHistory.BossKey(price.Name), price.Difficulty))
        .Select(group => group.MaxBy(price => price.EffectiveFrom)!).ToArray();
    public static IReadOnlyList<CrystalPrice> Choices { get; } = SingleChoices.Where(price => CycleFor(price.Name) == BossCycle.Weekly).ToArray();
    public static bool Valid(ManualWeeklyClear clear) => !string.IsNullOrWhiteSpace(clear.Ocid)
        && !string.IsNullOrWhiteSpace(clear.Name) && clear.Cycle == CycleFor(clear.Name)
        && SingleChoices.Any(choice => SchedulerBossHistory.BossKey(choice.Name) == SchedulerBossHistory.BossKey(clear.Name)
            && choice.Difficulty == CrystalPrices.DifficultyKey(clear.Difficulty));
}
