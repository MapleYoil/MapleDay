using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;
namespace MapleDay.Storage.Tests;

public class RecordSyncClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MapleDay-Sync-Test-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    private readonly AppSettings _settings = new() { IncomeDisplayMode = "cash", SchedulerOcids = ["test-ocid"],
        HuntingIncomeRecords = [new("hunt", "test-ocid", new(2026, 10, 10), 100_000_000, 100, 1_000_000)] };
    private RecordSyncClient Client(Action<AppSettings>? save = null) => new(_root, _server, save ?? (_ => { }));
    private static Task<bool> Confirm(int _, int __) => Task.FromResult(true);
    private static Task<IReadOnlySet<string>?> Resolve(IReadOnlyList<SyncConflict> _) => Task.FromResult<IReadOnlySet<string>?>(new HashSet<string>());
    [Fact] public async Task FirstSyncRequiresConfirmationAndSendsOnlyEncryptedAllowedRecords()
    {
        using var client = Client(); await client.ConnectAsync("", "시험 PC", default);
        await client.SynchronizeAsync(_settings, (_, _) => Task.FromResult(false), Resolve, default);
        Assert.Equal(0, _server.Writes); Assert.Empty(_server.Data);
        await client.SynchronizeAsync(_settings, Confirm, Resolve, default);
        Assert.Equal(1, _server.Writes); Assert.True(client.Checkpoint!.Confirmed);
        var checkpoint = client.Checkpoint;
        var raw = RecordSync.Decrypt(Convert.FromBase64String(_server.Data), checkpoint.Credentials.Key, checkpoint.Credentials.Vault);
        Assert.Equal(2, raw.Records.Count); Assert.All(raw.Records.Keys, key => Assert.True(key.StartsWith("hunting/") || key.StartsWith("character/")));
        Assert.DoesNotContain("test-ocid", _server.LastRequest);
        Assert.DoesNotContain(checkpoint.Credentials.Key, _server.LastRequest);
        Assert.DoesNotContain(checkpoint.Credentials.Key, Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_root, "sync.dat"))));
    }
    [Fact] public async Task SavedBaselinePropagatesDeletionAndKeepsPcPreferences()
    {
        using (var first = Client()) { await first.ConnectAsync("", "시험 PC", default); await first.SynchronizeAsync(_settings, Confirm, Resolve, default); }
        _settings.HuntingIncomeRecords.Clear();
        using var second = Client(); await second.LoadAsync(); await second.SynchronizeAsync(_settings, Confirm, Resolve, default);
        var c = second.Checkpoint!.Credentials;
        Assert.DoesNotContain(RecordSync.Decrypt(Convert.FromBase64String(_server.Data), c.Key, c.Vault).Records.Keys, key => key.StartsWith("hunting/"));
        Assert.Equal("cash", _settings.IncomeDisplayMode);
    }
    [Fact] public async Task EditsDuringUploadNeverGetOverwrittenByIncomingSnapshot()
    {
        using var client = Client(); await client.ConnectAsync("", "시험 PC", default);
        _server.BeforeWrite = () => _settings.HuntingIncomeRecords[0] = _settings.HuntingIncomeRecords[0] with { Meso = 777 };
        var result = await client.SynchronizeAsync(_settings, Confirm, Resolve, default);
        Assert.Contains("기록이 바뀌었습니다", result); Assert.Equal(777, _settings.HuntingIncomeRecords[0].Meso); Assert.False(client.Checkpoint!.Confirmed);
    }
    [Fact] public async Task ConcurrentServerRevisionDoesNotApplyOrAdvanceBaseline()
    {
        using var client = Client(); await client.ConnectAsync("", "시험 PC", default); _server.Conflict = true;
        var result = await client.SynchronizeAsync(_settings, Confirm, Resolve, default);
        Assert.Contains("먼저 저장", result); Assert.False(client.Checkpoint!.Confirmed); Assert.Single(_settings.HuntingIncomeRecords);
    }
    [Fact] public async Task IncomingChangesAreBackedUpAndFailedPersistenceRollsBack()
    {
        var fail = false; using var client = Client(_ => { if (fail) throw new IOException("test save failure"); });
        await client.ConnectAsync("", "시험 PC", default); await client.SynchronizeAsync(_settings, Confirm, Resolve, default);
        var original = SyncRecords.Capture(_settings); var remote = JsonSerializer.Deserialize<SyncDocument>(JsonSerializer.Serialize(original))!;
        var changed = _settings.HuntingIncomeRecords[0] with { Meso = 999 };
        remote.Records[remote.Records.Keys.Single(key => key.StartsWith("hunting/"))] = JsonSerializer.SerializeToElement(changed);
        var c = client.Checkpoint!.Credentials;
        _server.Data = Convert.ToBase64String(RecordSync.Encrypt(remote, c.Key, c.Vault)); _server.Revision++;
        fail = true;
        await Assert.ThrowsAsync<IOException>(() => client.SynchronizeAsync(_settings, Confirm, Resolve, default));
        Assert.True(SyncRecords.Same(original, SyncRecords.Capture(_settings))); Assert.Single(client.Backups());
        fail = false; await client.SynchronizeAsync(_settings, Confirm, Resolve, default);
        Assert.Equal(999, _settings.HuntingIncomeRecords[0].Meso);
        await client.RestoreAsync(_settings, client.Backups().Last().Path, default);
        Assert.Equal(100_000_000, _settings.HuntingIncomeRecords[0].Meso);
    }
    [Fact] public void MalformedIncomingKeysDoNotMutateAnyRecords()
    {
        var original = SyncRecords.Capture(_settings); var broken = SyncDocument.Empty;
        broken.Records["hunting/wrong"] = JsonSerializer.SerializeToElement(_settings.HuntingIncomeRecords[0] with { Meso = 999 });
        Assert.Throws<ArgumentException>(() => SyncRecords.Apply(_settings, broken));
        Assert.True(SyncRecords.Same(original, SyncRecords.Capture(_settings)));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeServer : HttpMessageHandler
    {
        public string Data = "", LastRequest = ""; public long Revision; public int Writes; public bool Conflict; public Action? BeforeWrite;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var route = request.RequestUri!.Segments.Last();
            if (route == "create") return Response(new { vault = new string('a', 32), device = new string('b', 32), token = new string('c', 64), recovery = new string('d', 64) });
            Assert.Equal(new string('c', 64), request.Headers.Authorization!.Parameter);
            if (request.Method == HttpMethod.Get) return Response(new { revision = Revision, data = Data });
            LastRequest = await request.Content!.ReadAsStringAsync(cancellationToken); var body = JsonSerializer.Deserialize<JsonElement>(LastRequest);
            BeforeWrite?.Invoke(); BeforeWrite = null;
            if (Conflict || body.GetProperty("revision").GetInt64() != Revision) return Response(new { revision = Revision, data = Data }, HttpStatusCode.Conflict);
            Data = body.GetProperty("data").GetString()!; Revision++; Writes++;
            return Response(new { revision = Revision, data = Data });
        }
        private static HttpResponseMessage Response(object data, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(data) };
        protected override void Dispose(bool disposing) { /* Reuse this in-memory server across simulated device restarts. */ }
    }
}
