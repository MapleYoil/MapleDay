using System.Text.Json;
using MapleDay.Core;
namespace MapleDay.Services;
public sealed record SyncedCharacter(string Ocid, int Position);

public static class SyncRecords
{
    private static void Add<T>(SyncDocument result, string kind, IEnumerable<T> items, Func<T, string> key)
    { foreach (var item in items) result.Records[kind + "/" + key(item)] = JsonSerializer.SerializeToElement(item); }
    public static SyncDocument Capture(AppSettings settings)
    {
        var result = SyncDocument.Empty;
        var sharedParties = settings.SharedPartyLinks.Select(link => link.PartyId).ToHashSet(StringComparer.Ordinal);
        Add(result, "hunting", settings.HuntingIncomeRecords, row => row.Ocid + "/" + row.Date.ToString("yyyy-MM-dd"));
        Add(result, "loot", settings.BossLootRecords.Where(row => string.IsNullOrEmpty(row.SharedPartyId)), row => row.Id);
        Add(result, "calendar", settings.CalendarSchedules, row => row.Id);
        Add(result, "party", settings.Parties.Where(row => !sharedParties.Contains(row.Id)), row => row.Id);
        Add(result, "clear", settings.ManualWeeklyClears, ManualWeeklyHistory.Key);
        Add(result, "change", settings.BossClearChanges, row => row.Ocid + "/" + row.OriginalId);
        Add(result, "size", settings.BossPartySizes, row => row.Key);
        Add(result, "character", settings.SchedulerOcids.Select((ocid, position) => new SyncedCharacter(ocid, position)), row => row.Ocid);
        return result;
    }
    public static SyncDocument CapturePartyShare(AppSettings settings)
    {
        var result = SyncDocument.Empty;
        Add(result, "party", settings.Parties, row => row.Id);
        Add(result, "loot", settings.BossLootRecords, row => row.Id);
        return result;
    }
    public static void ApplyPartyShare(AppSettings settings, SyncDocument document)
    {
        if (document.Version != 1 || document.Records is null || document.Records.Count > 10000
            || document.Records.Keys.Any(key => !key.StartsWith("party/", StringComparison.Ordinal) && !key.StartsWith("loot/", StringComparison.Ordinal)))
            throw new ArgumentException("공유 파티 기록 버전을 확인해주세요.");
        var parties = document.Records.Where(item => item.Key.StartsWith("party/", StringComparison.Ordinal))
            .Select(item => item.Value.Deserialize<PartyPreset>() ?? throw new ArgumentException("공유 파티 형식을 확인해주세요.")).ToArray();
        var loot = document.Records.Where(item => item.Key.StartsWith("loot/", StringComparison.Ordinal))
            .Select(item => item.Value.Deserialize<BossLootRecord>() ?? throw new ArgumentException("공유 물욕템 형식을 확인해주세요.")).ToArray();
        if (parties.Length > 1 || parties.Any(party => !PartySettlement.Valid(party) || party.Members.Any(member => member.Ocid.Length > 0))
            || loot.Any(record => parties.Length != 1 || record.SharedPartyId != parties[0].Id || !record.Ocid.StartsWith("shared-party:", StringComparison.Ordinal)
                || record.ClearId.Length > 0 || record.Settlement is null || record.Settlement.Members.Any(member => member.Ocid.Length > 0)
                || !BossLoot.Valid(record)))
            throw new ArgumentException("유효하지 않은 공유 파티 기록입니다.");
        var candidate = new AppSettings { Parties = parties.ToList(), BossLootRecords = loot.ToList() };
        var canonical = CapturePartyShare(candidate);
        if (canonical.Records.Count != document.Records.Count || canonical.Records.Keys.Except(document.Records.Keys).Any())
            throw new ArgumentException("중복되거나 잘못 연결된 공유 파티 기록입니다.");
        settings.Parties = candidate.Parties; settings.BossLootRecords = candidate.BossLootRecords;
    }
    public static void Apply(AppSettings settings, SyncDocument document)
    {
        if (document.Version != 1 || document.Records is null || document.Records.Count > 100000)
            throw new ArgumentException("동기화 기록 버전을 확인해주세요.");
        string[] allowed = ["hunting", "loot", "calendar", "party", "clear", "change", "size", "character"];
        if (document.Records.Keys.Any(key => !allowed.Contains(key.Split('/')[0])))
            throw new ArgumentException("새 형식의 기록입니다. 앱을 업데이트해주세요.");
        T[] Read<T>(string kind) => document.Records.Where(item => item.Key.StartsWith(kind + "/", StringComparison.Ordinal))
            .Select(item => item.Value.Deserialize<T>() ?? throw new ArgumentException("동기화 기록 형식을 확인해주세요.")).ToArray();
        var hunting = Read<HuntingIncomeRecord>("hunting"); var loot = Read<BossLootRecord>("loot"); var schedules = Read<CalendarSchedule>("calendar"); var parties = Read<PartyPreset>("party");
        var clears = Read<ManualWeeklyClear>("clear"); var changes = Read<BossClearChange>("change"); var sizes = Read<KeyValuePair<string, int>>("size"); var characters = Read<SyncedCharacter>("character");
        if (hunting.Any(row => !HuntingIncome.Valid(row)) || loot.Any(row => !BossLoot.Valid(row)) || schedules.Any(row => !ScheduleCalendar.Valid(row))
            || parties.Any(row => !PartySettlement.Valid(row)) || clears.Any(row => !ManualWeeklyHistory.Valid(row)) || sizes.Any(row => row.Value is < 1 or > 6 || string.IsNullOrWhiteSpace(row.Key) || row.Key.Length > 500)
            || characters.Any(row => string.IsNullOrWhiteSpace(row.Ocid) || row.Ocid.Length > 200 || row.Position is < 0 or > 10000) || changes.Any(row => string.IsNullOrWhiteSpace(row.Ocid) || row.Ocid.Length > 200 || string.IsNullOrWhiteSpace(row.OriginalId) || row.OriginalId.Length > 500
                || row.Replacement is { } replacement && (!ManualWeeklyHistory.Valid(replacement) || replacement.Ocid != row.Ocid)))
            throw new ArgumentException("유효하지 않은 동기화 기록입니다. 로컬 기록은 유지됩니다.");
        var candidate = new AppSettings { HuntingIncomeRecords = hunting.ToList(), BossLootRecords = loot.ToList(), CalendarSchedules = schedules.ToList(), Parties = parties.ToList(),
            ManualWeeklyClears = clears.ToList(), BossClearChanges = changes.ToList(), BossPartySizes = sizes.ToDictionary(row => row.Key, row => row.Value), SchedulerOcids = characters.OrderBy(row => row.Position).ThenBy(row => row.Ocid).Select(row => row.Ocid).Distinct().ToList() };
        // Keys identify the same logical record on every device; reject mismatched or duplicate IDs.
        var canonical = Capture(candidate);
        if (canonical.Records.Count != document.Records.Count || canonical.Records.Keys.Except(document.Records.Keys).Any())
            throw new ArgumentException("중복되거나 잘못 연결된 동기화 기록입니다.");
        try { _ = checked(hunting.Sum(row => row.Total) + loot.Sum(row => row.Received)); }
        catch (OverflowException) { throw new ArgumentException("수익 합계가 지원하는 범위를 초과했습니다."); }
        var sharedIds = settings.SharedPartyLinks.Select(link => link.PartyId).ToHashSet(StringComparer.Ordinal);
        var sharedLoot = settings.BossLootRecords.Where(row => !string.IsNullOrEmpty(row.SharedPartyId) && sharedIds.Contains(row.SharedPartyId)).ToArray();
        var sharedParties = settings.Parties.Where(row => sharedIds.Contains(row.Id)).ToArray();
        settings.HuntingIncomeRecords = candidate.HuntingIncomeRecords;
        settings.BossLootRecords = candidate.BossLootRecords.Where(row => string.IsNullOrEmpty(row.SharedPartyId)).Concat(sharedLoot).ToList();
        settings.CalendarSchedules = candidate.CalendarSchedules;
        settings.Parties = candidate.Parties.Where(row => !sharedIds.Contains(row.Id)).Concat(sharedParties).ToList();
        settings.ManualWeeklyClears = candidate.ManualWeeklyClears; settings.BossClearChanges = candidate.BossClearChanges; settings.BossPartySizes = candidate.BossPartySizes; settings.SchedulerOcids = candidate.SchedulerOcids;
    }
    public static bool Same(SyncDocument a, SyncDocument b) => a.Records.Count == b.Records.Count && a.Records.All(row => b.Records.TryGetValue(row.Key, out var other) && RecordSync.Same(row.Value, other));
}
