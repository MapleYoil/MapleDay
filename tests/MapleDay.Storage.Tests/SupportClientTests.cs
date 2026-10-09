using System.Net;
using System.Text;
using System.Text.Json;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class SupportClientTests
{
    [Fact]
    public async Task Live_changes_refresh_immediately_and_watch_stops_on_cancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Support-Stream-" + Guid.NewGuid().ToString("N"));
        using var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            using var client = new SupportClient(new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/") }, directory);
            var received = System.Threading.Channels.Channel.CreateUnbounded<bool>();
            var watch = client.WatchAsync(() => received.Writer.TryWrite(true), cancellation.Token);
            var context = await listener.GetContextAsync().WaitAsync(cancellation.Token);
            Assert.Equal("/support/events", context.Request.Url!.AbsolutePath);
            Assert.Matches("^Bearer [a-f0-9]{64}$", context.Request.Headers["Authorization"]!);
            Assert.Empty(context.Request.Url.Query);
            using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            await socket.SendAsync(Encoding.UTF8.GetBytes("ready"), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellation.Token);
            await received.Reader.ReadAsync(cancellation.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("changed"), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellation.Token);
            await received.Reader.ReadAsync(cancellation.Token);
            cancellation.Cancel(); await watch.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { cancellation.Cancel(); if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
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
    public void Own_followup_is_not_an_unread_admin_reply_or_notification()
    {
        var ticket = new SupportTicket { Id = "one", State = "received", Replies = [new() { Id = 12, Body = "내 답장", Author = "user" }] };
        ticket.RestoreReadState(true, 0);
        Assert.True(ticket.IsRead);
        Assert.DoesNotContain("답변 도착", ticket.StatusText);
        Assert.Contains("내 답장", ticket.Conversation);
        ticket.Replies.Add(new() { Id = 13, Body = "운영자 답변" });
        ticket.RestoreReadState(true, 12);
        Assert.False(ticket.IsRead);
        Assert.Equal("답변 도착", ticket.StatusText);
        ticket.RestoreReadState(true, 13);
        Assert.True(ticket.IsRead);
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
