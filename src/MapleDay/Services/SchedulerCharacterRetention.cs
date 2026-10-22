using MapleDay.Core;

namespace MapleDay.Services;

public sealed record SavedSchedulerCharacter(string Ocid, string Name, string World, string Class, int Level)
{
    public CharacterSummary Summary => new() { Ocid = Ocid, Name = Name, World = World, Class = Class, Level = Level };
    public static SavedSchedulerCharacter From(CharacterSummary character)
        => new(character.Ocid, character.Name, character.World, character.Class, character.Level);
    public static bool Valid(SavedSchedulerCharacter? character) => character is not null
        && character.Ocid is { Length: > 0 and <= 200 } && character.Name is { Length: > 0 and <= 50 }
        && character.World is { Length: <= 50 } && character.Class is { Length: <= 100 } && character.Level is >= 0 and <= 300;
}

public sealed record SchedulerCharacterMigration(string OldOcid, string NewOcid);

public static class SchedulerCharacterRetention
{
    public static bool Remember(AppSettings settings, IEnumerable<CharacterSummary> characters)
    {
        settings.SchedulerCharacters ??= [];
        var changed = false;
        foreach (var character in characters.Where(character => settings.SchedulerOcids.Contains(character.Ocid)
            && !string.IsNullOrWhiteSpace(character.Ocid) && !string.IsNullOrWhiteSpace(character.Name)))
        {
            var saved = SavedSchedulerCharacter.From(character);
            var index = settings.SchedulerCharacters.FindIndex(item => item.Ocid == character.Ocid);
            if (index < 0) { settings.SchedulerCharacters.Add(saved); changed = true; }
            else if (settings.SchedulerCharacters[index] != saved) { settings.SchedulerCharacters[index] = saved; changed = true; }
        }
        return changed;
    }

    public static bool Remember(AppSettings settings, SavedSchedulerCharacter character)
    {
        if (!settings.SchedulerOcids.Contains(character.Ocid) || !SavedSchedulerCharacter.Valid(character)) return false;
        settings.SchedulerCharacters ??= [];
        var index = settings.SchedulerCharacters.FindIndex(item => item.Ocid == character.Ocid);
        if (index < 0) { settings.SchedulerCharacters.Add(character); return true; }
        if (settings.SchedulerCharacters[index] == character) return false;
        settings.SchedulerCharacters[index] = character; return true;
    }

    public static IReadOnlyList<SchedulerCharacterMigration> Reconcile(AppSettings settings, IEnumerable<CharacterSummary> current)
    {
        settings.SchedulerCharacters ??= [];
        var roster = current.Where(character => !string.IsNullOrWhiteSpace(character.Ocid) && !string.IsNullOrWhiteSpace(character.Name))
            .DistinctBy(character => character.Ocid).ToArray();
        var currentOcids = roster.Select(character => character.Ocid).ToHashSet(StringComparer.Ordinal);
        var migrations = new List<SchedulerCharacterMigration>();
        foreach (var saved in settings.SchedulerCharacters.Where(saved => settings.SchedulerOcids.Contains(saved.Ocid)
            && !currentOcids.Contains(saved.Ocid)).ToArray())
        {
            var replacement = roster.FirstOrDefault(character => string.Equals(character.Name, saved.Name, StringComparison.Ordinal));
            if (replacement is null || replacement.Ocid == saved.Ocid) continue;
            Migrate(settings, saved.Ocid, replacement.Ocid);
            migrations.Add(new(saved.Ocid, replacement.Ocid));
        }
        Remember(settings, roster);
        settings.SchedulerCharacters = settings.SchedulerCharacters.Where(SavedSchedulerCharacter.Valid)
            .DistinctBy(character => character.Ocid).ToList();
        return migrations;
    }

    public static void Migrate(AppSettings settings, string oldOcid, string newOcid)
    {
        if (string.IsNullOrWhiteSpace(oldOcid) || string.IsNullOrWhiteSpace(newOcid) || oldOcid == newOcid) return;
        settings.SchedulerOcids = settings.SchedulerOcids.Select(ocid => ocid == oldOcid ? newOcid : ocid).Distinct(StringComparer.Ordinal).ToList();
        settings.ManualWeeklyClears = settings.ManualWeeklyClears.Select(item => item.Ocid == oldOcid ? item with { Ocid = newOcid } : item)
            .DistinctBy(ManualWeeklyHistory.Key).ToList();
        settings.BossClearChanges = settings.BossClearChanges.Select(item => item.Ocid == oldOcid ? item with
            { Ocid = newOcid, Replacement = item.Replacement is { } replacement ? replacement with { Ocid = newOcid } : null } : item)
            .DistinctBy(item => item.Ocid + "|" + item.OriginalId).ToList();
        settings.BossLootRecords = settings.BossLootRecords.Select(item => item with
            {
                Ocid = item.Ocid == oldOcid ? newOcid : item.Ocid,
                Settlement = item.Settlement is { } settlement ? settlement with
                    { Members = settlement.Members.Select(member => member.Ocid == oldOcid ? member with { Ocid = newOcid } : member).ToArray() } : null
            })
            .DistinctBy(item => item.Id).ToList();
        settings.HuntingIncomeRecords = settings.HuntingIncomeRecords.Select(item => item.Ocid == oldOcid ? item with { Ocid = newOcid } : item)
            .DistinctBy(item => item.Id).ToList();
        settings.CalendarSchedules = settings.CalendarSchedules.Select(item => item with
            {
                Ocid = item.Ocid == oldOcid ? newOcid : item.Ocid,
                Party = item.Party is { } party ? party with
                    { Members = party.Members.Select(member => member.Ocid == oldOcid ? member with { Ocid = newOcid } : member).ToArray() } : null
            })
            .DistinctBy(item => item.Id).ToList();
        settings.Parties = settings.Parties.Select(party => party with
            { Members = party.Members.Select(member => member.Ocid == oldOcid ? member with { Ocid = newOcid } : member).ToArray() }).ToList();
        settings.SharedPartyLinks = settings.SharedPartyLinks.Select(link => link.LocalOcid == oldOcid ? link with { LocalOcid = newOcid } : link).ToList();
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in settings.BossPartySizes)
        {
            var key = pair.Key.StartsWith(oldOcid + "|", StringComparison.Ordinal) ? newOcid + pair.Key[oldOcid.Length..] : pair.Key;
            if (!sizes.ContainsKey(key) || !pair.Key.StartsWith(oldOcid + "|", StringComparison.Ordinal)) sizes[key] = pair.Value;
        }
        settings.BossPartySizes = sizes;
        settings.SchedulerCharacters.RemoveAll(character => character.Ocid == oldOcid);
    }
}
