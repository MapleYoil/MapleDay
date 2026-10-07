using MapleDay.Core;

namespace MapleDay.Services;

public sealed record CharacterDetailsUpdate(string Ocid, CharacterBasic? Basic, CroppedCharacterImage? Image,
    bool ImageFailed, string? Error, DateTimeOffset? UpdatedAt)
{
    public CachedCharacter Merge(CachedCharacter saved) => Basic is null ? saved : saved with
    { Basic = Basic, Image = Image ?? (ImageFailed ? saved.Image : null), ImageFailed = ImageFailed, UpdatedAt = UpdatedAt };
}

public sealed class CharacterDetailsLoader(NexonApiClient api, CharacterImageLoader images)
{
    public async Task RefreshAsync(string key, IEnumerable<string> ocids, IReadOnlyCollection<string>? registered,
        Func<CharacterDetailsUpdate, Task> apply, CancellationToken token)
    {
        var selected = registered?.ToHashSet(StringComparer.Ordinal);
        var targets = ocids.Distinct(StringComparer.Ordinal).Where(ocid => selected is null || selected.Contains(ocid)).ToArray();
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = token }, async (ocid, ct) =>
        {
            CharacterBasic? basic = null;
            CroppedCharacterImage? image = null;
            var imageFailed = false;
            string? error = null;
            DateTimeOffset? updatedAt = null;
            try { basic = await api.GetBasicAsync(ocid, key, ct); updatedAt = DateTimeOffset.UtcNow; }
            catch (NexonApiException ex) when (!ex.IsAuthenticationError) { error = ex.Message; }
            catch (HttpRequestException) { error = "연결 실패 · 새로고침으로 다시 조회해주세요"; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { error = "응답 시간 초과 · 새로고침으로 다시 조회해주세요"; }
            if (basic is not null)
            {
                try { image = await images.LoadAsync(basic.ImageUrl, ct); }
                catch (Exception ex) when (ex is HttpRequestException or IOException
                    or System.Runtime.InteropServices.COMException or ArgumentException or OverflowException)
                { imageFailed = true; }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { imageFailed = true; }
            }
            ct.ThrowIfCancellationRequested();
            await apply(new(ocid, basic, image, imageFailed, error, updatedAt));
        });
    }
}
