using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MapleDay.Core;

public enum ScheduleKind { General, Boss }
public enum ScheduleFilter { All, Boss, General }
public sealed record CalendarSchedule(string Id, ScheduleKind Kind, string Title, DateOnly Date, TimeOnly? Time = null,
    string Note = "", string Ocid = "", string CharacterName = "", string Boss = "", string Difficulty = "", bool Weekly = false,
    DateOnly? RepeatUntil = null, int PartySize = 1)
{
    public HashSet<DateOnly> CompletedDates { get; init; } = [];
}
public sealed record EventNotice
{
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("notice_id")] public int NoticeId { get; init; }
    [JsonPropertyName("date_event_start")] public DateTimeOffset Start { get; init; }
    [JsonPropertyName("date_event_end")] public DateTimeOffset End { get; init; }
    [JsonPropertyName("contents")] public string Contents { get; init; } = "";
}
public sealed record EventNoticeResponse
{
    [JsonPropertyName("event_notice")] public List<EventNotice> Notices { get; init; } = [];
}
public sealed record SundayNotice(int NoticeId, DateOnly Start, DateOnly End, string Url, string[] Images);
public sealed record ScheduleOccurrence(DateOnly Date, CalendarSchedule? Schedule = null, SundayNotice? Sunday = null)
{
    public bool IsSunday => Schedule is null;
    public bool Completed => Schedule?.CompletedDates.Contains(Date) == true;
    public string Title => Schedule?.Title ?? "썬데이 메이플";
    public string DisplayName => Schedule is { Kind: ScheduleKind.Boss, CharacterName.Length: > 0 } item ? item.CharacterName : Title;
    public string AccessibleName => Schedule is { Kind: ScheduleKind.Boss } item
        ? $"{DisplayName}, {BossLootCatalog.DifficultyLabel(item.Difficulty)} {item.Boss}, {item.PartySize}인{(Completed ? ", 완료" : "")}" : Title;
}
public sealed record ScheduleCalendarDay(DateOnly Date, bool InMonth, IReadOnlyList<ScheduleOccurrence> Items);

public static class ScheduleCalendar
{
    public static bool Valid(CalendarSchedule item) => !string.IsNullOrWhiteSpace(item.Id)
        && !string.IsNullOrWhiteSpace(item.Title) && item.Title.Length <= 100 && item.Note.Length <= 2000
        && Enum.IsDefined(item.Kind) && item.Date.Year >= 2003 && (item.RepeatUntil is null || item.RepeatUntil >= item.Date)
        && (item.Kind != ScheduleKind.Boss || !string.IsNullOrWhiteSpace(item.Boss) && !string.IsNullOrWhiteSpace(item.Difficulty)
            && item.PartySize >= 1 && item.PartySize <= BossParty.Maximum(item.Boss));

    public static IReadOnlyList<ScheduleCalendarDay> Month(DateOnly month, IEnumerable<CalendarSchedule> schedules,
        IEnumerable<SundayNotice> notices, ScheduleFilter filter = ScheduleFilter.All, bool includeSunday = true)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);
        var entries = schedules.Where(Valid).DistinctBy(item => item.Id).Where(item => filter == ScheduleFilter.All
            || item.Kind == (filter == ScheduleFilter.Boss ? ScheduleKind.Boss : ScheduleKind.General)).ToArray();
        var sundays = notices.Where(item => item.NoticeId > 0 && item.End >= item.Start).ToArray();
        return Enumerable.Range(0, 42).Select(index =>
        {
            var date = start.AddDays(index);
            var items = entries.Where(item => item.Date == date || item.Weekly && date >= item.Date
                && (item.RepeatUntil is null || date <= item.RepeatUntil) && (date.DayNumber - item.Date.DayNumber) % 7 == 0)
                .OrderBy(item => item.Time ?? TimeOnly.MinValue).ThenBy(item => item.Title, StringComparer.Ordinal)
                .Select(item => new ScheduleOccurrence(date, item)).ToList();
            if (includeSunday && filter != ScheduleFilter.Boss && date.DayOfWeek == DayOfWeek.Sunday)
                items.Insert(0, new(date, Sunday: sundays.Where(item => date >= item.Start && date <= item.End)
                    .OrderByDescending(item => item.NoticeId).FirstOrDefault()));
            return new ScheduleCalendarDay(date, date.Month == first.Month && date.Year == first.Year, items);
        }).ToArray();
    }

    public static DateOnly KoreanDate(DateTimeOffset date) => DateOnly.FromDateTime(date.ToOffset(TimeSpan.FromHours(9)).DateTime);
    public static bool NoticeRefreshDue(DateTimeOffset? last, DateTimeOffset now)
    {
        if (last is null || now < last || now - last >= TimeSpan.FromHours(1)) return true;
        var korea = now.ToOffset(TimeSpan.FromHours(9));
        var release = new DateTimeOffset(korea.Year, korea.Month, korea.Day, 10, 0, 0, TimeSpan.FromHours(9));
        return korea.DayOfWeek == DayOfWeek.Friday && now >= release && last < release;
    }

    public static bool OfficialUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Host == "nexon.com" || uri.Host.EndsWith(".nexon.com", StringComparison.OrdinalIgnoreCase));

    // Render image URLs only. Notice markup is never executed in the app.
    public static string[] NoticeImages(string contents)
    {
        return Regex.Matches(contents, @"<img\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            .Select(tag => Regex.Match(tag.Value, "(?:^|\\s)src\\s*=\\s*(?:\"(?<url>[^\"]*)\"|'(?<url>[^']*)'|(?<url>[^\\s>]+))",
                RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            .Where(match => match.Success).Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value.Trim()))
            .Select(url => url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url)
            .Where(OfficialUri).Distinct(StringComparer.Ordinal).Take(20).ToArray();
    }
}
