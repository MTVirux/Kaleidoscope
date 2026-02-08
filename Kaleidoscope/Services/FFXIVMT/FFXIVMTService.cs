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

        LogService.Debug(LogCategory.Universalis, $"[FFXIVMTService] Initialized with rate limit of {MaxRequestsPerSecond} req/s");
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
            LogService.Debug(LogCategory.Universalis, $"[FFXIVMTService] Rate limiting, waiting {waitTime.TotalMilliseconds:F0}ms");
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
        if (response?.Data == null || response.Data.Value.ValueKind == JsonValueKind.Undefined)
            return null;

        try
        {
            // The "data" field can be a JSON object (dict), a JSON array, or a JSON-encoded string
            List<GilfluxItem>? itemList = null;
            var dataElement = response.Data.Value;
            var dataJson = dataElement.ValueKind == JsonValueKind.String
                ? dataElement.GetString()
                : dataElement.GetRawText();

            if (!string.IsNullOrEmpty(dataJson))
            {
                var trimmed = dataJson.TrimStart();
                if (trimmed.StartsWith('['))
                {
                    // Array of items
                    itemList = JsonSerializer.Deserialize<List<GilfluxItem>>(dataJson, _jsonOptions);
                }
                else if (trimmed.StartsWith('{'))
                {
                    // Dictionary keyed by item id
                    var dict = JsonSerializer.Deserialize<Dictionary<string, GilfluxItem>>(dataJson, _jsonOptions);
                    itemList = dict?.Values.ToList();
                }
            }

            // Parse the timeframe labels and durations from the gilflux_timeframe_in_ms field
            var timeframeLabels = new List<string>();
            var timeframeDurations = new Dictionary<string, TimeSpan>();
            if (response.GilfluxTimeframeInMs != null && response.GilfluxTimeframeInMs.Value.ValueKind != JsonValueKind.Undefined)
            {
                try
                {
                    Dictionary<string, long>? timeframes;
                    var tfElement = response.GilfluxTimeframeInMs.Value;
                    if (tfElement.ValueKind == JsonValueKind.String)
                    {
                        var tfString = tfElement.GetString();
                        timeframes = string.IsNullOrEmpty(tfString)
                            ? null
                            : JsonSerializer.Deserialize<Dictionary<string, long>>(tfString, _jsonOptions);
                    }
                    else if (tfElement.ValueKind == JsonValueKind.Object)
                    {
                        timeframes = JsonSerializer.Deserialize<Dictionary<string, long>>(tfElement.GetRawText(), _jsonOptions);
                    }
                    else
                    {
                        timeframes = null;
                    }

                    if (timeframes != null)
                    {
                        // Sort by duration ascending (shortest timeframe first: 1h, 3h, ... 7d)
                        timeframeLabels = timeframes.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();

                        // Convert ms durations to TimeSpan
                        foreach (var kv in timeframes)
                            timeframeDurations[kv.Key] = TimeSpan.FromMilliseconds(kv.Value);
                    }
                }
                catch (JsonException)
                {
                    // Fall back to default timeframe labels if parsing fails (shortest first)
                    timeframeLabels = new List<string> { "1h", "3h", "6h", "12h", "1d", "3d", "7d" };
                }
            }

            // If no timeframes were returned, use defaults (shortest first)
            if (timeframeLabels.Count == 0)
                timeframeLabels = new List<string> { "1h", "3h", "6h", "12h", "1d", "3d", "7d" };

            return new GilfluxResult
            {
                Items = itemList ?? new List<GilfluxItem>(),
                TimeframeLabels = timeframeLabels,
                TimeframeDurations = timeframeDurations,
            };
        }
        catch (JsonException ex)
        {
            LogService.Error(LogCategory.Universalis, $"[FFXIVMTService] Failed to parse Gilflux data: {ex.Message}");
            return null;
        }
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

            LogService.Debug(LogCategory.Universalis, $"[FFXIVMTService] GET {url}");
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogService.Warning(LogCategory.Universalis, $"[FFXIVMTService] Request failed with status {response.StatusCode} for {url}");
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
                    LogService.Warning(LogCategory.Universalis, $"[FFXIVMTService] Server returned HTML with embedded error for {url}: {jsonPart}");
                }
                else
                {
                    LogService.Warning(LogCategory.Universalis, $"[FFXIVMTService] Server returned HTML instead of JSON for {url}");
                }
                return null;
            }

            var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);

            return result;
        }
        catch (TaskCanceledException)
        {
            LogService.Debug(LogCategory.Universalis, $"[FFXIVMTService] Request cancelled for {url}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            LogService.Warning(LogCategory.Universalis, $"[FFXIVMTService] HTTP error for {url}: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            LogService.Error(LogCategory.Universalis, $"[FFXIVMTService] JSON parse error for {url}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Universalis, $"[FFXIVMTService] Unexpected error for {url}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;

        try { _httpClient.Dispose(); }
        catch (Exception) { /* Ignore disposal errors */ }

        LogService.Debug(LogCategory.Universalis, "[FFXIVMTService] Disposed");
    }
}
