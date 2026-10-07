using System.Net;
using System.Text;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class NexonApiClientTests
{
    [Fact]
    public async Task ListIncludesEveryAccountAndSendsKeyOnlyInHeader()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("https://open.api.nexon.com/maplestory/v1/character/list", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-nxopen-api-key")));
            Assert.DoesNotContain("test-key", request.RequestUri.AbsoluteUri);
            return Task.FromResult(Json(HttpStatusCode.OK, """
                {"account_list":[
                  {"account_id":"a","character_list":[{"ocid":"one","character_name":"첫캐릭터","world_name":"스카니아","character_class":"아델","character_level":285}]},
                  {"account_id":"b","character_list":[{"ocid":"two","character_name":"두번째","world_name":"루나","character_class":"비숍","character_level":260}]}
                ]}
                """));
        });
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        var result = await client.GetCharactersAsync("test-key");
        Assert.Equal(2, result.Accounts.Count);
        Assert.Equal(2, result.Accounts.SelectMany(a => a.Characters).Count());
        Assert.Equal("첫캐릭터", result.Accounts[0].Characters[0].Name);
        Assert.Empty(http.DefaultRequestHeaders);
    }

    [Fact]
    public async Task BasicEscapesOcidAndPreservesApiExperiencePrecision()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("?ocid=id%26date%3Dbad", request.RequestUri!.Query);
            return Task.FromResult(Json(HttpStatusCode.OK, """
                {"date":null,"character_name":"캐릭터","world_name":"루나","character_class":"아델","character_level":285,
                 "character_exp_rate":"12.345","character_image":"https://example.com/character.png"}
                """));
        });
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        var result = await client.GetBasicAsync("id&date=bad", "test-key");
        Assert.Equal("12.345", result.ExpRate);
        Assert.Equal(285, result.Level);
        Assert.Null(result.Date);
        Assert.Equal("https://example.com/character.png", result.ImageUrl);
    }

    [Theory]
    [InlineData("OPENAPI00005")]
    [InlineData("OPENAPI00002")]
    public async Task InvalidKeysAndPermissionsReturnActionableErrors(string code)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            System.Text.Json.JsonSerializer.Serialize(new { error = new { name = code, message = "server-private-value" } }))));
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        var error = await Assert.ThrowsAsync<NexonApiException>(() => client.GetCharactersAsync("secret-test-value"));
        Assert.True(error.IsAuthenticationError);
        Assert.DoesNotContain("secret-test-value", error.Message);
        Assert.DoesNotContain("server-private-value", error.Message);
        Assert.Contains("API", error.Message);
    }

    [Fact]
    public async Task HistoricalBasicUsesDateAndPreservesLargeExperienceWhileEmptyDatesRemainEmpty()
    {
        var requests = 0;
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("?ocid=id%26bad&date=2023-12-21", request.RequestUri!.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-nxopen-api-key")));
            return Task.FromResult(Json(HttpStatusCode.OK, ++requests == 1
                ? """{"character_level":291,"character_exp":12345678901234567,"character_exp_rate":"12.345","character_date_create":"2023-01-01T00:00+09:00"}"""
                : """{"character_level":null,"character_exp":null}"""));
        });
        using var http = new HttpClient(handler);
        using var api = new NexonApiClient(http);
        var basic = await api.GetBasicAtAsync("id&bad", "test-key", ExperienceHistory.FirstDate);
        Assert.Equal(12345678901234567L, basic!.Exp);
        Assert.Equal("12.345", basic.ExpRate);
        Assert.Equal("2023-01-01T00:00+09:00", basic.CreatedDate);
        Assert.Null(await api.GetBasicAtAsync("id&bad", "test-key", ExperienceHistory.FirstDate));
    }

    [Fact]
    public async Task HtmlErrorBodyDoesNotLeakIntoUi()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        { Content = new StringContent("<html>private-server-details</html>", Encoding.UTF8, "text/html") }));
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        var error = await Assert.ThrowsAsync<NexonApiException>(() => client.GetCharactersAsync("test-key"));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.DoesNotContain("private-server-details", error.Message);
    }

    [Fact]
    public async Task RateLimitRetriesThenSucceeds()
    {
        var requests = 0;
        using var handler = new FakeHandler((_, _) => Task.FromResult(++requests == 1
            ? Json(HttpStatusCode.TooManyRequests, """{"error":{"name":"OPENAPI00007"}}""")
            : Json(HttpStatusCode.OK, """{"account_list":[]}""")));
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        Assert.Empty((await client.GetCharactersAsync("test-key")).Accounts);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task CancellationStopsAnInFlightRequest()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new FakeHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCharactersAsync("test-key", cts.Token));
    }

    [Fact]
    public async Task AFailedBasicRequestDoesNotPreventNextCharacter()
    {
        var requests = 0;
        using var handler = new FakeHandler((_, _) => Task.FromResult(++requests == 1
            ? Json(HttpStatusCode.BadRequest, """{"error":{"name":"OPENAPI00009"}}""")
            : Json(HttpStatusCode.OK, """{"character_name":"다음캐릭터","character_level":200,"character_exp_rate":"0.000"}""")));
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        var error = await Assert.ThrowsAsync<NexonApiException>(() => client.GetBasicAsync("first", "test-key"));
        Assert.False(error.IsAuthenticationError);
        Assert.Equal("다음캐릭터", (await client.GetBasicAsync("second", "test-key")).Name);
    }

    [Fact]
    public async Task SchedulerEscapesOcidAndKeepsTheKeyInTheHeader()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("/maplestory/v1/scheduler/character-state", request.RequestUri!.AbsolutePath);
            Assert.Equal("?ocid=id%26date%3Dbad", request.RequestUri.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-nxopen-api-key")));
            Assert.DoesNotContain("test-key", request.RequestUri.AbsoluteUri);
            return Task.FromResult(Json(HttpStatusCode.OK, """{"character_name":"캐릭터","daily_contents":[],"boss_contents":[]}"""));
        });
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        Assert.Equal("캐릭터", (await client.GetSchedulerAsync("id&date=bad", "test-key")).Name);
        Assert.Empty(http.DefaultRequestHeaders);
    }

    [Fact]
    public async Task MissingSchedulerRecordReturnsAnActionableError()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"character_name":null}""")));
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        var error = await Assert.ThrowsAsync<NexonApiException>(() => client.GetSchedulerAsync("ocid", "test-key"));
        Assert.Contains("접속", error.Message);
    }

    [Fact]
    public async Task DatedSchedulerRequestIncludesDateAndAllowsNoLoginResult()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal("?ocid=id%26date%3Dbad&date=2026-10-01", request.RequestUri!.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-nxopen-api-key")));
            return Task.FromResult(Json(HttpStatusCode.OK, """{"character_name":null}"""));
        });
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        Assert.Null(await client.GetSchedulerAtAsync("id&date=bad", "test-key", new(2026, 10, 1)));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task NullBasicFieldsAreReportedAsUnavailableInsteadOfCrashing()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"character_name":null,"character_class":null,"character_level":null,"character_exp_rate":null,"character_image":null}""")));
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        var error = await Assert.ThrowsAsync<NexonApiException>(() => client.GetBasicAsync("unavailable", "test-key"));
        Assert.Contains("아직 제공되지", error.Message);
        Assert.False(error.IsAuthenticationError);
    }

    [Fact]
    public async Task MultipleBasicRequestsAreInFlightTogether()
    {
        var active = 0;
        var peak = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHandler(async (_, token) =>
        {
            var current = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref peak, Math.Max(peak, current));
            if (current == 4) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Interlocked.Decrement(ref active);
            return Json(HttpStatusCode.OK, """{"character_level":200,"character_exp_rate":"0.000"}""");
        });
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => client.GetBasicAsync($"character-{i}", "test-key")));
        Assert.Equal(4, peak);
    }

    [Fact]
    public async Task SlidingWindowLimitsRequestStartsWithoutSerializingResponses()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var starts = new List<TimeSpan>();
        using var handler = new FakeHandler((_, _) =>
        {
            lock (starts) starts.Add(timer.Elapsed);
            return Task.FromResult(Json(HttpStatusCode.OK, """{"account_list":[]}"""));
        });
        using var http = new HttpClient(handler);
        using var client = new NexonApiClient(http, requestsPerSecond: 2);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.GetCharactersAsync("test-key")));
        Assert.Equal(3, starts.Count);
        Assert.True(starts[2] - starts[0] >= TimeSpan.FromMilliseconds(950));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }
}

