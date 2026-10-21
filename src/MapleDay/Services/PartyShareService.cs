using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapleDay.Core;
using Microsoft.UI.Dispatching;

namespace MapleDay.Services;

public sealed class PartyShareService : IDisposable
{
    private sealed class Entry(SharedPartyLink link, RecordSyncClient client)
    {
        public SharedPartyLink Link { get; set; } = link;
        public RecordSyncClient Client { get; } = client;
        public CancellationTokenSource Lifetime { get; } = new();
        public Task? Watch { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings? _settings;
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _debounce;
    private DispatcherQueueTimer? _poll;
    private string _root = "";
    private bool _applying, _stopped, _syncRequested;
    public event EventHandler? Changed;
    public event Action<string>? StatusChanged;

    public bool IsShared(string partyId) => _entries.ContainsKey(partyId);
    public SharedPartyLink? Link(string partyId) => _entries.GetValueOrDefault(partyId)?.Link;
    public bool CanDeleteSharedData(string partyId) => _entries.GetValueOrDefault(partyId)?.Client.RecoveryCode.Length > 0;

    public async Task InitializeAsync(AppSettings settings, DispatcherQueue dispatcher)
    {
        if (_settings is not null || _stopped) return;
        _settings = settings; _dispatcher = dispatcher;
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "party-share");
        _debounce = dispatcher.CreateTimer(); _debounce.Interval = TimeSpan.FromSeconds(2); _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => _ = SynchronizeAsync();
        _poll = dispatcher.CreateTimer(); _poll.Interval = TimeSpan.FromSeconds(15); _poll.IsRepeating = true;
        _poll.Tick += (_, _) => _ = SynchronizeAsync();
        AppSettings.Saved += OnSaved;
        foreach (var link in settings.SharedPartyLinks.Where(ValidLink).GroupBy(link => link.PartyId).Select(group => group.First()).ToArray())
        {
            var client = Client(link.PartyId);
            try
            {
                await client.LoadAsync();
                if (client.Checkpoint is null) { client.Dispose(); continue; }
                var entry = new Entry(link, client); _entries[link.PartyId] = entry; StartWatch(entry);
            }
            catch (Exception error) when (Expected(error)) { client.Dispose(); }
        }
        EnsurePoll();
        ScheduleSync();
    }

