using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed class ExperienceHistoryStore(string? root = null)
{
    private readonly string _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "experience-history");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string PathFor(string key, string ocid) => Path.Combine(_root, Hash(key), Hash(ocid) + ".json");
    public async Task<IReadOnlyList<ExperienceSnapshot>> LoadAsync(string key, string ocid, CancellationToken token)
    {
        var path = PathFor(key, ocid);
        if (!File.Exists(path)) return [];
        try
        {
            var snapshots = JsonSerializer.Deserialize<List<ExperienceSnapshot>>(await File.ReadAllTextAsync(path, token)) ?? [];
            if (snapshots.Any(snapshot => snapshot.Date < ExperienceHistory.FirstDate
                || snapshot.Value is { } value && (value.Level is < 1 or > 300 || value.Exp < 0 || value.Percent is < 0 or > 100)))
                return [];
            return snapshots.GroupBy(snapshot => snapshot.Date).Select(group => group.MaxBy(snapshot => snapshot.FetchedAt)!).ToArray();
        }
        catch (JsonException) { return []; }
    }
    public async Task SaveAsync(string key, string ocid, IEnumerable<ExperienceSnapshot> snapshots, CancellationToken token)
    {
        var path = PathFor(key, ocid);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshots.OrderBy(snapshot => snapshot.Date)), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
