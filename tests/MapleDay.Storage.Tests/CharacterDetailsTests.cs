using System.Collections.Concurrent;
using System.Net;
using System.Text;
using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class CharacterDetailsTests
{
    [Fact]
    public async Task AutomaticRefreshOnlyRequestsRegisteredCharactersAndPreservesOtherDatesAndFailedData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Character-Tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var oldDate = DateTimeOffset.Parse("2026-09-20T13:00:00+09:00");
            var initial = new[] { "registered", "failed", "other" }.Select(ocid => new CachedCharacter(
                new CharacterSummary { Ocid = ocid, Name = ocid, Level = 280 },
                new CharacterBasic { Name = ocid, Level = 280, ExpRate = "10" }, UpdatedAt: oldDate)).ToList();
            var store = new CharacterCacheStore(Path.Combine(directory, "characters.dat"));
            await store.SaveAsync("test-key", 2, initial, default);
            var cached = new ConcurrentDictionary<string, CachedCharacter>((await store.LoadAsync("test-key"))!.Characters.ToDictionary(character => character.Summary.Ocid));
            var requests = new ConcurrentBag<string>();
            using var http = new HttpClient(new Handler(request =>
            {
                Assert.Equal("/maplestory/v1/character/basic", request.RequestUri!.AbsolutePath);
                var ocid = request.RequestUri.Query[6..];
                requests.Add(ocid);
                return ocid == "failed" ? new(HttpStatusCode.InternalServerError) : Basic();
            }));
            using var api = new NexonApiClient(http);
            await new CharacterDetailsLoader(api, new()).RefreshAsync("test-key", ["registered", "failed", "other", "registered"],
                ["registered", "failed", "missing"], update =>
                { cached[update.Ocid] = update.Merge(cached[update.Ocid]); return Task.CompletedTask; }, default);
            Assert.Equal(new[] { "failed", "registered" }, requests.Order().ToArray());
            await store.SaveAsync("test-key", 2, cached.Values.ToList(), default);
            var restored = (await new CharacterCacheStore(Path.Combine(directory, "characters.dat")).LoadAsync("test-key"))!;
            Assert.Equal(3, restored.Characters.Count);
            Assert.Equal(2, restored.AccountCount);
            Assert.Equal(oldDate, restored.Characters.Single(character => character.Summary.Ocid == "other").UpdatedAt);
            Assert.Equal(oldDate, restored.Characters.Single(character => character.Summary.Ocid == "failed").UpdatedAt);
            Assert.Equal(280, restored.Characters.Single(character => character.Summary.Ocid == "failed").Basic!.Level);
            var updated = restored.Characters.Single(character => character.Summary.Ocid == "registered");
            Assert.Equal(291, updated.Basic!.Level);
            Assert.True(updated.UpdatedAt > oldDate);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExplicitRefreshStillRequestsEveryCharacter()
    {
        var count = 0;
        using var http = new HttpClient(new Handler(_ => { Interlocked.Increment(ref count); return Basic(); }));
        using var api = new NexonApiClient(http);
        await new CharacterDetailsLoader(api, new()).RefreshAsync("test-key", ["one", "two"], null,
            update => { Assert.NotNull(update.UpdatedAt); return Task.CompletedTask; }, default);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task EmptyRegistrationMakesNoRequests()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Unexpected API request")));
        using var api = new NexonApiClient(http);
        await new CharacterDetailsLoader(api, new()).RefreshAsync("test-key", ["one", "two"], [],
            _ => throw new InvalidOperationException("Unexpected update"), default);
    }

    [Fact]
    public async Task OldCachesUseTheirOriginalSaveDateAndNeverTreatRestoreAsAFreshUpdate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Character-Tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CharacterCacheStore(Path.Combine(directory, "characters.dat"));
            await store.SaveAsync("test-key", 1, [new(new CharacterSummary { Ocid = "basic" }, new CharacterBasic { Level = 280 }),
                new(new CharacterSummary { Ocid = "summary-only" })], default);
            var first = (await store.LoadAsync("test-key"))!;
            Assert.Equal(first.UpdatedAt, first.Characters.Single(character => character.Summary.Ocid == "basic").UpdatedAt);
            Assert.Null(first.Characters.Single(character => character.Summary.Ocid == "summary-only").UpdatedAt);
            await store.SaveAsync("test-key", 1, first.Characters, default);
            var second = (await store.LoadAsync("test-key"))!;
            Assert.Equal(first.UpdatedAt, second.Characters.Single(character => character.Summary.Ocid == "basic").UpdatedAt);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidKeyDoesNotProduceASuccessfulUpdate()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden)
        { Content = new StringContent("{\"error\":{\"name\":\"OPENAPI00002\"}}") }));
        using var api = new NexonApiClient(http);
        await Assert.ThrowsAsync<NexonApiException>(() => new CharacterDetailsLoader(api, new()).RefreshAsync("test-key", ["one"], ["one"],
            _ => throw new InvalidOperationException("Unexpected update"), default));
    }

    private static HttpResponseMessage Basic() => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"character_name\":\"캐릭터\",\"character_level\":291,\"character_exp_rate\":\"50\"}", Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(respond(request));
    }
}
