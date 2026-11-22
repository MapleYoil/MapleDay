namespace MapleDay.Core;

public sealed record IncomeWeekRange(DateOnly Start, DateOnly End, DateOnly Anchor, int WeekOfMonth)
{
    public string Label => $"{Anchor:yyyy년 M월} {WeekOfMonth}주차 · {Start:MM.dd}~{End:MM.dd}";
}

public static class IncomeHistory
{
    public static IncomeWeekRange Week(DateOnly date)
    {
        var distance = ((int)date.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
        var start = date.AddDays(-distance);
        var anchor = start.AddDays(3);
        return new(start, start.AddDays(6), anchor, (anchor.Day - 1) / 7 + 1);
    }

    public static IReadOnlyList<IncomeWeekRange> Month(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var result = new List<IncomeWeekRange>();
        for (var week = Week(first); week.Anchor <= last; week = Week(week.Start.AddDays(7)))
            if (week.Anchor.Month == first.Month && week.Anchor.Year == first.Year) result.Add(week);
        return result;
    }
}

public sealed record IncomeExpenseRecord(string Id, DateOnly Date, long Amount, string Category, string Memo = "", string Ocid = "");

public static class IncomeExpenses
{
    public const long MaximumAmount = 1_000_000_000_000_000;
    public static IReadOnlyList<string> Categories { get; } = ["장비 구매", "강화", "소모품", "거래·수수료", "기타"];

    public static bool Valid(IncomeExpenseRecord? record) => record is not null
        && !string.IsNullOrWhiteSpace(record.Id) && record.Id.Length <= 100
        && record.Date >= new DateOnly(2003, 4, 29)
        && record.Amount is > 0 and <= MaximumAmount
        && Categories.Contains(record.Category)
        && record.Memo.Length <= 200 && record.Ocid.Length <= 200;

    public static long Sum(IEnumerable<IncomeExpenseRecord> records, DateOnly start, DateOnly end)
        => records.Where(Valid).Where(record => record.Date >= start && record.Date <= end)
            .DistinctBy(record => record.Id).Sum(record => record.Amount);
}
