using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class AppUpdateTests
{
    private static readonly byte[] Installer = "verified test installer"u8.ToArray();
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant();
    private static string Release(string version = "v1.0.34.0", bool draft = false, bool prerelease = false,
        string? url = null, string? digest = null, long? size = null, string? name = null) => JsonSerializer.Serialize(new
    {
        tag_name = version, draft, prerelease,
        assets = new[] { new { name = name ?? "MapleDay-Update-1.0.34.0-x64.json", state = "uploaded", size = size ?? Installer.Length,
            digest = digest ?? "sha256:" + Hash,
            browser_download_url = url ?? "https://github.com/MapleYoil/MapleDay/releases/download/v1.0.34.0/MapleDay-Update-1.0.34.0-x64.json" } }
    });
    private static AppUpdate Update() => AppUpdatePolicy.Parse(Release(), new Version(1, 0, 33, 0))!;

    [Fact]
    public void SelectsStableManifestWithExactVersionAndHash()
    {
        var result = Update();
        Assert.Equal(new Version(1, 0, 34, 0), result.Version);
        Assert.Equal(Hash, result.Sha256);
        Assert.Equal(Installer.Length, result.Size);
    }

    [Theory]
    [InlineData("v1.0.33.0")]
    [InlineData("v1.0.9.0")]
    [InlineData("v1.0.34.0-beta")]
    [InlineData("../../1.0.34.0")]
    public void NeverUpdatesToSameOlderOrInvalidVersions(string tag)
        => Assert.Null(AppUpdatePolicy.Parse(Release(version: tag), new Version(1, 0, 33, 0)));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SkipsDraftAndPrerelease(bool draft, bool prerelease)
        => Assert.Null(AppUpdatePolicy.Parse(Release(draft: draft, prerelease: prerelease), new Version(1, 0, 33, 0)));

    [Theory]
    [InlineData("https://github.com/Someone/MapleDay/releases/download/v1.0.34.0/MapleDay-Update-1.0.34.0-x64.json")]
    [InlineData("https://github.com.evil.test/MapleYoil/MapleDay/releases/download/v1.0.34.0/MapleDay-Update-1.0.34.0-x64.json")]
    [InlineData("http://github.com/MapleYoil/MapleDay/releases/download/v1.0.34.0/MapleDay-Update-1.0.34.0-x64.json")]
    [InlineData("https://github.com/MapleYoil/MapleDay/releases/download/v1.0.34.0/MapleDay-Update-1.0.34.0-x64.json?file=other")]
    public void RejectsUntrustedOrModifiedDownloadUrls(string url)
        => Assert.Throws<InvalidDataException>(() => AppUpdatePolicy.Parse(Release(url: url), new Version(1, 0, 33, 0)));

    [Theory]
    [InlineData("sha256:bad", 22)]
    [InlineData("md5:00000000000000000000000000000000", 22)]
    [InlineData("sha256:2151b604e3429bff440b9fbc03eb3617bc2603cda96c95b9bb05277f9ddba255", 0)]
    [InlineData("sha256:2151b604e3429bff440b9fbc03eb3617bc2603cda96c95b9bb05277f9ddba255", 209715201)]
    public void RequiresValidHashAndBoundedSize(string digest, long size)
        => Assert.Throws<InvalidDataException>(() => AppUpdatePolicy.Parse(Release(digest: digest, size: size), new Version(1, 0, 33, 0)));

    [Fact]
    public void DoesNotUseMSIXOrUnrelatedReleaseAssetsAsManifest()
        => Assert.Throws<InvalidDataException>(() => AppUpdatePolicy.Parse(Release(name: "MapleDay.msix"), new Version(1, 0, 33, 0)));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Requests++; return Task.FromResult(respond(request, token)); }
    }

    [Fact]
    public async Task DownloadsVerifiesAndReusesCachedInstaller()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new Handler((_, _) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) });
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            var file = await client.DownloadAsync(Update(), directory, null, default);
            Assert.Equal(Installer, await File.ReadAllBytesAsync(file));
            Assert.True(await AppUpdateClient.VerifyAsync(file, Update(), default));
            Assert.Equal(file, await client.DownloadAsync(Update(), directory, null, default));
            Assert.Equal(1, handler.Requests);
            Assert.Empty(Directory.GetFiles(directory, "*.part"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CorruptDownloadCannotReplacePreviouslyVerifiedFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var update = Update();
            var file = Path.Combine(directory, update.FileName);
            await File.WriteAllTextAsync(file, "old invalid cache");
            var corrupted = Installer.ToArray(); corrupted[0] ^= 1;
            using var http = new HttpClient(new Handler((_, _) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(corrupted) }));
            using var client = new AppUpdateClient(http);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(update, directory, null, default));
            Assert.Equal("old invalid cache", await File.ReadAllTextAsync(file));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RejectsRedirectToForeignHostBeforeRequestingIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new Handler((_, _) => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://evil.test/file.exe"); return response; });
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Update(), directory, null, default));
            Assert.Equal(1, handler.Requests);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancelledDownloadDoesNotLeavePartialInstaller()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cancellation = new CancellationTokenSource();
            var handler = new Handler((_, _) => { cancellation.Cancel(); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }; });
            using var http = new HttpClient(handler);
            using var client = new AppUpdateClient(http);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(Update(), directory, null, cancellation.Token));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
