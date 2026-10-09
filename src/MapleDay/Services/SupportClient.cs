using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Text;

namespace MapleDay.Services;

public sealed class SupportReply
{
    public long Id { get; set; }
    public string Body { get; set; } = "";
    public long Created { get; set; }
    public string Author { get; set; } = "admin";
    [JsonIgnore] public bool IsAdmin => Author != "user";
}
public sealed class SupportTicket : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string State { get; set; } = "";
    public string Kind { get; set; } = "inquiry";
    public long? Completed { get; set; }
    public long Created { get; set; }
    public List<SupportReply> Replies { get; set; } = [];
    public string Summary => $"{Nickname} · {DateTimeOffset.FromUnixTimeSeconds(Created).ToLocalTime():MM.dd HH:mm} · {(Kind == "suggestion" ? "건의사항" : "1:1 문의")}";
    [JsonIgnore] public bool IsRead { get; private set; }
    public string StatusText => Kind == "suggestion" ? State switch
    {
        "implemented" => "반영 완료", "rejected" => "반려", _ => Completed is not null ? "처리 완료" : "접수"
    } : Completed is not null ? "처리 완료" : Replies.Any(reply => reply.IsAdmin) ? IsRead ? "답변 읽음" : "답변 도착" : (State switch
    {
        "queued" or "sending" => "발송 대기", "sent" or "received" => "답변 대기", _ => "발송 확인 필요"
    }) + (IsRead ? " · 읽음" : "");
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RestoreReadState(bool opened, long readReplyId)
    {
        var read = opened && !Replies.Any(reply => reply.IsAdmin && reply.Id > readReplyId);
        if (IsRead == read) return;
        IsRead = read;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    public string NotificationTitle => Kind == "suggestion" ? State switch
    {
        "implemented" => "메요일 · 건의사항이 반영됐어요", "rejected" => "메요일 · 건의사항이 반려됐어요", _ => "메요일 · 건의사항 처리 결과가 도착했어요"
    } : "메요일 · 문의 답변이 도착했어요";
    public string Conversation => $"[{Nickname} · {(Kind == "suggestion" ? "건의사항" : "문의")}]\n{Body}" + string.Concat(Replies.Select(reply =>
        $"\n\n[{(!reply.IsAdmin ? "내 답장" : Kind == "suggestion" ? "처리 결과" : "운영자 답변")} · {DateTimeOffset.FromUnixTimeSeconds(reply.Created).ToLocalTime():yyyy.MM.dd HH:mm}]\n{reply.Body}"));
}
public sealed record SupportInbox(List<SupportTicket> Tickets);
public sealed record SupportReceipt(string Id, string State);
public sealed record SupportDraft(string Id, string Kind, string Nickname, string Subject, string Body);

public sealed class SupportClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly ApiKeyStore _identity;
    private readonly ApiKeyStore _draft;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private string? _token;

    public SupportClient(HttpClient? http = null, string? storageDirectory = null)
    {
        _http = http ?? new() { BaseAddress = new Uri("https://server.morialuluka.com/api/mapleday/"), Timeout = TimeSpan.FromSeconds(30) };
        var directory = storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay");
        _identity = new(Path.Combine(directory, "support-client.dat"));
        _draft = new(Path.Combine(directory, "support-draft.dat"));
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        await _initialization.WaitAsync(token);
        try
        {
            if (_token is not null) return;
            var stored = await _identity.LoadAsync();
            if (stored is null)
            {
                stored = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                await _identity.SaveAsync(stored, token);
            }
            _token = stored;
        }
        finally { _initialization.Release(); }
    }

    public async Task<SupportInbox> InboxAsync(CancellationToken token)
    {
        await InitializeAsync(token);
        using var request = Request(HttpMethod.Get, "support/inbox");
        using var response = await _http.SendAsync(request, token);
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<SupportInbox>(JsonOptions, token) ?? new([]);
    }

    public async Task<SupportReceipt> SubmitAsync(string kind, string nickname, string subject, string body, CancellationToken token)
    {
        await InitializeAsync(token);
        var previous = await _draft.LoadAsync();
        var draft = previous is null ? null : JsonSerializer.Deserialize<SupportDraft>(previous, JsonOptions);
        if (draft is null || draft.Kind != kind || draft.Nickname != nickname || draft.Subject != subject || draft.Body != body)
            draft = new(Guid.NewGuid().ToString("N"), kind, nickname, subject, body);
        // Persist before sending so a lost response can be retried without duplicate mail.
        await _draft.SaveAsync(JsonSerializer.Serialize(draft, JsonOptions), token);
        using var request = Request(HttpMethod.Post, "support");
        request.Content = JsonContent.Create(draft, options: JsonOptions);
        using var response = await _http.SendAsync(request, token);
        await EnsureSuccessAsync(response, token);
        var receipt = await response.Content.ReadFromJsonAsync<SupportReceipt>(JsonOptions, token)
            ?? throw new HttpRequestException("접수 결과를 확인할 수 없어요. 같은 내용으로 다시 시도해주세요.");
        _draft.Delete();
        return receipt;
    }

    public async Task<SupportDraft?> PendingDraftAsync()
    {
        var raw = await _draft.LoadAsync();
        return raw is null ? null : JsonSerializer.Deserialize<SupportDraft>(raw, JsonOptions);
    }

    public async Task ReplyAsync(string ticketId, string requestId, string body, CancellationToken token)
    {
        await InitializeAsync(token);
        using var request = Request(HttpMethod.Post, "support/" + ticketId + "/reply");
        request.Content = JsonContent.Create(new { id = requestId, body });
        using var response = await _http.SendAsync(request, token);
        await EnsureSuccessAsync(response, token);
    }

    public async Task WatchAsync(Action changed, CancellationToken token)
    {
        var retry = 1;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await InitializeAsync(token);
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + _token);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
                var uri = new UriBuilder(new Uri(_http.BaseAddress!, "support/events")) { Scheme = _http.BaseAddress!.Scheme == "https" ? "wss" : "ws" };
                await socket.ConnectAsync(uri.Uri, token);
                retry = 1;
                var buffer = new byte[256];
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer.AsMemory(), token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (!result.EndOfMessage || result.MessageType != WebSocketMessageType.Text) throw new WebSocketException("Invalid event");
                    var signal = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (signal is "ready" or "changed") changed();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (error is WebSocketException or HttpRequestException or IOException or OperationCanceledException) { }
            try { await Task.Delay(TimeSpan.FromSeconds(retry), token); }
            catch (OperationCanceledException) { break; }
            retry = Math.Min(30, retry * 2);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var error = "문의 서버에 연결할 수 없어요. 잠시 후 다시 시도해주세요.";
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(token);
            if (payload.TryGetProperty("error", out var message)) error = message.GetString() ?? error;
        }
        catch (JsonException) { }
        throw new HttpRequestException(error, null, response.StatusCode);
    }

    public void Dispose() { _http.Dispose(); _initialization.Dispose(); }
}
