using System.Net.Http;
using System.Text.Json;
using Kaleidoscope.Models.FFXIVMT;
using OtterGui.Services;

namespace Kaleidoscope.Services.FFXIVMT;

/// <summary>
/// Service for querying the FFXIVMT API at mtvirux.app.
/// Provides access to Gilflux rankings.
/// </summary>
/// <remarks>
/// Rate limits: Conservative 5 req/s to avoid overloading the API.
/// API base: https://mtvirux.app/api/v1/
/// </remarks>
public sealed class FFXIVMTService : IDisposable, IService
{
    private const string BaseUrl = "https://mtvirux.app/api/v1/";
    private const string UserAgent = "Kaleidoscope-FFXIV-Plugin";
    private const int MaxRequestsPerSecond = 5;

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Queue<DateTime> _requestTimestamps = new();
    private readonly object _rateLimitLock = new();
    private volatile bool _disposed;

    public FFXIVMTService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(60) // These API calls can be slow
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        LogService.Debug(LogCategory.FFXIVMT, $"[FFXIVMTService] Initialized with rate limit of {MaxRequestsPerSecond} req/s");
    }

    /// <summary>
    /// Waits for rate limit before making a request. Uses a sliding window approach.
    /// </summary>
    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        TimeSpan waitTime;
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-1);

            while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < windowStart)
                _requestTimestamps.Dequeue();

            if (_requestTimestamps.Count >= MaxRequestsPerSecond)
            {
                var oldestInWindow = _requestTimestamps.Peek();
                waitTime = oldestInWindow.AddSeconds(1) - now;
            }
            else
            {
                waitTime = TimeSpan.Zero;
            }
        }

        if (waitTime > TimeSpan.Zero)
        {
            LogService.Debug(LogCategory.FFXIVMT, $"[FFXIVMTService] Rate limiting, waiting {waitTime.TotalMilliseconds:F0}ms");
            await Task.Delay(waitTime, cancellationToken);
        }

        lock (_rateLimitLock)
        {
            _requestTimestamps.Enqueue(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Fetches Gilflux ranking data for a given location.
    /// All items are returned — crafted-only filtering is handled client-side via ItemDataService.
    /// </summary>
    /// <param name="targetLocation">World name, DC name, or region name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>GilfluxResult with items and dynamic timeframe labels, or null on error.</returns>
    public async Task<GilfluxResult?> GetGilfluxAsync(
        string targetLocation,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var url = $"gilflux?target_location={Uri.EscapeDataString(targetLocation)}&crafted_only=0&request_id={requestId}";

        var response = await GetAsync<GilfluxResponse>(url, cancellationToken);
        if (response is not { Status: true, Data: not null })
        {
            if (response?.Message != null)
                LogService.Warning(LogCategory.FFXIVMT, $"[FFXIVMTService] Gilflux request rejected: {response.Message}");
            return null;
        }

        // Derive column labels and durations from gilflux_timeframe_in_ms (shortest first: 1h ... 7d)
        List<string> timeframeLabels;
        var timeframeDurations = new Dictionary<string, TimeSpan>();
        if (response.GilfluxTimeframeInMs is { Count: > 0 } timeframes)
        {
            timeframeLabels = timeframes.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
            foreach (var kv in timeframes)
                timeframeDurations[kv.Key] = TimeSpan.FromMilliseconds(kv.Value);
        }
        else
        {
            timeframeLabels = new List<string> { "1h", "3h", "6h", "12h", "1d", "3d", "7d" };
        }

        return new GilfluxResult
        {
            Items = response.Data,
            TimeframeLabels = timeframeLabels,
            TimeframeDurations = timeframeDurations,
        };
    }

    /// <summary>
    /// Generic GET request with rate limiting, logging, and error handling.
    /// </summary>
    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        if (_disposed)
            return null;

        try
        {
            await WaitForRateLimitAsync(cancellationToken);

            LogService.Debug(LogCategory.FFXIVMT, $"[FFXIVMTService] GET {url}");
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogService.Warning(LogCategory.FFXIVMT, $"[FFXIVMTService] Request failed with status {response.StatusCode} for {url}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // Detect HTML error pages (API sometimes returns 200 with HTML on server errors)
            if (json.TrimStart().StartsWith('<') || json.TrimStart().StartsWith('\n'))
            {
                // Try to extract a JSON error payload that may follow the HTML
                var jsonStart = json.LastIndexOf('{');
                if (jsonStart >= 0)
                {
                    var jsonPart = json[jsonStart..];
                    LogService.Warning(LogCategory.FFXIVMT, $"[FFXIVMTService] Server returned HTML with embedded error for {url}: {jsonPart}");
                }
                else
                {
                    LogService.Warning(LogCategory.FFXIVMT, $"[FFXIVMTService] Server returned HTML instead of JSON for {url}");
                }
                return null;
            }

            var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);

            return result;
        }
        catch (TaskCanceledException)
        {
            LogService.Debug(LogCategory.FFXIVMT, $"[FFXIVMTService] Request cancelled for {url}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            LogService.Warning(LogCategory.FFXIVMT, $"[FFXIVMTService] HTTP error for {url}: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            LogService.Error(LogCategory.FFXIVMT, $"[FFXIVMTService] JSON parse error for {url}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.FFXIVMT, $"[FFXIVMTService] Unexpected error for {url}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;

        try { _httpClient.Dispose(); }
        catch (Exception) { /* Ignore disposal errors */ }

        LogService.Debug(LogCategory.FFXIVMT, "[FFXIVMTService] Disposed");
    }
}
