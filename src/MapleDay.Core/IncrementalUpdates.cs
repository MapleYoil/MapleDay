using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapleDay.Core;

public sealed record UpdateFile(string Path, long Size, string Sha256, long Offset, long CompressedSize);
public sealed record UpdateManifest(Version Version, Uri Archive, long ArchiveSize, IReadOnlyList<UpdateFile> Files);
public sealed record PreparedUpdate(string Directory, int ChangedFiles, int RemovedFiles, long DownloadSize);

public static class IncrementalUpdatePolicy
{
    public const string IndexPath = "App/update-files.txt";
    private static readonly string[] Required = ["MapleDay.exe", "App/MapleDay.exe", "App/MapleDay.dll", "App/MapleDay.pri", "App/coreclr.dll", "App/Microsoft.UI.Xaml.dll", IndexPath];
    public static bool IsManagedPath(string path)
    {
        if (path == "MapleDay.exe") return true;
        if (!path.StartsWith("App/", StringComparison.Ordinal) || path.Length > 220) return false;
        foreach (var component in path.Split('/'))
        {
            if (component.Length == 0 || component.StartsWith('.') || component.EndsWith('.') || component.EndsWith(' ')
                || component.Equals("Uninstall", StringComparison.OrdinalIgnoreCase)
                || component.Any(c => c < 32 || "\\:*?\"<>|".Contains(c))
                || Regex.IsMatch(component, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) return false;
        }
        return true;
    }

    public static string Resolve(string root, string path)
    {
        if (!IsManagedPath(path)) throw new InvalidDataException("업데이트 파일 경로가 올바르지 않습니다.");
        var current = Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("연결된 폴더에는 업데이트할 수 없습니다.");
        foreach (var component in path.Split('/'))
        {
            current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("연결된 파일에는 업데이트할 수 없습니다.");
        }
        return current;
    }

    public static UpdateManifest Parse(string json, AppUpdate update)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var expected = $"MapleDay-Update-{update.Version.ToString(4)}-x64.zip";
        if (root.GetProperty("format").GetInt32() != 1 || root.GetProperty("version").GetString() != update.Version.ToString(4)
            || root.GetProperty("archive").GetString() != expected) throw new InvalidDataException("업데이트 버전이 일치하지 않습니다.");
        var archiveSize = root.GetProperty("archive_size").GetInt64();
        if (archiveSize <= 0 || archiveSize > 512L * 1024 * 1024) throw new InvalidDataException("업데이트 크기를 확인하지 못했어요.");
        var files = new List<UpdateFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0, rangeEnd = 0;
        foreach (var item in root.GetProperty("files").EnumerateArray())
        {
            var file = new UpdateFile(item.GetProperty("path").GetString() ?? "", item.GetProperty("size").GetInt64(),
                item.GetProperty("sha256").GetString() ?? "", item.GetProperty("offset").GetInt64(), item.GetProperty("compressed_size").GetInt64());
            if (!IsManagedPath(file.Path) || !paths.Add(file.Path) || !Regex.IsMatch(file.Sha256, "^[a-f0-9]{64}$")
                || file.Size < 0 || file.Size > 256L * 1024 * 1024 || file.CompressedSize <= 0 || file.CompressedSize > archiveSize
                || file.Offset < rangeEnd || file.Offset > archiveSize - file.CompressedSize || files.Count >= 5000)
                throw new InvalidDataException("업데이트 파일 목록이 올바르지 않습니다.");
            rangeEnd = file.Offset + file.CompressedSize;
            expanded += file.Size;
            if (expanded > 1024L * 1024 * 1024) throw new InvalidDataException("업데이트 파일 크기가 너무 큽니다.");
            files.Add(file);
        }
        if (Required.Any(path => !paths.Contains(path)) || files.Any(file => files.Any(other => other != file && other.Path.StartsWith(file.Path + "/", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("앱 실행에 필요한 업데이트 파일이 없습니다.");
        return new(update.Version, new Uri(update.Download, expected), archiveSize, files);
    }
}

/// <summary>Uses GitHub HTTP ranges to retrieve only changed files. No installer process.</summary>
public sealed class IncrementalUpdateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public IncrementalUpdateClient(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    }
    public static async Task<bool> MatchesAsync(string file, UpdateFile expected, CancellationToken token)
    {
        if (!File.Exists(file)) return false;
        await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return input.Length == expected.Size && Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PreparedUpdate> PrepareAsync(UpdateManifest manifest, string root, string updates, IProgress<double>? progress, CancellationToken token)
    {
        var changed = new List<UpdateFile>();
        foreach (var file in manifest.Files)
            if (!await MatchesAsync(IncrementalUpdatePolicy.Resolve(root, file.Path), file, token)) changed.Add(file);
        var removed = new List<string>();
        var index = IncrementalUpdatePolicy.Resolve(root, IncrementalUpdatePolicy.IndexPath);
        var target = manifest.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(index))
        {
            if (new FileInfo(index).Length > 1024 * 1024) throw new InvalidDataException("기존 앱 파일 목록이 너무 큽니다.");
            foreach (var path in await File.ReadAllLinesAsync(index, token))
            {
                if (!IncrementalUpdatePolicy.IsManagedPath(path)) throw new InvalidDataException("기존 앱 파일 목록이 올바르지 않습니다.");
                if (!target.Contains(path) && File.Exists(IncrementalUpdatePolicy.Resolve(root, path))) removed.Add(path);
            }
        }
        if (removed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != removed.Count) throw new InvalidDataException("기존 앱 파일 목록이 중복됩니다.");
        Directory.CreateDirectory(updates);
        var job = Path.Combine(updates, "job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        var total = changed.Sum(file => file.CompressedSize);
        long downloaded = 0;
        try
        {
            foreach (var file in changed)
            {
                token.ThrowIfCancellationRequested();
                var destination = Path.Combine(job, "files", file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await DownloadFileAsync(manifest, file, destination, token);
                downloaded += file.CompressedSize;
                progress?.Report(total == 0 ? 100 : downloaded * 100.0 / total);
            }
            var plan = new StringBuilder();
            foreach (var file in changed) plan.AppendLine($"R\t{file.Path}\t{file.Sha256}\t{file.Size}");
            foreach (var path in removed) plan.AppendLine($"D\t{path}\t-\t0");
            // UTF-16LE is read by the native launcher without JSON/runtime DLLs.
            await File.WriteAllTextAsync(Path.Combine(job, "plan.txt"), plan.ToString(), Encoding.Unicode, token);
            await File.WriteAllTextAsync(Path.Combine(job, "root.txt"), Path.GetFullPath(root), Encoding.Unicode, token);
            var launcher = changed.Any(file => file.Path == "MapleDay.exe") ? Path.Combine(job, "files", "MapleDay.exe") : Path.Combine(root, "MapleDay.exe");
            File.Copy(launcher, Path.Combine(job, "worker.exe"));
            progress?.Report(100);
            return new(job, changed.Count, removed.Count, total);
        }
        catch { Directory.Delete(job, true); throw; }
    }

    private async Task DownloadFileAsync(UpdateManifest manifest, UpdateFile file, string destination, CancellationToken token)
    {
        var uri = manifest.Archive;
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            if (!AppUpdatePolicy.IsDownloadHost(uri)) throw new InvalidDataException("업데이트 다운로드 주소가 올바르지 않습니다.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("MapleDay-Updater/2.0");
            request.Headers.Range = new RangeHeaderValue(file.Offset, file.Offset + file.CompressedSize - 1);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location ?? throw new InvalidDataException("업데이트 다운로드 주소가 없습니다.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes" || range.From != file.Offset
                || range.To != file.Offset + file.CompressedSize - 1 || range.Length != manifest.ArchiveSize
                || (response.Content.Headers.ContentLength is { } length && length != file.CompressedSize))
                throw new InvalidDataException("변경된 파일만 다운로드할 수 없어요. 잠시 후 다시 시도해주세요.");
            await using var compressed = new MemoryStream();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    if (compressed.Length + count > file.CompressedSize) throw new InvalidDataException("업데이트 파일 크기가 일치하지 않습니다.");
                    await compressed.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }
            if (compressed.Length != file.CompressedSize) throw new InvalidDataException("업데이트 다운로드가 끝나지 않았어요.");
            compressed.Position = 0;
            await using (var deflate = new DeflateStream(compressed, CompressionMode.Decompress, leaveOpen: true))
            await using (var output = File.Create(destination))
            {
                var buffer = new byte[81920];
                int count;
                long expanded = 0;
                while ((count = await deflate.ReadAsync(buffer, token)) > 0)
                {
                    expanded += count;
                    if (expanded > file.Size) throw new InvalidDataException("업데이트 파일 크기가 일치하지 않습니다.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }
            if (!await MatchesAsync(destination, file, token)) throw new InvalidDataException("업데이트 파일의 SHA-256 검증에 실패했어요.");
            return;
        }
        throw new InvalidDataException("업데이트 다운로드 연결이 너무 많이 변경되었습니다.");
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
