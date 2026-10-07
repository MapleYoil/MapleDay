using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed record CachedCharacter(CharacterSummary Summary, CharacterBasic? Basic = null, CroppedCharacterImage? Image = null, bool ImageFailed = false, DateTimeOffset? UpdatedAt = null);
public sealed record CharacterRosterCache(string KeyHash, int AccountCount, DateTimeOffset UpdatedAt, List<CachedCharacter> Characters);

public sealed class CharacterCacheStore(string? path = null, string? profileRoot = null)
{
    private readonly string _legacyPath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "characters.dat");
    private readonly string? _profileRoot = profileRoot ?? (path is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "character-cache") : null);
    private ApiKeyStore StoreFor(string apiKey) => new(_profileRoot is null ? _legacyPath : Path.Combine(_profileRoot, KeyHash(apiKey) + ".dat"));

    public async Task<CharacterRosterCache?> LoadAsync(string apiKey)
    {
        var raw = await StoreFor(apiKey).LoadAsync();
        if (raw is null && _profileRoot is not null) raw = await new ApiKeyStore(_legacyPath).LoadAsync();
        if (raw is null) return null;
        var cache = JsonSerializer.Deserialize<CharacterRosterCache>(raw);
        if (cache is null || cache.KeyHash != KeyHash(apiKey) || cache.Characters is null) return null;
        var characters = cache.Characters.Where(character => character.Summary is not null && !string.IsNullOrWhiteSpace(character.Summary.Ocid))
            .DistinctBy(character => character.Summary.Ocid)
            .Select(character => character.UpdatedAt is null && character.Basic is not null ? character with { UpdatedAt = cache.UpdatedAt } : character)
            .Select(character => character.Image is { } image && (image.Width != CharacterImageCrop.FrameWidth
                || image.Height != CharacterImageCrop.FrameHeight || image.Pixels is null
                || image.Pixels.Length != CharacterImageCrop.FrameWidth * CharacterImageCrop.FrameHeight * 4)
                    ? character with { Image = null, ImageFailed = true } : character).ToList();
        return cache with { Characters = characters };
    }

    public Task SaveAsync(string apiKey, int accountCount, List<CachedCharacter> characters, CancellationToken token) =>
        StoreFor(apiKey).SaveAsync(JsonSerializer.Serialize(new CharacterRosterCache(KeyHash(apiKey), accountCount, DateTimeOffset.UtcNow, characters)), token);

    public void Delete() => new ApiKeyStore(_legacyPath).Delete();
    private static string KeyHash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
