namespace MapleDay.Core;

public sealed record HuntingIncomeRecord(string Id, string Ocid, DateOnly Date, long Meso,
    int Fragments, long FragmentUnitPrice, decimal MesoBonus = 0, decimal? LimitPercent = null, int Level = 0)
{
    public long Total => checked(Meso + Fragments * FragmentUnitPrice);
}

public static class HuntingIncome
{
    public const long MaximumMeso = 1_000_000_000_000_000;
    public static long DailyLimit(int level) => level switch
    {
        < 1 or > 300 => throw new ArgumentOutOfRangeException(nameof(level)),
        < 100 => 20_000_000,
        < 200 => 40_000_000,
        < 260 => 80_000_000 + (level - 200) / 5 * 5_000_000L,
        _ => 150_000_000 + (level - 260) / 5 * 10_000_000L
    };
    public static long FromLimit(int level, decimal percent, decimal bonus)
    {
        if (percent is < 0 or > 100 || bonus is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(percent));
        return (long)decimal.Floor(DailyLimit(level) * percent / 100 * (1 + bonus / 100));
    }
    public static long FragmentPrice(int tenThousands)
        => tenThousands is >= 1 and <= 9999 ? tenThousands * 10_000L : throw new ArgumentOutOfRangeException(nameof(tenThousands));
    public static bool Valid(HuntingIncomeRecord record) => !string.IsNullOrWhiteSpace(record.Id)
        && !string.IsNullOrWhiteSpace(record.Ocid) && record.Meso is >= 0 and <= MaximumMeso
        && record.Fragments is >= 0 and <= 1_000_000 && record.FragmentUnitPrice is >= 10_000 and <= 99_990_000
        && record.FragmentUnitPrice % 10_000 == 0 && record.MesoBonus is >= 0 and <= 10000
        && (record.LimitPercent is null || record.LimitPercent is >= 0 and <= 100)
        && (record.Meso > 0 || record.Fragments > 0);
    public static long Sum(IEnumerable<HuntingIncomeRecord> records, DateOnly start, DateOnly end)
        => records.Where(Valid).Where(record => record.Date >= start && record.Date <= end).DistinctBy(record => record.Id).Sum(record => record.Total);
    public static IncomeReplay Replay(IEnumerable<HuntingIncomeRecord> records, Func<string, string> characterName)
        => new(records.Where(Valid).DistinctBy(record => record.Id).Select(record => new ReplayClear(record.Date,
            characterName(record.Ocid), record.Total, record.Fragments == 0 ? [] :
                [new ReplayLoot("솔 에르다 조각", record.Fragments * record.FragmentUnitPrice, "Hunting/fragment.png", record.Fragments)])))
        { Category = "hunting" };
}
