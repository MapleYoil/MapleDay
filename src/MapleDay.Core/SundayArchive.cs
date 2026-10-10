using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace MapleDay.Core;

public sealed class SundayArchive(HttpClient http)
{
    private const string Root = "https://maplestory.nexon.com";
    private readonly Dictionary<int, (DateOnly Day, SundayNotice[] Items)> _pages = [];
    public void ClearIndex() => _pages.Clear();
    public static bool CanFetch(DateOnly date, DateOnly today) => date.DayOfWeek == DayOfWeek.Sunday
        && date >= today.AddMonths(-6) && date <= today;

    public async Task<SundayNotice?> FindAsync(DateOnly date, DateOnly today, CancellationToken token = default)
    {
        if (!CanFetch(date, today)) return null;
        // Search only Sunday notices; six months normally fits into three pages.
        for (var page = 1; page <= 12; page++)
        {
            if (!_pages.TryGetValue(page, out var cached) || cached.Day != today)
            {
                var html = await http.GetStringAsync(Root + "/News/Event/Closed?search=" + Uri.EscapeDataString("썬데이") + "&page=" + page, token).ConfigureAwait(false);
                cached = (today, ParseList(html)); _pages[page] = cached;
            }
            var match = cached.Items.Where(item => date >= item.Start && date <= item.End).OrderByDescending(item => item.NoticeId).FirstOrDefault();
            if (match is not null)
            {
                var html = await http.GetStringAsync(match.Url, token).ConfigureAwait(false);
                var images = SundayNotices.EffectImages(NoticeBody(html));
                return match with { Images = images };
            }
            if (cached.Items.Length == 0 || cached.Items.Min(item => item.Start) < date) return null;
        }
        return null;
    }

    public static SundayNotice[] ParseList(string html)
    {
        var result = new List<SundayNotice>();
        foreach (Match block in Regex.Matches(html, "<div\\s+class=[\"']event_list_wrap[\"'][^>]*>(?<body>.*?)</dl>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var body = block.Groups["body"].Value;
            var text = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(1)));
            if (!Regex.IsMatch(text, "썬데이\\s*메이플", RegexOptions.None, TimeSpan.FromSeconds(1))) continue;
            var id = Regex.Match(body, @"/News/Event/Closed/(?<id>\d+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            var dates = Regex.Matches(text, @"\d{4}\.\d{2}\.\d{2}", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!id.Success || !int.TryParse(id.Groups["id"].Value, out var value) || dates.Count != 2
                || !DateOnly.TryParseExact(dates[0].Value, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !DateOnly.TryParseExact(dates[1].Value, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end < start) continue;
            result.Add(new(value, start, end, Root + "/News/Event/Closed/" + value, []));
        }
        return result.DistinctBy(item => item.NoticeId).ToArray();
    }

    public static string NoticeBody(string html)
    {
        var start = Regex.Match(html, "<div\\s+class=[\"']new_board_con[\"'][^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (!start.Success) return "";
        var end = Regex.Match(html[(start.Index + start.Length)..], "<div\\s+id=[\"']ajaxRefresh[\"']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return end.Success ? html.Substring(start.Index + start.Length, end.Index) : "";
    }
}
