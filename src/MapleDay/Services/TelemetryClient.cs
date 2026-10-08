using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed class UsageState
{
    public string Installation { get; set; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public List<string> Aliases { get; set; } = [];
}

public sealed record UsageCharacter(string Nickname, int Level, string World);

public sealed class TelemetryClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ApiKeyStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UsageState? _identity;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    public TelemetryClient(HttpClient? http = null, string? path = null)
    {
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri("https://server.morialuluka.com/api/mapleday/"), Timeout = TimeSpan.FromSeconds(15) };
        _store = new(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "usage-identity.dat"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleDay-Telemetry/1.0");
    }

    public async Task SendAsync(IEnumerable<string> highestNicknames, Version version, DiagnosticQueue queue, bool usageEnabled, CancellationToken token, bool anonymous = false, UsageCharacter? character = null)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_identity is null)
            {
                var saved = await _store.LoadAsync();
                _identity = saved is null ? new() : JsonSerializer.Deserialize<UsageState>(saved) ?? new();
            }
            var nicknames = highestNicknames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
            var aliases = nicknames.Select(UsageIdentity.NicknameHash);
            _identity.Aliases = _identity.Aliases.Concat(aliases).Distinct(StringComparer.Ordinal).TakeLast(64).ToList();
            // Persist identity and old/new aliases before network requests, including offline launches.
            await _store.SaveAsync(JsonSerializer.Serialize(_identity), token);
            if (usageEnabled)
            {
                var payload = new { _identity.Installation, Version = version.ToString(4), Aliases = _identity.Aliases,
                    Anonymous = anonymous, Nicknames = anonymous ? Array.Empty<string>() : nicknames };
                var profile = !anonymous && character is { Level: >= 1 and <= 300 } && !string.IsNullOrWhiteSpace(character.World)
                    && nicknames.Contains(character.Nickname, StringComparer.Ordinal) ? character : null;
                if (profile is null)
                {
                    using var response = await _http.PostAsJsonAsync("usage", payload, _json, token);
                    response.EnsureSuccessStatusCode();
                }
                else
                {
                    using var response = await _http.PostAsJsonAsync("usage", new { payload.Installation, payload.Version, payload.Aliases,
                        payload.Anonymous, payload.Nicknames, Profiles = new[] { profile } }, _json, token);
                    // The local test build can run before the server rollout. Retry only the
                    // old server's exact unknown-field rejection, never a profile validation error.
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest
                        && await response.Content.ReadFromJsonAsync<JsonElement>(token) is var error
                        && error.TryGetProperty("error", out var message) && message.GetString() == "허용되지 않은 집계 정보입니다.")
                    {
                        using var legacy = await _http.PostAsJsonAsync("usage", payload, _json, token);
                        legacy.EnsureSuccessStatusCode();
                    }
                    else response.EnsureSuccessStatusCode();
                }
            }
            foreach (var pending in queue.Pending())
            {
                token.ThrowIfCancellationRequested();
                var report = pending.Report;
                using var sent = await _http.PostAsJsonAsync("errors", new { report.Id, _identity.Installation, report.Category,
                    report.ExceptionType, report.Hresult, report.Frames, report.Version, report.OsVersion, report.Fatal, report.Occurred }, _json, token);
                if (sent.StatusCode == System.Net.HttpStatusCode.BadRequest) { queue.Acknowledge(pending.Path); continue; }
                sent.EnsureSuccessStatusCode();
                queue.Acknowledge(pending.Path);
            }
        }
        finally { _gate.Release(); }
    }
    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
}
