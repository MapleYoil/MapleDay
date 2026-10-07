using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapleDay.Core;

public sealed record AppUpdate(Version Version, string FileName, Uri Download, string Sha256, long Size);

public static partial class AppUpdatePolicy
{
    public const long MaximumInstallerSize = 200 * 1024 * 1024;
    public const string Repository = "MapleYoil/MapleDay";
    public static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");
    [GeneratedRegex(@"^v?(\d+\.\d+\.\d+\.\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionTag();
    [GeneratedRegex(@"^sha256:([a-fA-F0-9]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex Digest();

    public static AppUpdate? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var match = VersionTag().Match(tag);
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version) || version <= current) return null;
        var filename = $"MapleDay-Setup-{version.ToString(4)}-x64.exe";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != filename || asset.GetProperty("state").GetString() != "uploaded") continue;
            var size = asset.GetProperty("size").GetInt64();
            var digest = asset.TryGetProperty("digest", out var value) ? Digest().Match(value.GetString() ?? "") : null;
            var expected = $"https://github.com/{Repository}/releases/download/{tag}/{filename}";
            if (size <= 0 || size > MaximumInstallerSize || digest?.Success != true
                || asset.GetProperty("browser_download_url").GetString() != expected) throw new InvalidDataException("업데이트 파일 정보를 확인하지 못했어요.");
            return new(version, filename, new Uri(expected), digest.Groups[1].Value.ToLowerInvariant(), size);
        }
        throw new InvalidDataException("새 버전의 x64 설치 파일이 아직 준비되지 않았어요.");
    }

    public static bool IsDownloadHost(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo) && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
}

/// <summary>Public GitHub requests only. No API keys or account credentials.</summary>
public sealed class AppUpdateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public AppUpdateClient(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<AppUpdate?> CheckAsync(Version current, CancellationToken token)
    {
        using var request = Request(AppUpdatePolicy.LatestRelease);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1024 * 1024, token);
        return AppUpdatePolicy.Parse(await response.Content.ReadAsStringAsync(token), current);
    }

    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("MapleDay-Updater/1.0");
        return request;
    }

    public static async Task<bool> VerifyAsync(string file, AppUpdate update, CancellationToken token)
    {
        if (!File.Exists(file)) return false;
        await using var stream = File.OpenRead(file);
        if (stream.Length != update.Size) return false;
        var hash = await SHA256.HashDataAsync(stream, token);
        return string.Equals(Convert.ToHexString(hash), update.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> DownloadAsync(AppUpdate update, string directory, IProgress<double>? progress, CancellationToken token)
    {
        var expectedPath = $"https://github.com/{AppUpdatePolicy.Repository}/releases/download/";
        var matchingUrl = update.Download.AbsoluteUri == $"{expectedPath}v{update.Version.ToString(4)}/{update.FileName}"
            || update.Download.AbsoluteUri == $"{expectedPath}{update.Version.ToString(4)}/{update.FileName}";
        if (!matchingUrl || !AppUpdatePolicy.IsDownloadHost(update.Download) || update.Size <= 0 || update.Size > AppUpdatePolicy.MaximumInstallerSize
            || update.FileName != $"MapleDay-Setup-{update.Version.ToString(4)}-x64.exe" || !Regex.IsMatch(update.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("업데이트 파일 정보가 올바르지 않습니다.");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, update.FileName);
        if (await VerifyAsync(file, update, token)) { progress?.Report(100); return file; }
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            var uri = update.Download;
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                using var request = Request(uri);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location ?? throw new InvalidDataException("업데이트 다운로드 주소가 없습니다.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (!AppUpdatePolicy.IsDownloadHost(uri)) throw new InvalidDataException("업데이트 다운로드 주소를 확인하지 못했어요.");
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != update.Size) throw new InvalidDataException("업데이트 파일 크기가 일치하지 않습니다.");
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, token)) > 0)
                    {
                        downloaded += count;
                        if (downloaded > update.Size) throw new InvalidDataException("업데이트 파일이 예상 크기를 초과했습니다.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                        progress?.Report(downloaded * 100.0 / update.Size);
                    }
                }
                if (!await VerifyAsync(temporary, update, token)) throw new InvalidDataException("업데이트 파일의 SHA-256 검증에 실패했어요.");
                token.ThrowIfCancellationRequested();
                File.Move(temporary, file, true);
                return file;
            }
            throw new InvalidDataException("업데이트 다운로드 연결이 너무 많이 변경되었습니다.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
