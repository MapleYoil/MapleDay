using System.Globalization;

namespace MapleDay.Core;

public static class SchedulerNumberFormat
{
    public static string Count(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}
