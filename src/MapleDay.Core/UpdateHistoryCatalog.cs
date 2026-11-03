using System.Globalization;
using System.Text.Json;

namespace MapleDay.Core;

public sealed record UpdateHistoryEntry(Version Version, DateOnly Date, string Notes);

public static class UpdateHistoryCatalog
{
    public const int PageSize = 5;

    public static IReadOnlyList<UpdateHistoryEntry> Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("format").GetInt32() != 1)
            throw new InvalidDataException("업데이트 내역 형식이 올바르지 않습니다.");
        var values = root.GetProperty("entries");
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is 0 or > 200)
            throw new InvalidDataException("업데이트 내역 수가 올바르지 않습니다.");
        var result = new List<UpdateHistoryEntry>();
        var versions = new HashSet<Version>();
        foreach (var item in values.EnumerateArray())
        {
            if (!Version.TryParse(item.GetProperty("version").GetString(), out var version)
                || !DateOnly.TryParseExact(item.GetProperty("date").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                throw new InvalidDataException("업데이트 버전 또는 날짜가 올바르지 않습니다.");
            var notes = AppUpdatePolicy.NormalizeReleaseNotes(item.GetProperty("notes").GetString());
            if (string.IsNullOrWhiteSpace(notes) || !versions.Add(version))
                throw new InvalidDataException("업데이트 내역이 비어 있거나 중복됩니다.");
            result.Add(new(version, date, notes));
        }
        if (result.Zip(result.Skip(1)).Any(pair => pair.First.Version <= pair.Second.Version))
            throw new InvalidDataException("업데이트 내역이 최신순이 아닙니다.");
        return result;
    }

    public static IReadOnlyList<UpdateHistoryEntry> Between(IReadOnlyList<UpdateHistoryEntry> entries, Version current, Version latest)
        => entries.Where(entry => entry.Version > current && entry.Version <= latest).ToArray();

    public static int PageCount(IReadOnlyList<UpdateHistoryEntry> entries)
        => (entries.Count + PageSize - 1) / PageSize;

    public static IReadOnlyList<UpdateHistoryEntry> Page(IReadOnlyList<UpdateHistoryEntry> entries, int page)
    {
        if (page < 0 || page >= PageCount(entries)) return [];
        return entries.Skip(page * PageSize).Take(PageSize).ToArray();
    }

    public static string ToMarkdown(IEnumerable<UpdateHistoryEntry> entries)
        => string.Join("\n\n", entries.Select(entry => $"## {entry.Version.ToString(4)} · {entry.Date:yyyy.MM.dd}\n{entry.Notes}"));
}
