using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace MapleDay.Core;

public static class KoreanPublicHolidays
{
    // National public holidays (관공서의 공휴일에 관한 규정, 2026-05-11), including substitute days. The annual
    // calendar supplies election days and newly announced temporary holidays.
    public static HashSet<DateOnly> ForYear(int year)
    {
        var dates = new HashSet<DateOnly>();
        var substitutes = new List<DateOnly[]>();
        void Fixed(int month, int day, bool substitute = false)
        {
            var date = new DateOnly(year, month, day); dates.Add(date);
            if (substitute) substitutes.Add([date]);
        }
        Fixed(1, 1); Fixed(3, 1, true); Fixed(5, 5, true); Fixed(6, 6);
        Fixed(8, 15, true); Fixed(10, 3, true); Fixed(10, 9, true); Fixed(12, 25, true);
        if (year >= 2026) { Fixed(5, 1); Fixed(7, 17, true); }
        var lunar = new KoreanLunisolarCalendar();
        if (year >= lunar.GetYear(lunar.MinSupportedDateTime) && year <= lunar.GetYear(lunar.MaxSupportedDateTime))
        {
            DateOnly Lunar(int month, int day)
            {
                var leap = lunar.GetLeapMonth(year);
                if (leap > 0 && month >= leap) month++;
                return DateOnly.FromDateTime(lunar.ToDateTime(year, month, day, 0, 0, 0, 0));
            }
            foreach (var center in new[] { Lunar(1, 1), Lunar(8, 15) })
            {
                var group = new[] { center.AddDays(-1), center, center.AddDays(1) };
                dates.UnionWith(group); substitutes.Add(group);
            }
            var buddha = Lunar(4, 8); dates.Add(buddha); substitutes.Add([buddha]);
        }
        foreach (var group in substitutes.OrderBy(group => group[0]))
        {
            var overlaps = group.Any(date => date.DayOfWeek == DayOfWeek.Sunday
                || group.Length == 1 && date.DayOfWeek == DayOfWeek.Saturday
                || substitutes.Any(other => !ReferenceEquals(group, other) && other.Contains(date)));
            if (!overlaps) continue;
            var replacement = group[^1].AddDays(1);
            while (replacement.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || dates.Contains(replacement)) replacement = replacement.AddDays(1);
            dates.Add(replacement);
        }
        if (year == 2026) dates.Add(new(year, 6, 3));
        return dates;
    }

    public static HashSet<DateOnly> ParseAnnualCalendar(string html, int year)
    {
        if (!html.Contains($"{year}년 달력자료", StringComparison.Ordinal)) throw new InvalidDataException("Calendar year mismatch.");
        var section = Regex.Match(html, @"국경일과 공휴일</h2>.*?<tbody>(?<rows>.*?)</tbody>", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        if (!section.Success) throw new InvalidDataException("Holiday table missing.");
        var dates = new HashSet<DateOnly>();
        foreach (Match row in Regex.Matches(section.Groups["rows"].Value, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline, TimeSpan.FromSeconds(1)))
        {
            var cells = Regex.Matches(row.Value, @"<td\b[^>]*>(.*?)</td>", RegexOptions.Singleline, TimeSpan.FromSeconds(1))
                .Select(cell => WebUtility.HtmlDecode(Regex.Replace(cell.Groups[1].Value, "<[^>]+>", "", RegexOptions.None, TimeSpan.FromSeconds(1)))).ToArray();
            for (var index = 0; index + 1 < cells.Length; index += 2)
            {
                if (cells[index].Contains("**", StringComparison.Ordinal)) continue;
                var values = Regex.Matches(cells[index + 1], @"(?<month>\d+)\s*월\s*(?<day>\d+)\s*일", RegexOptions.None, TimeSpan.FromSeconds(1));
                if (values.Count == 0) continue;
                DateOnly Read(Match value) => new(year, int.Parse(value.Groups["month"].Value), int.Parse(value.Groups["day"].Value));
                var start = Read(values[0]); var end = Read(values[^1]);
                if (end < start || end.DayNumber - start.DayNumber > 7) throw new InvalidDataException("Invalid holiday range.");
                for (var date = start; date <= end; date = date.AddDays(1)) dates.Add(date);
            }
        }
        if (dates.Count < 12) throw new InvalidDataException("Incomplete holiday calendar.");
        return dates;
    }
}

public sealed record SundayAnnouncementWindow(DateOnly Sunday, DateTimeOffset Due)
{
    public bool IsDue(DateTimeOffset now) => now >= Due && ScheduleCalendar.KoreanDate(now) == ScheduleCalendar.KoreanDate(Due);
}

public static class SundayAnnouncements
{
    public static SundayAnnouncementWindow Window(DateOnly sunday, Func<DateOnly, bool> holiday)
    {
        if (sunday.DayOfWeek != DayOfWeek.Sunday) throw new ArgumentException("Sunday required.", nameof(sunday));
        var date = sunday.AddDays(-2);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holiday(date)) date = date.AddDays(-1);
        return new(sunday, new(date.ToDateTime(new TimeOnly(10, 0)), SchedulerReminders.Korea));
    }
    public static SundayAnnouncementWindow Upcoming(DateTimeOffset now, Func<DateOnly, bool> holiday)
    {
        var date = ScheduleCalendar.KoreanDate(now);
        var sunday = date.AddDays(((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7);
        return Window(sunday, holiday);
    }
    public static bool ShouldAttempt(SundayAnnouncementWindow window, DateTimeOffset now, DateTimeOffset? retryAfter,
        bool enabled, bool sent) => enabled && !sent && window.IsDue(now) && (retryAfter is null || now >= retryAfter);
    public static SundayNotice? Published(IEnumerable<SundayNotice> notices, DateOnly sunday) => notices
        .Where(notice => notice.NoticeId > 0 && sunday >= notice.Start && sunday <= notice.End && notice.Images.Length > 0)
        .OrderByDescending(notice => notice.NoticeId).FirstOrDefault();
}
