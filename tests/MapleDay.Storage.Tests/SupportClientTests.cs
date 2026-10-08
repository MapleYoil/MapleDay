using System.Net;
using System.Text;
using System.Text.Json;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class SupportClientTests
{
    [Theory]
    [InlineData("implemented", "반영 완료", "반영됐어요")]
    [InlineData("rejected", "반려", "반려됐어요")]
    public void SuggestionResultUsesItsStatusAndReadCursor(string state, string label, string title)
    {
        var json = $$"""{"id":"suggestion","kind":"suggestion","state":"{{state}}","completed":1800000000,"body":"건의 내용","replies":[{"id":42,"body":"처리 결과","created":1800000000}]}""";
        var ticket = JsonSerializer.Deserialize<SupportTicket>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(label, ticket.StatusText);
        Assert.Contains(title, ticket.NotificationTitle);
        Assert.Contains("건의사항", ticket.Conversation);
        Assert.Contains("처리 결과", ticket.Conversation);
        ticket.RestoreReadState(true, 41);
        Assert.False(ticket.IsRead);
        ticket.RestoreReadState(true, 42);
        Assert.True(ticket.IsRead);
    }

    [Fact]
    public void OldInquiryPayloadRemainsCompatibleAndPendingSuggestionsShowReceived()
    {
        var old = JsonSerializer.Deserialize<SupportTicket>("{\"state\":\"received\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("inquiry", old.Kind);
        Assert.Equal("답변 대기", old.StatusText);
        Assert.Equal("메요일 · 문의 답변이 도착했어요", old.NotificationTitle);
        Assert.Equal("접수", new SupportTicket { Kind = "suggestion", State = "received" }.StatusText);
    }

    [Fact]
    public void ReadTicketRemainsReadAfterRefreshButANewReplyBecomesUnread()
    {
        var ticket = new SupportTicket { Id = "one", State = "sent" };
        Assert.False(ticket.IsRead);
        ticket.RestoreReadState(true, 0);
        Assert.True(ticket.IsRead);
        Assert.Contains("읽음", ticket.StatusText);
        ticket.Replies.Add(new() { Id = 12, Body = "답변" });
        ticket.RestoreReadState(true, 0);
        Assert.False(ticket.IsRead);
        Assert.Equal("답변 도착", ticket.StatusText);
        ticket.RestoreReadState(true, 12);
        Assert.True(ticket.IsRead);
        Assert.Equal("답변 읽음", ticket.StatusText);
        ticket.Replies.Add(new() { Id = 13, Body = "추가 답변" });
        ticket.RestoreReadState(true, 12);
        Assert.False(ticket.IsRead);
    }

    [Theory]
    [InlineData("scheduler", "scheduler")]
    [InlineData("income", "income")]
    [InlineData("support", "support")]
    [InlineData("unknown-page", "characters")]
    public void StartPageSurvivesSettingsSerializationAndInvalidPagesFallBack(string selected, string expected)
    {
        var saved = JsonSerializer.Serialize(new AppSettings { StartPage = selected });
        Assert.Equal(expected, JsonSerializer.Deserialize<AppSettings>(saved)!.ValidStartPage);
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        public List<string> Ids { get; } = [];
        public List<string?> Tokens { get; } = [];
        public bool FailNext { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Tokens.Add(request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Get) return Json("{\"tickets\":[]}");
            var raw = await request.Content!.ReadAsStringAsync(token);
            var draft = JsonSerializer.Deserialize<SupportDraft>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Ids.Add(draft.Id);
            Assert.Equal("선택한캐릭터", draft.Nickname);
            Assert.DoesNotContain("live_", raw);
            if (FailNext)
            {
                FailNext = false;
                return Json("{\"error\":\"일시적인 오류\"}", HttpStatusCode.ServiceUnavailable);
            }
            return Json(JsonSerializer.Serialize(new { id = draft.Id, state = "queued" }));
        }
        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task LostResponseCanBeRetriedAfterRestartWithoutDuplicatingTicket()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Support-Test-" + Guid.NewGuid().ToString("N"));
        var server = new FakeServer { FailNext = true };
        HttpClient Http() => new(server, disposeHandler: false) { BaseAddress = new Uri("https://test.invalid/") };
        try
        {
            using (var first = new SupportClient(Http(), directory))
                await Assert.ThrowsAsync<HttpRequestException>(() => first.SubmitAsync("inquiry", "선택한캐릭터", "제목", "내용", CancellationToken.None));
            using (var restarted = new SupportClient(Http(), directory))
            {
                var draft = await restarted.PendingDraftAsync();
                Assert.NotNull(draft);
                await restarted.SubmitAsync("inquiry", "선택한캐릭터", "제목", "내용", CancellationToken.None);
                Assert.Null(await restarted.PendingDraftAsync());
            }
            Assert.Equal(server.Ids[0], server.Ids[1]);
            Assert.Equal(server.Tokens[0], server.Tokens[1]);
            Assert.DoesNotContain(server.Tokens[0]!, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "support-client.dat"))));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
