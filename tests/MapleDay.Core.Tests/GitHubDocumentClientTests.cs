using System.Net;
using System.Text;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class GitHubDocumentClientTests
{
    [Fact]
    public async Task FreshHistoryRequestBypassesAndReplacesCachedDocument()
    {
        var cache = Path.Combine(Path.GetTempPath(), "MapleDay-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "update-history.json"), "stale");
        var handler = new StubHandler("{\"format\":1,\"entries\":[]}");
        using var client = new GitHubDocumentClient(new HttpClient(handler), cache);

        var result = await client.UpdateHistoryAsync(allowCachedFallback: false, CancellationToken.None);

        Assert.Equal("{\"format\":1,\"entries\":[]}", result);
        Assert.Equal(result, await File.ReadAllTextAsync(Path.Combine(cache, "update-history.json")));
        Assert.True(handler.NoCache);
        Assert.True(handler.NoStore);
        Assert.True(handler.PragmaNoCache);
        Directory.Delete(cache, true);
    }

    [Fact]
    public async Task FailedFreshRequestDoesNotReuseCachedHistory()
    {
        var cache = Path.Combine(Path.GetTempPath(), "MapleDay-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "update-history.json"), "stale");
        using var client = new GitHubDocumentClient(new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable)), cache);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.UpdateHistoryAsync(allowCachedFallback: false, CancellationToken.None));
        Directory.Delete(cache, true);
    }

    [Fact]
    public async Task ManualDocumentViewCanUseCacheWhenGitHubIsTemporarilyUnavailable()
    {
        var cache = Path.Combine(Path.GetTempPath(), "MapleDay-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "privacy-policy.txt"), "cached policy");
        using var client = new GitHubDocumentClient(new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable)), cache);

        Assert.Equal("cached policy", await client.PrivacyPolicyAsync(allowCachedFallback: true, CancellationToken.None));
        Directory.Delete(cache, true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;
        public bool NoCache { get; private set; }
        public bool NoStore { get; private set; }
        public bool PragmaNoCache { get; private set; }

        public StubHandler(string content) : this(HttpStatusCode.OK, content) { }
        public StubHandler(HttpStatusCode status, string content = "") { _status = status; _content = content; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            NoCache = request.Headers.CacheControl?.NoCache == true;
            NoStore = request.Headers.CacheControl?.NoStore == true;
            PragmaNoCache = request.Headers.Pragma.Any(value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_content, Encoding.UTF8, "text/plain")
            });
        }
    }
}
