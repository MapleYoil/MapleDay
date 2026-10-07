using System.Collections.Concurrent;
using System.Net;
using MapleDay.Core;

namespace MapleDay.Services;

public sealed record ExperienceHistoryResult(ExperienceValue? Current, IReadOnlyList<ExperienceSnapshot> Snapshots,
    DateOnly Today, DateOnly FinalDate, int Completed, int Requested, int FailedDates, bool StorageFailed);

public sealed class ExperienceHistoryLoader(NexonApiClient api, ExperienceHistoryStore store)
{
    public async Task<ExperienceHistoryResult> LoadAsync(string ocid, string key, DateTimeOffset now, CancellationToken token,
        IProgress<ExperienceHistoryResult>? progress = null)
    {
        var today = ExperienceHistory.Today(now);
        var finalDate = ExperienceHistory.LatestFinalDate(now);
        var storageFailed = false;
        IReadOnlyList<ExperienceSnapshot> cached;
        try { cached = await store.LoadAsync(key, ocid, token); }
        catch (Exception error) when (StorageError(error)) { cached = []; storageFailed = true; }
        var snapshots = new ConcurrentDictionary<DateOnly, ExperienceSnapshot>(cached.Where(snapshot => snapshot.Date <= today).ToDictionary(snapshot => snapshot.Date));
        var current = snapshots.Values.OrderByDescending(snapshot => snapshot.Date).FirstOrDefault(snapshot => snapshot.Value is not null)?.Value;
        var completed = 0; var failed = 0; var requested = 0;
        progress?.Report(Result());
        var basic = await api.GetBasicAsync(ocid, key, token);
        current = ExperienceValue.From(basic) ?? throw new InvalidDataException("현재 경험치 데이터가 없습니다.");
        snapshots[today] = new(today, current, false, now);
        await Save();
        var start = ExperienceHistory.StartDate(basic.CreatedDate);
        var dates = new List<DateOnly>();
        for (var date = finalDate; date >= start; date = date.AddDays(-1))
            if (!snapshots.TryGetValue(date, out var existing) || !existing.Final) dates.Add(date);
        requested = dates.Count;
        progress?.Report(Result());
        try
        {
            // Fetch the eight recent endpoints first so the seven-day average is
            // available while the initial backfill of older dates continues.
            await Fetch(dates.Where(date => date >= finalDate.AddDays(-7)).ToArray());
            await Save(); progress?.Report(Result());
            foreach (var batch in dates.Where(date => date < finalDate.AddDays(-7)).Chunk(64))
            {
                await Fetch(batch);
                await Save(); progress?.Report(Result());
            }
            return Result();
        }
        catch
        {
            // Partial successes survive cancellation. The app waits for this
            // writer before deleting data; never restart the entire backfill.
            await Save(CancellationToken.None);
            throw;
        }

        ExperienceHistoryResult Result() => new(current, snapshots.Values.OrderBy(snapshot => snapshot.Date).ToArray(),
            today, finalDate, completed, requested, failed, storageFailed);
        async Task Save(CancellationToken? saveToken = null)
        {
            try { await store.SaveAsync(key, ocid, snapshots.Values.ToArray(), saveToken ?? token); }
            catch (Exception error) when (StorageError(error)) { storageFailed = true; }
        }
        async Task Fetch(IReadOnlyList<DateOnly> batch)
        {
            await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token }, async (date, cancellation) =>
            {
                try
                {
                    var historical = await api.GetBasicAtAsync(ocid, key, date, cancellation);
                    if (historical?.Date is { Length: >= 10 } responseDate && responseDate[..10] != date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                        throw new InvalidDataException("조회 날짜와 응답 날짜가 다릅니다.");
                    var value = ExperienceValue.From(historical);
                    snapshots[date] = new(date, value, true, now);
                }
                catch (NexonApiException error) when (!error.IsAuthenticationError && error.StatusCode != HttpStatusCode.TooManyRequests)
                { Interlocked.Increment(ref failed); }
                catch (Exception error) when (error is HttpRequestException or InvalidDataException || error is OperationCanceledException && !cancellation.IsCancellationRequested)
                { Interlocked.Increment(ref failed); }
                Interlocked.Increment(ref completed);
            });
        }
    }
    private static bool StorageError(Exception error) => error is IOException or UnauthorizedAccessException;
}
