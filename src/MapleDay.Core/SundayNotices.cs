using System.Text.RegularExpressions;

namespace MapleDay.Core;

public static class SundayNotices
{
    public static string[] EffectImages(string contents)
    {
        // Embedded animations and hidden editor images are overlays, not notices.
        var tags = Regex.Matches(contents, @"<img\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            .Where(tag => !Regex.IsMatch(tag.Value, @"position\s*:\s*absolute|display\s*:\s*none|width\s*:\s*0(?:[.][0]+)?(?:%|px)",
                RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        return ScheduleCalendar.NoticeImages(string.Join("", tags.Select(tag => tag.Value)));
    }
    public static async Task<IReadOnlyList<SundayNotice>> RefreshAsync(NexonApiClient api, string apiKey,
        IEnumerable<SundayNotice> stored, DateTimeOffset now, CancellationToken token = default)
    {
        var list = await api.GetEventNoticesAsync(apiKey, token).ConfigureAwait(false);
        var result = stored.GroupBy(item => item.NoticeId).ToDictionary(group => group.Key, group => group.First());
        foreach (var notice in list.Notices.Where(item => item.Title.Contains("썬데이 메이플", StringComparison.Ordinal)
            && item.NoticeId > 0 && item.Start != default && item.End >= item.Start))
        {
            var detail = await api.GetEventNoticeAsync(notice.NoticeId, apiKey, token).ConfigureAwait(false);
            var images = EffectImages(detail.Contents);
            if (images.Length == 0 && result.TryGetValue(notice.NoticeId, out var existing)) images = existing.Images;
            result[notice.NoticeId] = new(notice.NoticeId, ScheduleCalendar.KoreanDate(notice.Start),
                ScheduleCalendar.KoreanDate(notice.End), ScheduleCalendar.OfficialUri(detail.Url) ? detail.Url : notice.Url, images);
        }
        var earliest = ScheduleCalendar.KoreanDate(now).AddMonths(-6);
        return result.Values.Where(item => item.End >= earliest).OrderBy(item => item.Start).TakeLast(260).ToArray();
    }
}
