using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class IncrementalUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MapleDay-range-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _updates = Path.Combine(Path.GetTempPath(), "MapleDay-range-jobs-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] Paths = ["MapleDay.exe", "App/MapleDay.exe", "App/MapleDay.dll", "App/MapleDay.pri", "App/coreclr.dll", "App/Microsoft.UI.Xaml.dll", "App/update-files.txt"];
    private readonly List<UpdateFile> _files = [];
    private readonly MemoryStream _archive = new();
    private static readonly Version Version = new(1, 0, 36, 0);
    private readonly Uri _uri = new("https://github.com/MapleYoil/MapleDay/releases/download/v1.0.36.0/MapleDay-Update-1.0.36.0-x64.zip");
    public IncrementalUpdateTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App"));
        foreach (var path in Paths)
        {
            var data = path.EndsWith("update-files.txt") ? System.Text.Encoding.UTF8.GetBytes(string.Join('\n', Paths)) : System.Text.Encoding.UTF8.GetBytes("new content for " + path);
            using var zipped = new MemoryStream();
            using (var deflate = new DeflateStream(zipped, CompressionLevel.Fastest, true)) deflate.Write(data);
            _files.Add(new(path, data.Length, Convert.ToHexStringLower(SHA256.HashData(data)), _archive.Length, zipped.Length));
            _archive.Write(zipped.ToArray());
            File.WriteAllBytes(Path.Combine(_root, path), data);
        }
    }
    private UpdateManifest Manifest => new(Version, _uri, _archive.Length, _files);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<(long Start, long End)> Ranges { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var range = Assert.Single(request.Headers.Range!.Ranges);
            Ranges.Add((range.From!.Value, range.To!.Value));
            return Task.FromResult(response(request));
        }
    }
    private HttpResponseMessage RangeResponse(HttpRequestMessage request, bool corrupt = false)
    {
        var range = request.Headers.Range!.Ranges.Single();
        var data = _archive.ToArray().AsSpan((int)range.From!.Value, (int)(range.To!.Value - range.From.Value + 1)).ToArray();
        if (corrupt) data[0] ^= 255;
        var result = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data) };
        result.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.From.Value, range.To.Value, _archive.Length);
        return result;
    }
    [Fact]
    public async Task DownloadsOnlyChangedRangeAndLeavesInstalledFilesAndTimestampsUntouched()
    {
        var changed = _files[2];
        File.WriteAllText(Path.Combine(_root, changed.Path), "old content");
        var before = Paths.ToDictionary(path => path, path => File.GetLastWriteTimeUtc(Path.Combine(_root, path)));
        var handler = new Handler(request => RangeResponse(request));
        using var http = new HttpClient(handler);
        using var client = new IncrementalUpdateClient(http);
        var prepared = await client.PrepareAsync(Manifest, _root, _updates, null, default);
        Assert.Equal(1, prepared.ChangedFiles);
        Assert.Equal(changed.CompressedSize, prepared.DownloadSize);
        Assert.Equal((changed.Offset, changed.Offset + changed.CompressedSize - 1), Assert.Single(handler.Ranges));
        Assert.Equal("old content", File.ReadAllText(Path.Combine(_root, changed.Path)));
        Assert.True(await IncrementalUpdateClient.MatchesAsync(Path.Combine(prepared.Directory, "files", changed.Path), changed, default));
        foreach (var path in Paths) Assert.Equal(before[path], File.GetLastWriteTimeUtc(Path.Combine(_root, path)));
    }
    [Fact]
    public async Task RemovalOnlyTouchesPreviouslyManagedFilesAndPreservesUninstaller()
    {
        File.AppendAllText(Path.Combine(_root, "App/update-files.txt"), "\nApp/obsolete.dll\n");
        File.WriteAllText(Path.Combine(_root, "App/obsolete.dll"), "obsolete");
        Directory.CreateDirectory(Path.Combine(_root, "App/Uninstall"));
        File.WriteAllText(Path.Combine(_root, "App/Uninstall/unins000.exe"), "keep");
        var handler = new Handler(request => RangeResponse(request));
        using var http = new HttpClient(handler);
        using var client = new IncrementalUpdateClient(http);
        var prepared = await client.PrepareAsync(Manifest, _root, _updates, null, default);
        Assert.Equal(1, prepared.RemovedFiles);
        var plan = await File.ReadAllTextAsync(Path.Combine(prepared.Directory, "plan.txt"));
        Assert.Contains("D\tApp/obsolete.dll", plan);
        Assert.DoesNotContain("Uninstall", plan);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_root, "App/Uninstall/unins000.exe")));
    }
    [Fact]
    public async Task CompleteDownloadIsRejectedWhenServerIgnoresRange()
    {
        File.WriteAllText(Path.Combine(_root, "App/MapleDay.dll"), "old");
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(_archive.ToArray()) }));
        using var client = new IncrementalUpdateClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.PrepareAsync(Manifest, _root, _updates, null, default));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_root, "App/MapleDay.dll")));
        Assert.Empty(Directory.GetDirectories(_updates));
    }
    [Fact]
    public async Task WrongFileHashCannotReachReplacementPlan()
    {
        var invalid = Manifest with { Files = _files.Select((file, index) => index == 2 ? file with { Sha256 = new string('0', 64) } : file).ToArray() };
        var original = File.ReadAllBytes(Path.Combine(_root, "App/MapleDay.dll"));
        using var http = new HttpClient(new Handler(request => RangeResponse(request)));
        using var client = new IncrementalUpdateClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.PrepareAsync(invalid, _root, _updates, null, default));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_root, "App/MapleDay.dll")));
        Assert.Empty(Directory.GetDirectories(_updates));
    }
    [Theory]
    [InlineData("App/../secret")]
    [InlineData("App\\file.dll")]
    [InlineData("App/file.dll:stream")]
    [InlineData("App/Uninstall/unins000.exe")]
    [InlineData("App/CON.txt")]
    [InlineData("App/.update-job")]
    [InlineData("App/trailing.")]
    public void RejectsPathsOutsideManagedFiles(string path) => Assert.False(IncrementalUpdatePolicy.IsManagedPath(path));
    [Fact]
    public void RejectsMissingRuntimeAndOverlappingRanges()
    {
        string Json(IEnumerable<UpdateFile> files) => JsonSerializer.Serialize(new { format = 1, version = Version.ToString(), archive = Path.GetFileName(_uri.LocalPath), archive_size = _archive.Length,
            files = files.Select(file => new { path = file.Path, size = file.Size, sha256 = file.Sha256, offset = file.Offset, compressed_size = file.CompressedSize }) });
        var update = new AppUpdate(Version, AppUpdatePolicy.ManifestName(Version), new Uri(_uri, AppUpdatePolicy.ManifestName(Version)), "a".PadRight(64, 'a'), 100);
        Assert.Equal(_files.Count, IncrementalUpdatePolicy.Parse(Json(_files), update).Files.Count);
        Assert.Throws<InvalidDataException>(() => IncrementalUpdatePolicy.Parse(Json(_files.Skip(1)), update));
        Assert.Throws<InvalidDataException>(() => IncrementalUpdatePolicy.Parse(Json(_files.Select((file, i) => i == 1 ? file with { Offset = 0 } : file)), update));
    }
    public void Dispose() { _archive.Dispose(); Directory.Delete(_root, true); if (Directory.Exists(_updates)) Directory.Delete(_updates, true); }
}
