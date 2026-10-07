using System.Collections.Concurrent;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed record SchedulerHistoryResult(SchedulerState Current, IReadOnlyList<SchedulerSnapshot> Snapshots, DateOnly Today, int FailedDates, bool StorageFailed);

public sealed class SchedulerHistoryLoader(NexonApiClient api, SchedulerHistoryStore store)
{
    public async Task<SchedulerHistoryResult> LoadAsync(string ocid, string apiKey, DateTimeOffset now, CancellationToken token)
    {
        var today = SchedulerBossHistory.KoreanToday(now);
        var storageFailed = 0;
        List<SchedulerSnapshot> cached;
        try { cached = await store.LoadAsync(apiKey, ocid, token); }
        catch (Exception error) when (StorageError(error)) { cached = []; storageFailed = 1; }
        var current = await api.GetSchedulerAsync(ocid, apiKey, token);
        var snapshots = new ConcurrentDictionary<DateOnly, SchedulerSnapshot>(cached.ToDictionary(snapshot => snapshot.Date));
        var live = WithCompletionEvidence(new SchedulerSnapshot(today, current, false, now));
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
        return new(current, snapshots.Values.OrderBy(snapshot => snapshot.Date).ToArray(), today, failed, storageFailed != 0);

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