    private RecordSyncClient Client(string partyId) => new(Path.Combine(_root, Safe(partyId)), save: _ => { },
        capture: SyncRecords.CapturePartyShare, apply: SyncRecords.ApplyPartyShare);
    private static string Safe(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool ValidLink(SharedPartyLink? link) => link is not null && link.PartyId is { Length: > 0 and <= 100 }
        && link.MemberId is { Length: > 0 and <= 100 } && link.LocalOcid is { Length: > 0 and <= 200 };
    private static bool Expected(Exception error) => error is ArgumentException or IOException or UnauthorizedAccessException
        or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or CryptographicException
        or InvalidOperationException or FormatException or System.Runtime.InteropServices.COMException;

    private void OnSaved(AppSettings settings)
    {
        if (!_stopped && !_applying && settings == _settings) ScheduleSync();
    }

    public void ScheduleSync()
    {
        if (_stopped || _debounce is null) return;
        if (_gate.CurrentCount == 0) { _syncRequested = true; return; }
        _debounce.Stop(); _debounce.Start();
    }

    private void StartWatch(Entry entry)
    {
        if (_stopped || entry.Watch is { IsCompleted: false }) return;
        entry.Watch = entry.Client.WatchAsync(() => _dispatcher?.TryEnqueue(() => _ = SynchronizeAsync(entry.Link.PartyId)), entry.Lifetime.Token);
    }
    private void EnsurePoll()
    {
        if (_poll is null) return;
        if (_entries.Count > 0 && !_poll.IsRunning) _poll.Start();
        else if (_entries.Count == 0) _poll.Stop();
    }

    private static AppSettings SharedSettings(AppSettings source, SharedPartyLink link)
    {
        var party = source.Parties.SingleOrDefault(item => item.Id == link.PartyId)
            ?? throw new ArgumentException("공유 파티를 찾을 수 없습니다.");
        var normalizedParty = PartyShareRecords.NormalizeParty(party);
        var loot = source.BossLootRecords.Where(record => record.SharedPartyId == party.Id)
            .Select(record => PartyShareRecords.NormalizeLoot(record, normalizedParty)).ToList();
        return new AppSettings { Parties = [normalizedParty], BossLootRecords = loot };
    }

    private void ApplyShared(Entry entry, AppSettings shared)
    {
        if (_settings is null) return;
        var party = shared.Parties.SingleOrDefault(item => item.Id == entry.Link.PartyId)
            ?? throw new ArgumentException("공유 파티 정보가 없습니다.");
        var localParty = _settings.Parties.FirstOrDefault(item => item.Id == party.Id);
        var projectedParty = PartyShareRecords.ProjectParty(party, entry.Link, localParty);
        var currentLoot = _settings.BossLootRecords.Where(record => record.SharedPartyId == party.Id)
            .GroupBy(record => record.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var projectedLoot = shared.BossLootRecords.Select(record => PartyShareRecords.ProjectLoot(record, entry.Link,
            currentLoot.GetValueOrDefault(record.Id))).ToArray();
        var parties = _settings.Parties.Select(item => item.Id == party.Id ? projectedParty : item).ToList();
        if (!parties.Any(item => item.Id == party.Id)) parties.Add(projectedParty);
        var loot = _settings.BossLootRecords.Where(record => record.SharedPartyId != party.Id).Concat(projectedLoot).ToList();
        if (Same(parties, _settings.Parties) && Same(loot, _settings.BossLootRecords)) return;
        var previousParties = _settings.Parties; var previousLoot = _settings.BossLootRecords;
        _settings.Parties = parties; _settings.BossLootRecords = loot; _applying = true;
        try { _settings.Save(); }
        catch { _settings.Parties = previousParties; _settings.BossLootRecords = previousLoot; throw; }
        finally { _applying = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task CreateAsync(PartyPreset party, string memberId, CancellationToken token = default)
    {
        if (_settings is null || _stopped) throw new InvalidOperationException("파티 공유를 시작할 수 없습니다.");
        if (_entries.ContainsKey(party.Id)) throw new ArgumentException("이미 공유 중인 파티입니다.");
        var member = party.Members.FirstOrDefault(item => item.Id == memberId && !string.IsNullOrWhiteSpace(item.Ocid))
            ?? throw new ArgumentException("내 캐릭터가 연결된 파티원을 선택해주세요.");
        var link = new SharedPartyLink(party.Id, member.Id, member.Ocid);
        var client = Client(party.Id);
        try
        {
            await client.ConnectAsync("", "파티 공유 PC", token);
            var shared = SharedSettings(_settings, link);
            var result = await client.SynchronizeAsync(shared, (_, _) => Task.FromResult(true),
                conflicts => Task.FromResult<IReadOnlySet<string>?>(conflicts.Select(item => item.Key).ToHashSet(StringComparer.Ordinal)), token);
            if (client.Checkpoint?.Confirmed != true) throw new IOException(result);
            var entry = new Entry(link, client); _entries[party.Id] = entry;
            _applying = true;
            try { _settings.SharedPartyLinks = _settings.SharedPartyLinks.Where(item => item.PartyId != party.Id).Append(link).ToList(); _settings.Save(); }
            finally { _applying = false; }
            StartWatch(entry); EnsurePoll(); StatusChanged?.Invoke("파티 공유를 시작했어요. 파티원별 초대 코드를 보내주세요.");
        }
        catch { client.Disconnect(); client.Dispose(); throw; }
    }

    public async Task<string> InviteAsync(string partyId, string memberId, CancellationToken token = default)
    {
        if (_settings is null || !_entries.TryGetValue(partyId, out var entry)) throw new ArgumentException("먼저 파티 공유를 시작해주세요.");
        var party = _settings.Parties.FirstOrDefault(item => item.Id == partyId);
        if (party?.Members.Any(member => member.Id == memberId) != true) throw new ArgumentException("초대할 파티원을 선택해주세요.");
        var inner = await entry.Client.PairAsync(token);
        return string.Join('.', "party1", Encode(partyId), Encode(memberId), Encode(inner));
    }

    public async Task JoinAsync(string code, string localOcid, CancellationToken token = default)
    {
        if (_settings is null || _stopped) throw new InvalidOperationException("공유 파티에 참여할 수 없습니다.");
        var parts = code.Trim().Split('.');
        if (parts.Length != 4 || parts[0] != "party1") throw new ArgumentException("파티 초대 코드를 확인해주세요.");
        string partyId, memberId, inner;
        try { partyId = Decode(parts[1]); memberId = Decode(parts[2]); inner = Decode(parts[3]); }
        catch (FormatException) { throw new ArgumentException("파티 초대 코드를 확인해주세요."); }
        var link = new SharedPartyLink(partyId, memberId, localOcid);
        if (!ValidLink(link) || _entries.ContainsKey(partyId)) throw new ArgumentException("이미 참여했거나 올바르지 않은 파티 초대입니다.");
        var client = Client(partyId);
        try
        {
            await client.ConnectAsync(inner, "파티 공유 PC", token);
            var shared = new AppSettings();
            var result = await client.SynchronizeAsync(shared, (_, _) => Task.FromResult(true), _ => Task.FromResult<IReadOnlySet<string>?>(null), token);
            if (client.Checkpoint?.Confirmed != true || shared.Parties.Count != 1 || shared.Parties[0].Id != partyId
                || !shared.Parties[0].Members.Any(member => member.Id == memberId))
                throw new ArgumentException(string.IsNullOrWhiteSpace(result) ? "파티 초대 정보를 확인해주세요." : result);
            var entry = new Entry(link, client); _entries[partyId] = entry;
            _settings.SharedPartyLinks = _settings.SharedPartyLinks.Where(item => item.PartyId != partyId).Append(link).ToList();
            ApplyShared(entry, shared);
            _applying = true; try { _settings.Save(); } finally { _applying = false; }
            StartWatch(entry); EnsurePoll(); StatusChanged?.Invoke("공유 파티에 참여했어요. 기존 물욕템과 판매 상태를 불러왔습니다.");
        }
        catch
        {
            try { if (client.Checkpoint is not null) await client.RevokeAsync(client.Checkpoint.Credentials.Device, token); } catch (Exception error) when (Expected(error)) { }
            client.Disconnect(); client.Dispose(); _entries.Remove(partyId); throw;
        }
    }

    public async Task<string> SynchronizeAsync(string? partyId = null, CancellationToken token = default)
    {
        if (_settings is null || _stopped || !await _gate.WaitAsync(0, token)) return "";
        try
        {
            var messages = new List<string>();
            var entries = partyId is null ? _entries.Values.ToArray()
                : _entries.TryGetValue(partyId, out var selected) ? [selected] : [];
            foreach (var entry in entries)
            {
                try
                {
                    entry.Link = _settings.SharedPartyLinks.FirstOrDefault(link => link.PartyId == entry.Link.PartyId) ?? entry.Link;
                    var before = SharedSettings(_settings, entry.Link); var beforeDocument = SyncRecords.CapturePartyShare(before);
                    var result = await entry.Client.SynchronizeAsync(before, (_, _) => Task.FromResult(true), ResolveLootConflicts, token);
                    if (result.Contains("충돌", StringComparison.Ordinal)) { messages.Add(result); continue; }
                    if (before.Parties.Count == 0)
                    {
                        DetachLocal(entry); messages.Add("파티 공유가 종료되어 연결을 정리했어요. 이 PC의 기록은 유지됩니다."); continue;
                    }
                    if (!SyncRecords.Same(beforeDocument, SyncRecords.CapturePartyShare(SharedSettings(_settings, entry.Link))))
                    { ScheduleSync(); continue; }
                    ApplyShared(entry, before); messages.Add(result);
                }
                catch (ArgumentException error) when (error.Message.Contains("연결이 해제", StringComparison.Ordinal)
                    || error.Message.Contains("동기화 연결이 필요", StringComparison.Ordinal))
                { DetachLocal(entry); messages.Add("파티 공유가 종료되어 연결을 정리했어요. 이 PC의 기록은 유지됩니다."); }
                catch (Exception error) when (Expected(error)) { messages.Add(error.Message); }
            }
            var message = messages.LastOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "공유할 변경 사항이 없습니다.";
            StatusChanged?.Invoke(message); return message;
        }
        finally
        {
            _gate.Release();
            if (_syncRequested) { _syncRequested = false; ScheduleSync(); }
        }
    }

    private void DetachLocal(Entry entry)
    {
        if (_settings is null || !_entries.Remove(entry.Link.PartyId)) return;
        entry.Lifetime.Cancel(); entry.Client.Disconnect(); entry.Client.Dispose(); EnsurePoll();
        _applying = true;
        try
        {
            _settings.SharedPartyLinks = _settings.SharedPartyLinks.Where(link => link.PartyId != entry.Link.PartyId).ToList();
            _settings.Save();
        }
        finally { _applying = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Task<IReadOnlySet<string>?> ResolveLootConflicts(IReadOnlyList<SyncConflict> conflicts)
    {
        var local = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conflict in conflicts)
        {
            if (!conflict.Key.StartsWith("loot/", StringComparison.Ordinal)) return Task.FromResult<IReadOnlySet<string>?>(null);
            if (conflict.Local is null || conflict.Remote is null)
            {
                if (conflict.Local is not null) local.Add(conflict.Key);
                continue;
            }
            if (!conflict.Local.Value.TryGetProperty("SharedUpdatedAt", out var localTime)
                || !conflict.Remote.Value.TryGetProperty("SharedUpdatedAt", out var remoteTime)
                || !localTime.TryGetInt64(out var left) || !remoteTime.TryGetInt64(out var right) || left <= 0 || right <= 0)
                return Task.FromResult<IReadOnlySet<string>?>(null);
            if (left > right || left == right && string.CompareOrdinal(conflict.Local.Value.GetRawText(), conflict.Remote.Value.GetRawText()) >= 0)
                local.Add(conflict.Key);
        }
        return Task.FromResult<IReadOnlySet<string>?>(local);
    }

    public async Task LeaveAsync(string partyId, bool deleteForEveryone, CancellationToken token = default)
    {
        if (_settings is null || !_entries.Remove(partyId, out var entry)) return;
        entry.Lifetime.Cancel();
        try
        {
            if (deleteForEveryone && entry.Client.RecoveryCode.Length > 0) await entry.Client.DeleteAsync("삭제", token);
            else if (entry.Client.Checkpoint is { } checkpoint) await entry.Client.RevokeAsync(checkpoint.Credentials.Device, token);
        }
        catch (Exception error) when (Expected(error)) { }
        entry.Client.Disconnect(); entry.Client.Dispose();
        EnsurePoll();
        _applying = true;
        try { _settings.SharedPartyLinks = _settings.SharedPartyLinks.Where(link => link.PartyId != partyId).ToList(); _settings.Save(); }
        finally { _applying = false; }
        StatusChanged?.Invoke(deleteForEveryone ? "모든 파티원의 공유 연결을 종료했어요. 로컬 기록은 유지됩니다." : "이 PC의 파티 공유 연결을 해제했어요. 로컬 기록은 유지됩니다.");
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool Same<T>(T left, T right) => RecordSync.Same(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
    private static string Decode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/'); text += new string('=', (4 - text.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }

    public async Task StopAsync()
    {
        if (_stopped) return; _stopped = true; AppSettings.Saved -= OnSaved; _debounce?.Stop(); _poll?.Stop();
        foreach (var entry in _entries.Values) entry.Lifetime.Cancel();
        await Task.WhenAll(_entries.Values.Select(entry => entry.Watch ?? Task.CompletedTask));
        foreach (var entry in _entries.Values) { entry.Lifetime.Dispose(); entry.Client.Dispose(); }
        _entries.Clear();
    }

    public void Dispose() { _ = StopAsync(); _gate.Dispose(); }
}
