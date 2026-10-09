using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed record LootMarketPrice(string ItemId, string Variant, string Region, long PriceEok, DateOnly Date)
{
    [JsonIgnore] public long WholeEokPrice => checked(PriceEok * 100_000_000);
    [JsonIgnore] public string Label => $"{Variant} · {PriceEok}억 메소 · {Date:MM.dd}";
}
public sealed record LootMarketSnapshot(DateTimeOffset? FetchedAt, LootMarketPrice[] Prices)
{
    public IReadOnlyList<LootMarketPrice> ForItem(string itemId, string world) => Prices.Where(price => price.ItemId == itemId
        && price.Region == (world.StartsWith("챌린저", StringComparison.Ordinal) ? "challengers" : "normal")
        && price.PriceEok is >= 0 and <= BossLoot.MaximumAmount / 100_000_000).ToArray();
}

public sealed class LootMarketClient(HttpClient? http = null, Func<byte[], byte[]>? sign = null) : IDisposable
{
    private readonly HttpClient _http = http ?? new() { BaseAddress = new Uri("https://server.morialuluka.com/api/mapleday/"), Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LootMarketSnapshot? _cached;
    private DateTimeOffset _nextCheck;
    private string? _session;
    private long _sessionExpiry;
    private sealed record MarketSession(string Token, long ExpiresAt);
    private static byte[] Sign(byte[] data)
    {
        using var resource = typeof(LootMarketClient).Assembly.GetManifestResourceStream("MapleDay.MarketClient.pem")
            ?? throw new InvalidDataException("이 빌드는 시세 인증을 지원하지 않습니다.");
        using var reader = new StreamReader(resource);
        using var rsa = RSA.Create(); rsa.ImportFromPem(reader.ReadToEnd());
        return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
    private async Task AuthenticateAsync(CancellationToken token)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_session is not null && _sessionExpiry > timestamp + 20) return;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = Convert.ToBase64String((sign ?? Sign)(Encoding.UTF8.GetBytes($"{nonce}\n{timestamp}")));
        using var response = await _http.PostAsJsonAsync("market/session", new { nonce, timestamp, signature }, token);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<MarketSession>(token)
            ?? throw new InvalidDataException("시세 인증을 완료하지 못했습니다.");
        if (string.IsNullOrEmpty(session.Token) || session.Token.Length > 200 || session.ExpiresAt <= timestamp || session.ExpiresAt > timestamp + 360)
            throw new InvalidDataException("시세 인증 응답이 올바르지 않습니다.");
        _session = session.Token; _sessionExpiry = session.ExpiresAt;
    }
    public async Task<LootMarketSnapshot> GetAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _nextCheck) return _cached;
            await AuthenticateAsync(token);
            using var request = new HttpRequestMessage(HttpMethod.Get, "market/prices");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session);
            using var response = await _http.SendAsync(request, token);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) { _session = null; _sessionExpiry = 0; }
            response.EnsureSuccessStatusCode();
            var snapshot = await response.Content.ReadFromJsonAsync<LootMarketSnapshot>(token) ?? throw new InvalidDataException("시세 응답이 없습니다.");
            if (snapshot.Prices is null || snapshot.Prices.Length > 1000)
                throw new InvalidDataException("시세 응답을 확인하지 못했습니다.");
            _cached = snapshot; _nextCheck = DateTimeOffset.UtcNow.AddMinutes(5);
            return snapshot;
        }
        finally { _gate.Release(); }
    }
    public void Dispose() { if (http is null) _http.Dispose(); }
}
