using System.Net;
using System.Text;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class ScheduleCalendarTests
{
    [Theory]
    [InlineData(ScheduleFilter.All)]
    [InlineData(ScheduleFilter.General)]
    [InlineData(ScheduleFilter.Boss)]
    public void HideSundayRemovesPublishedAndPlaceholderEventsWhileKeepingPersonalPlans(ScheduleFilter filter)
    {
        var date = new DateOnly(2026, 10, 11);
        CalendarSchedule[] plans = [new("personal", ScheduleKind.General, "썬데이 친구 약속", date),
            new("boss", ScheduleKind.Boss, "보스 약속", date, Boss: "스우", Difficulty: "hard")];
        var notice = new SundayNotice(1397, date, date, "https://maplestory.nexon.com/News/Event/1397", ["image"]);
        var hidden = ScheduleCalendar.Month(date, plans, [notice], filter, includeSunday: false);
        Assert.Equal(42, hidden.Count);
        Assert.DoesNotContain(hidden.SelectMany(day => day.Items), item => item.IsSunday);
        var visible = hidden.Single(day => day.Date == date).Items;
        Assert.Equal(filter == ScheduleFilter.All ? 2 : 1, visible.Count);
        Assert.Contains(visible, item => item.Schedule?.Id == (filter == ScheduleFilter.Boss ? "boss" : "personal"));
        var shown = ScheduleCalendar.Month(date, plans, [notice], filter);
        Assert.Equal(filter != ScheduleFilter.Boss, shown.SelectMany(day => day.Items).Any(item => item.IsSunday));
    }
    [Fact]
    public void EverySundayHasAPlaceholderAndPublishedNoticesReplaceOnlyTheirEventDates()
    {
        var notice = new SundayNotice(1397, new(2026, 10, 11), new(2026, 10, 11), "https://maplestory.nexon.com/News/Event/1397", ["https://lwi.nexon.com/event.png"]);
        var month = ScheduleCalendar.Month(new(2026, 10, 1), [], [notice]);
        Assert.Equal(42, month.Count);
        Assert.Equal(DayOfWeek.Sunday, month[0].Date.DayOfWeek);
        Assert.Equal(31, month.Count(day => day.InMonth));
        foreach (var day in month)
        {
            if (day.Date.DayOfWeek != DayOfWeek.Sunday) { Assert.Empty(day.Items); continue; }
            var sunday = Assert.Single(day.Items);
            Assert.True(sunday.IsSunday);
            Assert.Equal("썬데이 메이플", sunday.Title);
            Assert.Equal(day.Date == notice.Start ? notice : null, sunday.Sunday);
        }
    }
    [Theory]
    [InlineData(ScheduleFilter.All, 3)]
    [InlineData(ScheduleFilter.Boss, 1)]
    [InlineData(ScheduleFilter.General, 2)]
    public void FiltersKeepBossAndPersonalPlansSeparate(ScheduleFilter filter, int count)
    {
        var date = new DateOnly(2026, 10, 11);
        CalendarSchedule[] records = [new("general", ScheduleKind.General, "약속", date),
            new("boss", ScheduleKind.Boss, "보스 파티", date, Boss: "스우", Difficulty: "hard")];
        var items = ScheduleCalendar.Month(date, records, [], filter).Single(day => day.Date == date).Items;
        Assert.Equal(count, items.Count);
        Assert.DoesNotContain(items, item => filter == ScheduleFilter.Boss && item.Schedule?.Kind != ScheduleKind.Boss);
        Assert.DoesNotContain(items, item => filter == ScheduleFilter.General && item.Schedule?.Kind == ScheduleKind.Boss);
    }
    [Fact]
    public void WeeklyPlansRespectStartAndEndAndCompletionBelongsToOneOccurrence()
    {
        var start = new DateOnly(2026, 10, 1); var next = start.AddDays(7);
        var plan = new CalendarSchedule("weekly", ScheduleKind.Boss, "주간 보스", start, new(21, 30), Boss: "림보", Difficulty: "normal",
            Weekly: true, RepeatUntil: start.AddDays(14)) { CompletedDates = [next] };
        var items = ScheduleCalendar.Month(start, [plan, plan], [], ScheduleFilter.Boss).SelectMany(day => day.Items).ToArray();
        Assert.Equal(new[] { start, next, start.AddDays(14) }, items.Select(item => item.Date));
        Assert.Equal(new[] { false, true, false }, items.Select(item => item.Completed));
    }
    [Fact]
    public void LeapMonthAndTimeOrderingDoNotMoveDates()
    {
        var date = new DateOnly(2028, 2, 29);
        CalendarSchedule[] plans = [new("late", ScheduleKind.General, "밤", date, new(23, 59)),
            new("early", ScheduleKind.General, "아침", date, new(10, 0)), new("day", ScheduleKind.General, "종일", date)];
        var month = ScheduleCalendar.Month(date, plans, [], ScheduleFilter.General);
        Assert.Equal(29, month.Count(day => day.InMonth));
        Assert.Equal(new[] { "종일", "아침", "밤" }, month.Single(day => day.Date == date).Items.Select(item => item.Title));
    }
    [Fact]
    public void InvalidPlansDoNotAppearAndRoundTripPreservesCompletionAndRecurrence()
    {
        var date = new DateOnly(2026, 10, 10);
        var good = new CalendarSchedule("id", ScheduleKind.General, "일정", date, Weekly: true, RepeatUntil: date.AddMonths(1))
            { CompletedDates = [date.AddDays(7)] };
        Assert.Equal(good.CompletedDates, JsonSerializer.Deserialize<CalendarSchedule>(JsonSerializer.Serialize(good))!.CompletedDates);
        Assert.True(ScheduleCalendar.Valid(good));
        CalendarSchedule[] invalid = [good with { Id = "" }, good with { Title = " " }, good with { RepeatUntil = date.AddDays(-1) },
            good with { Kind = ScheduleKind.Boss }, good with { Note = new string('a', 2001) }];
        Assert.All(invalid, item => Assert.False(ScheduleCalendar.Valid(item)));
        Assert.Empty(ScheduleCalendar.Month(date, invalid, [], ScheduleFilter.Boss).SelectMany(day => day.Items));
    }

    [Fact]
    public void BossScheduleUsesCharacterAndPartySizeWhileItsTitleAndCompletionArePreserved()
    {
        var date = new DateOnly(2026, 10, 10);
        var plan = new CalendarSchedule("id", ScheduleKind.Boss, "친구와 림보 약속", date,
            CharacterName: "초코마린", Boss: "림보", Difficulty: "normal", PartySize: 3)
            { CompletedDates = [date] };
        var item = Assert.Single(ScheduleCalendar.Month(date, [plan], [], ScheduleFilter.Boss).Single(day => day.Date == date).Items);
        Assert.Equal("초코마린", item.DisplayName);
        Assert.Equal("친구와 림보 약속", item.Title);
        Assert.Equal("초코마린, 노멀 림보, 3인, 완료", item.AccessibleName);
        Assert.True(ScheduleCalendar.Valid(plan));
        Assert.False(ScheduleCalendar.Valid(plan with { PartySize = 4 }));
        Assert.False(ScheduleCalendar.Valid(plan with { PartySize = 0 }));
        Assert.Equal("친구와 림보 약속", (item with { Schedule = plan with { CharacterName = "" } }).DisplayName);
        Assert.True(ScheduleCalendar.Valid(plan with { Boss = "스우", PartySize = 6 }));
    }
    [Fact]
    public void ImageExtractionDecodesUrlsAndIgnoresUnsafeMarkup()
    {
        var html = """
            <body><script>malicious()</script><img src="https://lwi.nexon.com/a.png?x=1&amp;y=2" onerror="malicious()">
            <IMG SRC='//file.nexon.com/b.png'><img data-src='https://lwi.nexon.com/skip.png'>
            <img src=https://lwi.nexon.com/c.png><img src='javascript:malicious()'><img src='http://lwi.nexon.com/d.png'>
            <img src='https://nexon.com.evil.invalid/a.png'><img src='https://user@nexon.com/a.png'>
            <img src='//file.nexon.com/b.png'></body>
            """;
        Assert.Equal(new[] { "https://lwi.nexon.com/a.png?x=1&y=2", "https://file.nexon.com/b.png", "https://lwi.nexon.com/c.png" }, ScheduleCalendar.NoticeImages(html));
    }
    [Fact]
    public void ChecksEveryHourForEarlyAnnouncementsAndAtFridayTenInKoreanTime()
    {
        var ten = new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.FromHours(9));
        Assert.True(ScheduleCalendar.NoticeRefreshDue(ten.AddMinutes(-20), ten));
        Assert.False(ScheduleCalendar.NoticeRefreshDue(ten.AddMinutes(-20), ten.AddMinutes(-1)));
        Assert.False(ScheduleCalendar.NoticeRefreshDue(ten, ten.AddMinutes(59)));
        Assert.True(ScheduleCalendar.NoticeRefreshDue(ten, ten.AddHours(1)));
        Assert.True(ScheduleCalendar.NoticeRefreshDue(ten.AddDays(-1).AddHours(-1), ten.AddDays(-1)));
        Assert.True(ScheduleCalendar.NoticeRefreshDue(null, ten));
        Assert.True(ScheduleCalendar.NoticeRefreshDue(ten.AddHours(1), ten));
        Assert.Equal(new DateOnly(2026, 10, 9), ScheduleCalendar.KoreanDate(new(2026, 10, 8, 15, 0, 0, TimeSpan.Zero)));
    }
    [Fact]
    public async Task NoticeApiUsesKeyOnlyInHeadersAndLoadsFullImageInsteadOfThumbnail()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-nxopen-api-key")));
            Assert.DoesNotContain("test-key", request.RequestUri.AbsoluteUri);
            return paths.Count == 1 ? """
                {"event_notice":[{"notice_id":1397,"title":"썬데이 메이플","url":"https://maplestory.nexon.com/News/Event/1397",
                 "thumbnail_url":"https://file.nexon.com/thumb.png","date_event_start":"2026-10-11T00:00+09:00","date_event_end":"2026-10-11T23:59+09:00"},
                 {"notice_id":1393,"title":"일반 이벤트","date_event_start":"2026-10-01T10:00+09:00","date_event_end":"2026-10-14T23:59+09:00"}]}
                """ : JsonSerializer.Serialize(new { title = "썬데이 메이플", url = "https://maplestory.nexon.com/News/Event/1397",
                    contents = "<img src='https://lwi.nexon.com/full.png'>" });
        }));
        using var api = new NexonApiClient(http);
        var old = new SundayNotice(1390, new(2026, 10, 4), new(2026, 10, 4), "https://maplestory.nexon.com/News/Event/1390", []);
        var result = await SundayNotices.RefreshAsync(api, "test-key", [old], new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new[] { "/maplestory/v1/notice-event", "/maplestory/v1/notice-event/detail?notice_id=1397" }, paths);
        Assert.Equal(2, result.Count);
        Assert.Contains(old, result);
        Assert.Equal(new DateOnly(2026, 10, 11), result[1].Start);
        Assert.Equal("https://lwi.nexon.com/full.png", Assert.Single(result[1].Images));
        Assert.Empty(http.DefaultRequestHeaders);
    }
    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
