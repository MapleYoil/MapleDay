using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MapleDay.Core;

/// <summary>Keys are supplied per request and are never persisted or logged.</summary>
public sealed class NexonApiClient : IDisposable
{
    public static readonly Uri BaseUri = new("https://open.api.nexon.com/");
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly int _requestsPerSecond;
    private readonly Queue<DateTimeOffset> _requestTimes = new();

    public NexonApiClient(HttpClient? http = null, int requestsPerSecond = 500)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerSecond);
        _requestsPerSecond = requestsPerSecond;
    }

    public Task<CharacterListResponse> GetCharactersAsync(string apiKey, CancellationToken token = default)
        => GetAsync<CharacterListResponse>("maplestory/v1/character/list", apiKey, token);

    public Task<EventNoticeResponse> GetEventNoticesAsync(string apiKey, CancellationToken token = default)
        => GetAsync<EventNoticeResponse>("maplestory/v1/notice-event", apiKey, token);

    public Task<EventNotice> GetEventNoticeAsync(int noticeId, string apiKey, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(noticeId);
        return GetAsync<EventNotice>($"maplestory/v1/notice-event/detail?notice_id={noticeId}", apiKey, token);
    }

    public Task<CharacterBasic> GetBasicAsync(string ocid, string apiKey, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ocid);
        return GetAsync<CharacterBasic>($"maplestory/v1/character/basic?ocid={Uri.EscapeDataString(ocid)}", apiKey, token);
    }

    public async Task<CharacterBasic?> GetBasicAtAsync(string ocid, string apiKey, DateOnly date, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ocid);
        var formatted = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var basic = await GetAsync<CharacterBasic>($"maplestory/v1/character/basic?ocid={Uri.EscapeDataString(ocid)}&date={formatted}",
            apiKey, token, allowEmptyBasic: true).ConfigureAwait(false);
        return basic.Level is null ? null : basic;
    }

    public Task<SchedulerState> GetSchedulerAsync(string ocid, string apiKey, CancellationToken token = default, bool allowEmpty = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ocid);
        return GetAsync<SchedulerState>($"maplestory/v1/scheduler/character-state?ocid={Uri.EscapeDataString(ocid)}", apiKey, token, allowEmptyScheduler: allowEmpty);
    }

    public async Task<SchedulerState?> GetSchedulerAtAsync(string ocid, string apiKey, DateOnly date, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ocid);
        var formattedDate = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var state = await GetAsync<SchedulerState>($"maplestory/v1/scheduler/character-state?ocid={Uri.EscapeDataString(ocid)}&date={formattedDate}",
            apiKey, token, allowEmptyScheduler: true).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(state.Name) ? null : state;
    }

    private async Task<T> GetAsync<T>(string path, string apiKey, CancellationToken token, bool allowEmptyScheduler = false, bool allowEmptyBasic = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        for (var attempt = 0; ; attempt++)
        {
            await WaitForRequestSlotAsync(token).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, path));
            request.Headers.Add("x-nxopen-api-key", apiKey);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
            {
                var retryAfter = response.Headers.RetryAfter;
                var delay = retryAfter?.Delta
                    ?? (retryAfter?.Date - DateTimeOffset.UtcNow)
                    ?? TimeSpan.FromSeconds(attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 8)), token).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                string? code = null;
                try
                {
                    code = (await response.Content.ReadFromJsonAsync<ApiErrorResponse>(token).ConfigureAwait(false))?.Error?.Code;
                }
                catch (JsonException) { }
                // Do not surface raw server bodies, request headers, or the entered key.
                throw new NexonApiException(response.StatusCode, code, DescribeError(response.StatusCode, code));
            }

            try
            {
                var result = await response.Content.ReadFromJsonAsync<T>(token).ConfigureAwait(false)
                    ?? throw new JsonException("Empty API response.");
                if (!allowEmptyBasic && result is CharacterBasic { Level: null })
                    throw new NexonApiException(response.StatusCode, null, "캐릭터 기본 정보가 아직 제공되지 않았습니다. 잠시 후 다시 조회해주세요.");
                if (!allowEmptyScheduler && result is SchedulerState state && string.IsNullOrWhiteSpace(state.Name))
                    throw new NexonApiException(response.StatusCode, null, "조회할 스케줄러 기록이 없습니다. 캐릭터 접속 후 다시 조회해주세요.");
                return result;
            }
            catch (JsonException)
            {
                throw new NexonApiException(response.StatusCode, null, "응답을 읽을 수 없습니다. 잠시 후 다시 조회해주세요.");
            }
        }
    }

    private async Task WaitForRequestSlotAsync(CancellationToken token)
    {
        await _requestGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            while (true)
            {
                var now = DateTimeOffset.UtcNow;
                while (_requestTimes.TryPeek(out var oldest) && now - oldest >= TimeSpan.FromSeconds(1))
                    _requestTimes.Dequeue();
                if (_requestTimes.Count < _requestsPerSecond)
                {
                    _requestTimes.Enqueue(now);
                    return;
                }
                await Task.Delay(_requestTimes.Peek() + TimeSpan.FromSeconds(1) - now, token).ConfigureAwait(false);
            }
        }
        finally { _requestGate.Release(); }
    }

    private static string DescribeError(HttpStatusCode status, string? code) => code switch
    {
        "OPENAPI00002" => "이 API에 접근할 권한이 없습니다. 메이플스토리용 API 키와 이용 권한을 확인해주세요.",
        "OPENAPI00005" => "API 키가 올바르지 않습니다. 복사한 키를 다시 확인해주세요.",
        "OPENAPI00007" => "API 호출 한도에 도달했습니다. 잠시 후 다시 조회해주세요.",
        "OPENAPI00009" => "아직 캐릭터 데이터가 준비되지 않았습니다. 잠시 후 다시 조회해주세요.",
        "OPENAPI00010" or "OPENAPI00011" => "넥슨 API가 점검 중입니다. 점검이 끝난 뒤 다시 조회해주세요.",
        _ => status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API 키 또는 메이플스토리 조회 권한을 확인해주세요.",
            HttpStatusCode.TooManyRequests => "API 호출 한도에 도달했습니다. 잠시 후 다시 조회해주세요.",
            HttpStatusCode.NotFound => "API 주소를 찾을 수 없습니다. 서비스 상태를 확인해주세요.",
            >= HttpStatusCode.InternalServerError => "넥슨 API에 일시적인 문제가 있습니다. 잠시 후 다시 조회해주세요.",
            _ => $"조회 요청을 처리하지 못했습니다. (HTTP {(int)status})"
        }
    };

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _requestGate.Dispose();
    }
}
