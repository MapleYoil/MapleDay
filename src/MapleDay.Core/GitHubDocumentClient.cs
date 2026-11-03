using System.Net;
using System.Net.Http.Headers;

namespace MapleDay.Core;

public sealed class GitHubDocumentClient : IDisposable
{
    public static readonly Uri UpdateHistoryUri = Raw("docs/update-history.json");
    public static readonly Uri PrivacyPolicyUri = Raw("docs/privacy-policy.txt");
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _cacheDirectory;

    public GitHubDocumentClient(HttpClient? http = null, string? cacheDirectory = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        _cacheDirectory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "github-documents");
    }

    public Task<string> UpdateHistoryAsync(bool allowCachedFallback, CancellationToken token)
        => DownloadAsync(UpdateHistoryUri, "update-history.json", 512 * 1024, allowCachedFallback, token);

    public Task<string> PrivacyPolicyAsync(bool allowCachedFallback, CancellationToken token)
        => DownloadAsync(PrivacyPolicyUri, "privacy-policy.txt", 256 * 1024, allowCachedFallback, token);

    private async Task<string> DownloadAsync(Uri uri, string cacheName, int maximumBytes, bool allowCachedFallback, CancellationToken token)
    {
        var cache = Path.Combine(_cacheDirectory, cacheName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("MapleDay-GitHub-Documents/1.0");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.Pragma.ParseAdd("no-cache");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentLength is { } length && length > maximumBytes)
                throw new HttpRequestException("GitHub 문서를 불러오지 못했어요.", null, response.StatusCode);
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (memory.Length + count > maximumBytes) throw new InvalidDataException("GitHub 문서가 너무 큽니다.");
                await memory.WriteAsync(buffer.AsMemory(0, count), token);
            }
            var content = System.Text.Encoding.UTF8.GetString(memory.ToArray()).TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("GitHub 문서가 비어 있습니다.");
            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(cache, content, token);
            return content;
        }
        catch (Exception error) when (allowCachedFallback && (error is HttpRequestException or IOException or InvalidDataException || error is OperationCanceledException && !token.IsCancellationRequested))
        {
            if (!File.Exists(cache) || new FileInfo(cache).Length is <= 0 or > 512 * 1024) throw;
            return await File.ReadAllTextAsync(cache, token);
        }
    }

    private static Uri Raw(string path) => new($"https://raw.githubusercontent.com/{AppUpdatePolicy.Repository}/refs/heads/main/{path}");
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
