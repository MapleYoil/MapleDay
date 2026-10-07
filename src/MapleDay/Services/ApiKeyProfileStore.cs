using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MapleDay.Services;

public sealed record ApiKeyProfile
{
    public string Key { get; }
    public string? RepresentativeOcid { get; }
    public string? Name { get; }
    public string? World { get; }
    [System.Text.Json.Serialization.JsonConstructor]
    public ApiKeyProfile(string key, string? representativeOcid = null, string? name = null, string? world = null)
    { Key = key; RepresentativeOcid = representativeOcid; Name = name; World = world; }
    public string Id => ApiKeyProfileStore.KeyHash(Key);
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"저장된 키 · {Id[..6]}" : $"{Name} · {World} ({Id[..6]})";
}

public sealed class ApiKeyProfileStore(string? path = null)
{
    private readonly ApiKeyStore _store = new(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "api-key-profiles.dat"));
    public static string KeyHash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public async Task<List<ApiKeyProfile>> LoadAsync()
    {
        var text = await _store.LoadAsync();
        return text is null ? [] : (JsonSerializer.Deserialize<List<ApiKeyProfile>>(text) ?? [])
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Key)).DistinctBy(profile => profile.Id).ToList();
    }
    public Task SaveAsync(IEnumerable<ApiKeyProfile> profiles, CancellationToken token = default) =>
        _store.SaveAsync(JsonSerializer.Serialize(profiles.DistinctBy(profile => profile.Id).ToList()), token);
}
