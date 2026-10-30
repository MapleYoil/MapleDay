namespace MapleDay.Core;

public static class SchedulerRefreshIndicator
{
    public static readonly TimeSpan RefreshAge = TimeSpan.FromHours(2);

    public static bool IsDue(IEnumerable<DateTimeOffset?> refreshedAt, DateTimeOffset now)
    {
        var timestamps = refreshedAt.ToArray();
        return timestamps.Length > 0
            && timestamps.Any(timestamp => timestamp is null || now - timestamp.Value >= RefreshAge);
    }
}
