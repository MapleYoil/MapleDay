using System.Collections.Concurrent;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed record SchedulerHistoryResult(SchedulerState Current, IReadOnlyList<SchedulerSnapshot> Snapshots, DateOnly Today, int FailedDates, bool StorageFailed)
{
    public DateOnly? FallbackDate { get; init; }
}

public sealed class SchedulerHistoryLoader(NexonApiClient api, SchedulerHistoryStore store)
{
    public async Task<SchedulerHistoryResult> LoadAsync(string ocid, string apiKey, DateTimeOffset now, CancellationToken token)
    {
        var today = SchedulerBossHistory.KoreanToday(now);
        var storageFailed = 0;
        List<SchedulerSnapshot> cached;
        try { cached = await store.LoadAsync(apiKey, ocid, token); }
        catch (Exception error) when (StorageError(error)) { cached = []; storageFailed = 1; }
        SchedulerState? current;
        try
        {
            current = await api.GetSchedulerAsync(ocid, apiKey, token, allowEmpty: true);
            if (string.IsNullOrWhiteSpace(current.Name)) current = null;
        }
        catch (NexonApiException error) when (error.Code == "OPENAPI00009") { current = null; }
        var snapshots = new ConcurrentDictionary<DateOnly, SchedulerSnapshot>(cached.ToDictionary(snapshot => snapshot.Date));
        // Keep genuine same-day observations, but never write a projected previous-day state.
        var observed = current ?? snapshots.GetValueOrDefault(today)?.State;
        var live = WithCompletionEvidence(new SchedulerSnapshot(today, observed, false, now));
        snapshots[today] = live;
        await Save(live);
        var dates = new List<DateOnly>();
        for (var date = today.AddDays(-1); date >= today.AddDays(-14) && date >= SchedulerBossHistory.FirstDate; date = date.AddDays(-1))
            if (!snapshots.TryGetValue(date, out var existing) || !existing.Final) dates.Add(date);
        var failed = 0;
        await Parallel.ForEachAsync(dates, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (date, cancellation) =>
        {
            try
            {
                var state = await api.GetSchedulerAtAsync(ocid, apiKey, date, cancellation);
                var snapshot = WithCompletionEvidence(new SchedulerSnapshot(date, state, true, now));
                snapshots[date] = snapshot;
                await Save(snapshot);
            }
            catch (NexonApiException error) when (!error.IsAuthenticationError) { Interlocked.Increment(ref failed); }
            catch (HttpRequestException) { Interlocked.Increment(ref failed); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Interlocked.Increment(ref failed); }
        });
        DateOnly? fallbackDate = null;
        if (current is null)
        {
            var source = SchedulerLastState.Latest(snapshots.Values, today)
                ?? throw new NexonApiException(System.Net.HttpStatusCode.OK, null, "조회할 스케줄러 기록이 없습니다. 캐릭터 접속 후 다시 조회해주세요.");
            current = SchedulerLastState.Project(source, today);
            fallbackDate = source.Date;
        }
        return new(current, snapshots.Values.OrderBy(snapshot => snapshot.Date).ToArray(), today, failed, storageFailed != 0)
            { FallbackDate = fallbackDate };

        SchedulerSnapshot WithCompletionEvidence(SchedulerSnapshot snapshot) => snapshot with
        {
            ExtremeMonsterParkCompleted = SchedulerReminders.ExtremeMonsterParkComplete(snapshot.State)
                || snapshots.TryGetValue(snapshot.Date, out var previous)
                    && (previous.ExtremeMonsterParkCompleted || SchedulerReminders.ExtremeMonsterParkComplete(previous.State))
        };

        async Task Save(SchedulerSnapshot snapshot)
        {
            try { await store.SaveAsync(apiKey, ocid, snapshot, token); }
            catch (Exception error) when (StorageError(error)) { Interlocked.Exchange(ref storageFailed, 1); }
        }
    }

    private static bool StorageError(Exception error) => error is IOException or UnauthorizedAccessException or JsonException;
}
