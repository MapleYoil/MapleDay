using System.Net;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SundayArchiveTests
{
    private const string Body = "<div class='new_board_con'><img src='https://lwi.nexon.com/effects.png'></div></div><div id='ajaxRefresh'><img src='https://ssl.nexon.com/comments.png'>";
    private static string Entry(int id, string date, string title = "썬데이 메이플") => $"<div class='event_list_wrap'><dl><a href='/News/Event/Closed/{id}?page=1'>{title}</a><dd class='date'>{date} ~ {date}</dd></dl></div>";

    [Fact]
    public void UsesNoticeImageWithoutHiddenEditorFilesOrAnimatedOverlays()
    {
        Assert.Equal(new[] { "https://lwi.nexon.com/effects.png" }, SundayNotices.EffectImages(SundayArchive.NoticeBody(Body)
            + "<img src='https://lwi.nexon.com/hidden.png' style='width: 0.00%;'>"
            + "<img src='https://lwi.nexon.com/overlay.gif' style='position: absolute;'>"
            + "<img src='https://lwi.nexon.com/test.png' style='display: none;'>"));
    }

    [Fact]
    public void ParsesOnlySundayEntriesAndNoticeBodyImages()
    {
        var entries = SundayArchive.ParseList(Entry(1, "2026.10.04") + Entry(2, "2026.09.27", "스페셜 썬데이 메이플")
            + Entry(3, "2026.09.20", "다른 이벤트") + Entry(4, "2026.99.99"));
        Assert.Equal(new[] { 1, 2 }, entries.Select(item => item.NoticeId));
        Assert.Equal("https://maplestory.nexon.com/News/Event/Closed/1", entries[0].Url);
        Assert.Equal(new[] { "https://lwi.nexon.com/effects.png" }, ScheduleCalendar.NoticeImages(SundayArchive.NoticeBody(Body)));
        Assert.Empty(SundayArchive.NoticeBody("<img src='https://lwi.nexon.com/banner.png'>"));
    }

    [Fact]
    public void SixCalendarMonthsIsInclusiveAndRejectsFutureOrNonSunday()
    {
        var today = new DateOnly(2026, 10, 12);
        Assert.True(SundayArchive.CanFetch(new(2026, 4, 12), today));
        Assert.False(SundayArchive.CanFetch(new(2026, 4, 5), today));
        Assert.False(SundayArchive.CanFetch(new(2026, 10, 18), today));
        Assert.False(SundayArchive.CanFetch(new(2026, 10, 10), today));
    }

    [Fact]
    public async Task LoadsMatchingPastSundayAcrossPagesAndReusesDailyIndex()
    {
        var handler = new FakeHandler(); using var http = new HttpClient(handler);
        var archive = new SundayArchive(http); var today = new DateOnly(2026, 10, 10);
        var first = await archive.FindAsync(new(2026, 9, 27), today);
        Assert.Equal(2, first!.NoticeId); Assert.Single(first.Images);
        Assert.Equal(3, handler.Requests.Count);
        await archive.FindAsync(new(2026, 9, 27), today);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Null(await archive.FindAsync(new(2026, 4, 5), today));
        Assert.Equal(4, handler.Requests.Count); // Out-of-range requests never touch the network.
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri; Requests.Add(uri);
            Assert.False(request.Headers.Contains("x-nxopen-api-key"));
            var html = uri.Contains("/Closed/2") ? Body : uri.Contains("page=2") ? Entry(2, "2026.09.27") : Entry(1, "2026.10.04");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }
}
