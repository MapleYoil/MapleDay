using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed class SchedulerHistoryStore(string? root = null)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "scheduler-history");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string DirectoryFor(string apiKey, string ocid) => Path.Combine(_root, Hash(apiKey), Hash(ocid));

    public async Task<List<SchedulerSnapshot>> LoadAsync(string apiKey, string ocid, CancellationToken token)
    {
        var directory = DirectoryFor(apiKey, ocid);
        var result = new List<SchedulerSnapshot>();
        if (!Directory.Exists(directory)) return result;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var snapshot = JsonSerializer.Deserialize<SchedulerSnapshot>(await File.ReadAllTextAsync(path, token));
                if (snapshot is not null && Path.GetFileNameWithoutExtension(path) == snapshot.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)) result.Add(snapshot);
            }
            catch (JsonException) { /* Retry a damaged date from the API while it is available. */ }
        }
        return result;
    }

    public async Task SaveAsync(string apiKey, string ocid, SchedulerSnapshot snapshot, CancellationToken token)
    {
        var directory = DirectoryFor(apiKey, ocid);
        var path = Path.Combine(directory, snapshot.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".json");
        var gate = WriteLocks.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));
        await gate.WaitAsync(token);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            SchedulerSnapshot? previous = null;
            if (File.Exists(path))
                try { previous = JsonSerializer.Deserialize<SchedulerSnapshot>(await File.ReadAllTextAsync(path, token)); }
                catch (JsonException) { }
            snapshot = snapshot with
            {
                State = snapshot.State ?? previous?.State,
                ExtremeMonsterParkCompleted = snapshot.ExtremeMonsterParkCompleted || SchedulerReminders.ExtremeMonsterParkComplete(snapshot.State)
                    || previous is not null && (previous.ExtremeMonsterParkCompleted || SchedulerReminders.ExtremeMonsterParkComplete(previous.State))
            };
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { gate.Release(); }
        }
    }
}
