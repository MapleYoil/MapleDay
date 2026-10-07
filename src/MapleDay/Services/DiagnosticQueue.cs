using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

/// <summary>Crash-safe, bounded queue of allowlisted fields; contains no raw log.</summary>
public sealed class DiagnosticQueue(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "diagnostics");
    private readonly object _gate = new();
    private readonly HashSet<string> _seen = [];
    private bool _enabled = true;
    public void Suspend() { lock (_gate) _enabled = false; }
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _enabled = enabled;
            if (!enabled && Directory.Exists(_directory))
                foreach (var file in Directory.EnumerateFiles(_directory, "*.json")) TryDelete(file);
        }
    }
    public void Capture(Exception error, string category, Version version, bool fatal = false)
    {
        if (error is OperationCanceledException) return;
        lock (_gate)
        {
            if (!_enabled) return;
            try
            {
                var report = DiagnosticReport.Create(error, category, version, fatal);
                var key = DateTime.UtcNow.ToString("yyyy-MM-dd") + report.Fingerprint;
                if (!_seen.Add(key)) return;
                Directory.CreateDirectory(_directory);
                var files = Directory.EnumerateFiles(_directory, "*.json").ToArray();
                foreach (var file in files.Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30))) TryDelete(file);
                if (Directory.EnumerateFiles(_directory, "*.json").Take(100).Count() >= 100) return;
                var path = Path.Combine(_directory, report.Id + ".json");
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(report));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception) { /* Reporting must never cause another application failure. */ }
        }
    }
    public IReadOnlyList<(string Path, DiagnosticReport Report)> Pending()
    {
        lock (_gate)
        {
            var result = new List<(string, DiagnosticReport)>();
            if (!_enabled || !Directory.Exists(_directory)) return result;
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.Ordinal).Take(20))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30)) { TryDelete(file); continue; }
                    var report = JsonSerializer.Deserialize<DiagnosticReport>(File.ReadAllText(file));
                    if (report is not null) result.Add((file, report)); else TryDelete(file);
                }
                catch (Exception) { TryDelete(file); }
            }
            return result;
        }
    }
    public void Acknowledge(string path) { lock (_gate) TryDelete(path); }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (Exception) { } }
}
