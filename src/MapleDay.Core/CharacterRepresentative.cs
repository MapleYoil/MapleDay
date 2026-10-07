namespace MapleDay.Core;

public static class CharacterRepresentative
{
    public static string? Choose(IEnumerable<CharacterSummary> characters, string? preferredOcid)
    {
        var roster = characters.Where(character => !string.IsNullOrWhiteSpace(character.Ocid)).ToArray();
        return roster.FirstOrDefault(character => character.Ocid == preferredOcid)?.Ocid
            ?? roster.OrderByDescending(character => character.Level).ThenBy(character => character.World, StringComparer.Ordinal)
                .ThenBy(character => character.Name, StringComparer.Ordinal).ThenBy(character => character.Ocid, StringComparer.Ordinal).FirstOrDefault()?.Ocid;
    }
}
