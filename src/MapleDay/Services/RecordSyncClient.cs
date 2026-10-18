using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using MapleDay.Core;
namespace MapleDay.Services;

public sealed record SyncCredentials(string Vault, string Device, string Token, string Key, string Recovery = "");
public sealed record SyncCheckpoint(SyncCredentials Credentials, SyncDocument Baseline, bool Confirmed = false, long Revision = -1);
public sealed record SyncState(long revision, string data);
public sealed record SyncDevice(string id, string name, long created, long seen);
public sealed record SyncBackup(string Path, string Label);
public sealed class RecordSyncClient : IDisposable
{
    public const string Endpoint = "https://server.morialuluka.com/api/mapleday/sync/";
    private readonly HttpClient _http;
    private readonly string _root;
    private readonly ApiKeyStore _store;
    private readonly Action<AppSettings> _save;
    public SyncCheckpoint? Checkpoint { get; private set; }
    public bool Applying { get; private set; }
    private readonly SemaphoreSlim _gate = new(1);
    public RecordSyncClient(string? root = null, HttpMessageHandler? handler = null, Action<AppSettings>? save = null)
    {
        _http = handler is null ? new() : new(handler); _http.Timeout = TimeSpan.FromSeconds(25);
        _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay");
        _store = new(Path.Combine(_root, "sync.dat"));
        _save = save ?? (settings => settings.Save());
    }
    public async Task LoadAsync()
    {
        var raw = await _store.LoadAsync(); if (raw is null) return;
        var value = JsonSerializer.Deserialize<SyncCheckpoint>(raw) ?? throw new ArgumentException("저장된 연결 정보를 확인해주세요.");
        static bool Hex(string? text, int length) => text is not null && text.Length == length && text.All(Uri.IsHexDigit);
        if (value.Credentials is not { } c || !Hex(c.Vault, 32) || !Hex(c.Device, 32) || !Hex(c.Token, 64) || !Hex(c.Key, 64)
            || c.Recovery is null || c.Recovery.Length != 0 && !Hex(c.Recovery, 64) || value.Baseline is null)
            throw new ArgumentException("저장된 연결 정보를 확인해주세요.");
        SyncRecords.Apply(new AppSettings(), value.Baseline); Checkpoint = value;
    }
    private async Task SaveAsync(SyncCheckpoint checkpoint, CancellationToken ct) { await _store.SaveAsync(JsonSerializer.Serialize(checkpoint), ct); Checkpoint = checkpoint; }
    private async Task<(JsonElement Data, HttpStatusCode Status)> RequestAsync(string path, object? body = null, bool anonymous = false, CancellationToken ct = default, long revision = -1)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, Endpoint + path);
        if (!anonymous) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Checkpoint?.Credentials.Token ?? "");
        if (body is null && revision >= 0) request.Headers.TryAddWithoutValidation("If-None-Match", $"\"sync-{revision}\"");
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotModified) return (default, response.StatusCode);
        if (response.Content.Headers.ContentLength > 5_700_000) throw new ArgumentException("서버 응답이 너무 큽니다.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var bytes = new byte[8192];
        int read; while ((read = await stream.ReadAsync(bytes, ct)) > 0) { if (buffer.Length+read > 5_700_000) throw new ArgumentException("서버 응답이 너무 큽니다."); buffer.Write(bytes, 0, read); }
        var result = JsonSerializer.Deserialize<JsonElement>(buffer.ToArray());
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
            throw new ArgumentException(result.TryGetProperty("error", out var error) ? error.GetString() : "동기화 서버에 연결하지 못했어요.");
        return (result, response.StatusCode);
    }
    public async Task ConnectAsync(string code, string name, CancellationToken ct)
    {
        if (Checkpoint is not null) throw new ArgumentException("기존 연결을 먼저 해제해주세요.");
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var route = "create"; object body = new { name };
        var recovery = "";
        if (!string.IsNullOrWhiteSpace(code))
        {
            var parts = code.Trim().Split('.');
            if (parts.Length != 4 || parts[0] is not ("pair1" or "recover1") || parts[1].Length != 32 || parts[2].Length != 64 || parts[3].Length != 64)
                throw new ArgumentException("연결 코드 또는 복구 코드를 확인해주세요.");
            _ = Convert.FromHexString(parts[1]); _ = Convert.FromHexString(parts[2]); _ = Convert.FromHexString(parts[3]); key = parts[3];
            route = parts[0] == "pair1" ? "join" : "recover"; body = new { name, code = parts[2], vault = parts[1] };
            if (route == "recover") recovery = parts[2];
        }
        var (data, _) = await RequestAsync(route, body, true, ct);
        if (data.TryGetProperty("recovery", out var secret)) recovery = secret.GetString()!;
        var credentials = new SyncCredentials(data.GetProperty("vault").GetString()!, data.GetProperty("device").GetString()!, data.GetProperty("token").GetString()!, key, recovery);
        if (!string.IsNullOrWhiteSpace(code) && credentials.Vault != code.Trim().Split('.')[1]) throw new ArgumentException("연결 코드를 확인해주세요.");
        await SaveAsync(new(credentials, SyncDocument.Empty), ct);
    }
    public string RecoveryCode => Checkpoint is { Credentials.Recovery.Length: > 0 } value ? $"recover1.{value.Credentials.Vault}.{value.Credentials.Recovery}.{value.Credentials.Key}" : "";
    public async Task<string> PairAsync(CancellationToken ct)
    {
        var (data, _) = await RequestAsync("pair", new { }, ct: ct);
        var credentials = Checkpoint!.Credentials;
        return $"pair1.{credentials.Vault}.{data.GetProperty("code").GetString()}.{credentials.Key}";
    }
    public async Task<SyncDevice[]> DevicesAsync(CancellationToken ct) => (await RequestAsync("devices", ct: ct)).Data.GetProperty("devices").Deserialize<SyncDevice[]>()!;
    public async Task RevokeAsync(string device, CancellationToken ct) => _ = await RequestAsync("revoke", new { device }, ct: ct);
    public async Task DeleteAsync(string confirmation, CancellationToken ct)
    {
        if (Checkpoint?.Credentials.Recovery is not { Length: > 0 } recovery) throw new ArgumentException("이 기기에 복구 권한이 없습니다. 복구 코드로 연결한 기기에서 삭제해주세요.");
        _ = await RequestAsync("delete", new { confirm = confirmation, recovery }, ct: ct); Disconnect();
    }
    public void Disconnect() { Checkpoint = null; _store.Delete(); }
    public SyncBackup[] Backups()
    {
        var folder = Path.Combine(_root, "sync-backups");
        return !Directory.Exists(folder) ? [] : Directory.EnumerateFiles(folder, "*.dat").OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path => new SyncBackup(path, File.GetLastWriteTime(path).ToString("yyyy.MM.dd HH:mm:ss"))).ToArray();
    }
    private async Task BackupAsync(SyncDocument document, CancellationToken ct)
    {
        var folder = Path.Combine(_root, "sync-backups"); Directory.CreateDirectory(folder);
        await new ApiKeyStore(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".dat")).SaveAsync(JsonSerializer.Serialize(document), ct);
        foreach (var old in Backups().Skip(10)) File.Delete(old.Path);
    }
    public async Task RestoreAsync(AppSettings settings, string path, CancellationToken ct)
    {
        if (!Backups().Any(item => item.Path == path)) throw new ArgumentException("이 PC의 동기화 백업을 선택해주세요.");
        ct.ThrowIfCancellationRequested();
        var raw = await new ApiKeyStore(path).LoadAsync() ?? throw new ArgumentException("백업을 읽지 못했어요.");
        var document = JsonSerializer.Deserialize<SyncDocument>(raw) ?? throw new ArgumentException("백업 형식을 확인해주세요.");
        SyncRecords.Apply(new AppSettings(), document);
        var original = SyncRecords.Capture(settings); await BackupAsync(original, ct);
        SyncRecords.Apply(settings, document);
        try { _save(settings); } catch { SyncRecords.Apply(settings, original); throw; }
    }
    public async Task<string> SynchronizeAsync(AppSettings settings, Func<int, int, Task<bool>> confirm,
        Func<IReadOnlyList<SyncConflict>, Task<IReadOnlySet<string>?>> resolve, CancellationToken ct)
    {
        if (Checkpoint is null || !await _gate.WaitAsync(0, ct)) return "";
        try
        {
            var checkpoint = Checkpoint; var local = SyncRecords.Capture(settings);
            var (raw, status) = await RequestAsync("state", ct: ct, revision: checkpoint.Confirmed ? checkpoint.Revision : -1);
            var state = status == HttpStatusCode.NotModified ? new SyncState(checkpoint.Revision, "") : raw.Deserialize<SyncState>()!;
            var remote = status == HttpStatusCode.NotModified ? checkpoint.Baseline : state.data.Length == 0 ? SyncDocument.Empty
                : RecordSync.Decrypt(Convert.FromBase64String(state.data), checkpoint.Credentials.Key, checkpoint.Credentials.Vault);
            if (!checkpoint.Confirmed && !await confirm(local.Records.Count, remote.Records.Count)) return "초기 병합을 취소했습니다. 기록을 전송하지 않았습니다.";
            var merge = RecordSync.Merge(checkpoint.Baseline, local, remote);
            if (merge.Conflicts.Count > 0)
            {
                var choices = await resolve(merge.Conflicts);
                if (choices is null) return $"충돌 {merge.Conflicts.Count}건 · 다음 동기화에서 해결해주세요. 자동 덮어쓰기는 하지 않습니다.";
                merge = RecordSync.Merge(checkpoint.Baseline, local, remote, choices);
            }
            if (!SyncRecords.Same(local, SyncRecords.Capture(settings))) return "동기화 중 기록이 바뀌었습니다. 다음 동기화에서 다시 병합합니다.";
            // Validate the complete incoming record set before saving or uploading.
            var probe = new AppSettings(); SyncRecords.Apply(probe, merge.Document);
            if (!SyncRecords.Same(local, merge.Document))
            {
                await BackupAsync(local, ct);
            }
            var revision = state.revision;
            if (!SyncRecords.Same(remote, merge.Document))
            {
                var encrypted = Convert.ToBase64String(RecordSync.Encrypt(merge.Document, checkpoint.Credentials.Key, checkpoint.Credentials.Vault));
                var response = await RequestAsync("state", new { revision = state.revision, data = encrypted }, ct: ct);
                if (response.Status == HttpStatusCode.Conflict) return "다른 기기가 먼저 저장했습니다. 다음 동기화에서 다시 비교합니다.";
                revision = response.Data.GetProperty("revision").GetInt64();
            }
            ct.ThrowIfCancellationRequested();
            if (Checkpoint != checkpoint || !SyncRecords.Same(local, SyncRecords.Capture(settings)))
                return "동기화 중 기록이 바뀌었습니다. 새 기록을 유지하고 다시 비교합니다.";
            Applying = true;
            try
            {
                if (!SyncRecords.Same(local, merge.Document))
                {
                    SyncRecords.Apply(settings, merge.Document);
                    try { _save(settings); } catch { SyncRecords.Apply(settings, local); throw; }
                }
                if (!checkpoint.Confirmed || checkpoint.Revision != revision || !SyncRecords.Same(checkpoint.Baseline, merge.Document))
                    await SaveAsync(checkpoint with { Baseline = merge.Document, Confirmed = true, Revision = revision }, ct);
            }
            finally { Applying = false; }
            return $"동기화 완료 · {DateTimeOffset.Now:MM.dd HH:mm:ss} · {merge.Document.Records.Count:N0}개 기록";
        }
        finally { _gate.Release(); }
    }
    public async Task WatchAsync(Action changed, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && Checkpoint is not null)
        {
            try
            {
                using var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Authorization", "Bearer " + Checkpoint.Credentials.Token);
                await socket.ConnectAsync(new Uri(Endpoint.Replace("https://", "wss://") + "events"), ct);
                var buffer = new byte[1024];
                while (socket.State == WebSocketState.Open)
                {
                    var length = 0; WebSocketReceiveResult message;
                    do
                    {
                        message = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), ct);
                        length += message.Count;
                        if (length == buffer.Length && !message.EndOfMessage) throw new WebSocketException("Unexpected sync signal");
                    } while (!message.EndOfMessage && message.MessageType != WebSocketMessageType.Close);
                    if (message.MessageType == WebSocketMessageType.Close) break;
                    if (System.Text.Encoding.UTF8.GetString(buffer, 0, length) is "changed" or "ready") changed();
                }
            }
            catch (Exception error) when (error is WebSocketException or HttpRequestException or OperationCanceledException) { }
            if (!ct.IsCancellationRequested) { try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; } }
        }
    }
    public void Dispose() { _http.Dispose(); }
}
