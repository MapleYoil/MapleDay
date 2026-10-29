namespace MapleDay.Core;

public sealed class CharacterApiKeyMap
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

    public void Remember(string apiKey, IEnumerable<CharacterSummary> characters)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        foreach (var character in characters.Where(character => !string.IsNullOrWhiteSpace(character.Ocid)))
            _keys[character.Ocid] = apiKey;
    }

    public string? Resolve(string ocid) => _keys.GetValueOrDefault(ocid);
}
