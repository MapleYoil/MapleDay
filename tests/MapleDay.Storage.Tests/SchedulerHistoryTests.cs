using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class SchedulerHistoryTests
{
    [Fact]
    public async Task FetchesAll14DaysOnceThenReusesFinalDatesAcrossRestarts()
    {
        var directory = Temporary();
        try
        {
            var queries = new ConcurrentBag<string>();
            using var handler = new Handler((request, _) =>
            {
                queries.Add(request.RequestUri!.Query);
                return Task.FromResult(Json("""{"character_name":"캐릭터","boss_contents":[]}"""));
            });
            using var http = new HttpClient(handler);
            using var api = new NexonApiClient(http);
            var now = DateTimeOffset.Parse("2026-10-06T03:00:00Z");
            var loader = new SchedulerHistoryLoader(api, new(directory));
            var first = await loader.LoadAsync("ocid", "test-key", now, default);
            Assert.Equal(15, queries.Count);
            Assert.Equal(14, first.Snapshots.Count(snapshot => snapshot.Final));
            Assert.DoesNotContain(queries, query => query.Contains("date=2026-10-06"));
            await new SchedulerHistoryLoader(api, new(directory)).LoadAsync("ocid", "test-key", now, default);
            Assert.Equal(16, queries.Count);
            Assert.Empty(await new SchedulerHistoryStore(directory).LoadAsync("other-key", "ocid", default));
            Assert.Empty(await new SchedulerHistoryStore(directory).LoadAsync("test-key", "other-ocid", default));
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
                Assert.DoesNotContain("test-key", await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task YesterdayIsFinalizedAndOlderSavedDatesAreRetained()
    {
        var directory = Temporary();
        try
        {
            var store = new SchedulerHistoryStore(directory);
            var yesterday = new DateOnly(2026, 10, 5);
            var state = new SchedulerState { Name = "캐릭터" };
            await store.SaveAsync("key", "id", new(yesterday, state, false, DateTimeOffset.UtcNow), default);
            await store.SaveAsync("key", "id", new(new(2026, 8, 1), state, true, DateTimeOffset.UtcNow), default);
            var queries = new ConcurrentBag<string>();
            using var handler = new Handler((request, _) => { queries.Add(request.RequestUri!.Query); return Task.FromResult(Json("""{"character_name":"캐릭터"}""")); });
            using var http = new HttpClient(handler);
            using var api = new NexonApiClient(http);
            var result = await new SchedulerHistoryLoader(api, store).LoadAsync("id", "key", DateTimeOffset.Parse("2026-10-06T03:00:00Z"), default);
            Assert.Contains(queries, query => query.Contains("date=2026-10-05"));
            Assert.True(result.Snapshots.Single(snapshot => snapshot.Date == yesterday).Final);
            Assert.Contains(result.Snapshots, snapshot => snapshot.Date == new DateOnly(2026, 8, 1));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LaunchDateBoundsRequestsAndFailedDaysAreRetriedWhileNoLoginDatesAreCached()
    {
        var directory = Temporary();
        try
        {
            var failures = 0;
            using var handler = new Handler((request, _) =>
            {
                var query = request.RequestUri!.Query;
                if (query.Contains("date=2026-06-25") && Interlocked.Increment(ref failures) == 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                if (query.Contains("date=")) return Task.FromResult(Json("""{"character_name":null}"""));
                return Task.FromResult(Json("""{"character_name":"캐릭터"}"""));
            });
            using var http = new HttpClient(handler);
            using var api = new NexonApiClient(http);
            var loader = new SchedulerHistoryLoader(api, new(directory));
            var now = DateTimeOffset.Parse("2026-06-27T03:00:00Z");
            var first = await loader.LoadAsync("id", "key", now, default);
            Assert.Equal(1, first.FailedDates);
            Assert.DoesNotContain(first.Snapshots, snapshot => snapshot.Date < SchedulerBossHistory.FirstDate);
            Assert.Contains(first.Snapshots, snapshot => snapshot.Date == new DateOnly(2026, 6, 26) && snapshot.Final && snapshot.State is null);
            var second = await loader.LoadAsync("id", "key", now, default);
            Assert.Equal(0, second.FailedDates);
            Assert.Equal(3, second.Snapshots.Count);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Temporary() => Path.Combine(Path.GetTempPath(), "MapleDay-History-Test-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task ExtremeCompletionIsRetainedAcrossZeroRefreshFinalizationAndWeekReset()
    {
        var directory = Temporary();
        try
        {
            var store = new SchedulerHistoryStore(directory);
            var day = new DateOnly(2026, 10, 7);
            var complete = new SchedulerState { World = "오로라", Weekly = [new()
                { Name = "[몬스터파크] 익스트림 몬스터파커에 도전해보겠나?", QuestState = "2", Registration = JsonSerializer.SerializeToElement(true) }] };
            await store.SaveAsync("key", "id", new(day, complete, false, DateTimeOffset.UtcNow), default);
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("""{"character_name":"캐릭터","world_name":"오로라","weekly_contents":[{"content_name":"[몬스터파크] 익스트림 몬스터파커에 도전해보겠나?","type":"quest","registration_flag":true,"quest_state":"0"}]}"""))));
            using var api = new NexonApiClient(http);
            var loader = new SchedulerHistoryLoader(api, store);
            var now = DateTimeOffset.Parse("2026-10-07T12:00:00+09:00");
            var refreshed = await loader.LoadAsync("id", "key", now, default);
            Assert.Equal("0", refreshed.Current.Weekly![0].QuestState);
            Assert.True(SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(refreshed.Snapshots, day, "오로라"));
            var tomorrow = await loader.LoadAsync("id", "key", now.AddDays(1), default);
            Assert.True(tomorrow.Snapshots.Single(snapshot => snapshot.Date == day).ExtremeMonsterParkCompleted);
            Assert.False(SchedulerReminders.ExtremeMonsterParkCompletedThisWeek(tomorrow.Snapshots, day.AddDays(1), "오로라"));
            // A stale concurrent writer must not discard observed completion.
            await store.SaveAsync("key", "id", new(day, refreshed.Current, true, now), default);
            Assert.True((await store.LoadAsync("key", "id", default)).Single(snapshot => snapshot.Date == day).ExtremeMonsterParkCompleted);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
