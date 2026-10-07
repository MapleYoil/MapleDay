using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class ExperienceHistoryTests
{
    private static HttpResponseMessage Basic(DateOnly? date, string created = "2023-12-21T00:00+09:00", bool empty = false) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            date = date?.ToString("yyyy-MM-dd") + (date is null ? "" : "T00:00+09:00"),
            character_name = empty ? null : "캐릭터", character_level = empty ? (int?)null : 291,
            character_exp = empty ? (long?)null : 10_000_000_000_000L, character_exp_rate = empty ? null : "10.000",
            character_date_create = created
        }), Encoding.UTF8, "application/json")
    };
    private static DateOnly? QueryDate(HttpRequestMessage request)
    {
        var value = request.RequestUri!.Query.Split('&').FirstOrDefault(part => part.StartsWith("date=", StringComparison.Ordinal));
        return value is null ? null : DateOnly.Parse(value[5..]);
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "MapleDay.Experience.Tests." + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AllSupportedDatesAreFetchedOnceIncludingEmptyDatesThenOnlyLiveAndNewDays()
    {
        var directory = Temporary();
        try
        {
            var queries = new ConcurrentBag<string>();
            using var http = new HttpClient(new Handler((request, _) =>
            {
                queries.Add(request.RequestUri!.Query);
                var date = QueryDate(request);
                return Task.FromResult(Basic(date, empty: date == new DateOnly(2023, 12, 22)));
            }));
            using var api = new NexonApiClient(http);
            var store = new ExperienceHistoryStore(directory);
            var now = DateTimeOffset.Parse("2023-12-25T12:00:00+09:00");
            var first = await new ExperienceHistoryLoader(api, store).LoadAsync("ocid", "secret-key", now, default);
            Assert.Equal(5, queries.Count);
            Assert.Equal(4, first.Snapshots.Count(snapshot => snapshot.Final));
            Assert.Null(first.Snapshots.Single(snapshot => snapshot.Date == new DateOnly(2023, 12, 22)).Value);
            Assert.Single(queries, query => !query.Contains("date="));
            var live = Assert.Single(first.Snapshots, snapshot => snapshot.Date == first.Today);
            Assert.False(live.Final);
            Assert.Equal(first.Current, live.Value);
            Assert.Equal(0m, ExperienceHistory.TodayGain(first.Snapshots, first.Today).Gained);
            await new ExperienceHistoryLoader(api, store).LoadAsync("ocid", "secret-key", now, default);
            Assert.Equal(6, queries.Count);
            await new ExperienceHistoryLoader(api, store).LoadAsync("ocid", "secret-key", now.AddDays(1), default);
            Assert.Equal(8, queries.Count);
            Assert.Single(queries, query => query.Contains("date=2023-12-25"));
            Assert.Empty(await store.LoadAsync("other-key", "ocid", default));
            Assert.Empty(await store.LoadAsync("secret-key", "other-id", default));
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
                Assert.DoesNotContain("secret-key", await File.ReadAllTextAsync(file));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailuresAreRetriedButSuccessfulNoDataDaysAreNotRepeated()
    {
        var directory = Temporary();
        try
        {
            var failing = new DateOnly(2023, 12, 23);
            var attempts = new ConcurrentDictionary<DateOnly, int>();
            using var http = new HttpClient(new Handler((request, _) =>
            {
                var date = QueryDate(request);
                if (date is { } day)
                {
                    var attempt = attempts.AddOrUpdate(day, 1, (_, value) => value + 1);
                    if (day == failing && attempt == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }
                return Task.FromResult(Basic(date, empty: date == new DateOnly(2023, 12, 22)));
            }));
            using var api = new NexonApiClient(http);
            var store = new ExperienceHistoryStore(directory);
            var now = DateTimeOffset.Parse("2023-12-25T12:00:00+09:00");
            Assert.Equal(1, (await new ExperienceHistoryLoader(api, store).LoadAsync("id", "key", now, default)).FailedDates);
            var second = await new ExperienceHistoryLoader(api, store).LoadAsync("id", "key", now, default);
            Assert.Equal(0, second.FailedDates);
            Assert.Equal(1, second.Requested);
            Assert.Equal(2, attempts[failing]);
            Assert.Equal(1, attempts[new(2023, 12, 22)]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancellationKeepsRecentEightDaysAndNextLaunchResumesOlderMissingDates()
    {
        var directory = Temporary();
        try
        {
            using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Basic(QueryDate(request)))));
            using var api = new NexonApiClient(http);
            var store = new ExperienceHistoryStore(directory);
            using var cancellation = new CancellationTokenSource();
            var progress = new ProgressCapture(result => { if (result.Completed == 8) cancellation.Cancel(); });
            var now = DateTimeOffset.Parse("2024-01-10T12:00:00+09:00");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new ExperienceHistoryLoader(api, store).LoadAsync("id", "key", now, cancellation.Token, progress));
            Assert.Equal(8, (await store.LoadAsync("key", "id", default)).Count(snapshot => snapshot.Final));
            var resumed = await new ExperienceHistoryLoader(api, store).LoadAsync("id", "key", now, default);
            Assert.Equal(12, resumed.Requested);
            Assert.Equal(20, resumed.Snapshots.Count(snapshot => snapshot.Final));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CreationBoundsHistoryAndTwoAmControlsYesterdayFinalization()
    {
        var directory = Temporary();
        try
        {
            var queries = new ConcurrentBag<DateOnly>();
            using var http = new HttpClient(new Handler((request, _) =>
            {
                var date = QueryDate(request);
                if (date is { } day) queries.Add(day);
                return Task.FromResult(Basic(date, "2026-10-01T15:00+09:00"));
            }));
            using var api = new NexonApiClient(http);
            var loader = new ExperienceHistoryLoader(api, new(directory));
            var before = DateTimeOffset.Parse("2026-10-07T01:59:00+09:00");
            await loader.LoadAsync("id", "key", before, default);
            Assert.Equal(5, queries.Count);
            Assert.DoesNotContain(new DateOnly(2026, 10, 6), queries);
            var after = await loader.LoadAsync("id", "key", before.AddMinutes(1), default);
            Assert.Equal(1, after.Requested);
            Assert.Contains(new DateOnly(2026, 10, 6), queries);
            Assert.All(queries, date => Assert.True(date >= new DateOnly(2026, 10, 1)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AResponseForWrongDateIsNotSavedAsRequestedDate()
    {
        var directory = Temporary();
        try
        {
            using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Basic(QueryDate(request) is null ? null : ExperienceHistory.FirstDate))));
            using var api = new NexonApiClient(http);
            var result = await new ExperienceHistoryLoader(api, new(directory)).LoadAsync("id", "key",
                DateTimeOffset.Parse("2023-12-25T12:00:00+09:00"), default);
            Assert.Equal(3, result.FailedDates);
            Assert.Single(result.Snapshots, snapshot => snapshot.Final);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CorruptedCacheCanBeReplacedAndNoTemporaryFilesRemain()
    {
        var directory = Temporary();
        try
        {
            var store = new ExperienceHistoryStore(directory);
            await store.SaveAsync("key", "id", [new(ExperienceHistory.FirstDate, new(291, 12345678901234567L, 12.345m), true, DateTimeOffset.UtcNow)], default);
            var path = Assert.Single(Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories));
            await File.WriteAllTextAsync(path, "broken JSON");
            Assert.Empty(await store.LoadAsync("key", "id", default));
            await store.SaveAsync("key", "id", [new(ExperienceHistory.FirstDate, new(291, 12345678901234567L, 12.345m), true, DateTimeOffset.UtcNow)], default);
            Assert.Equal(12345678901234567L, Assert.Single(await store.LoadAsync("key", "id", default)).Value!.Exp);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class ProgressCapture(Action<ExperienceHistoryResult> action) : IProgress<ExperienceHistoryResult>
    { public void Report(ExperienceHistoryResult value) => action(value); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(request, token); }
}
