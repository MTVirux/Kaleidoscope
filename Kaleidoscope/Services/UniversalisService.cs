using System.Net.Http;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Universalis;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service for fetching market data from the Universalis API.
/// Provides access to item prices, listings, sale history, and market statistics.
/// </summary>
/// <remarks>
/// Rate limits: Limited to 10 req/s to be conservative with Universalis API.
/// See: https://docs.universalis.app
/// </remarks>
public sealed class UniversalisService : IService, IDisposable
{
    private const string BaseUrl = "https://universalis.app/api/v2/";
    private const string UserAgent = "Kaleidoscope-FFXIV-Plugin";
    private const int MaxRequestsPerSecond = 10;
    
    private readonly HttpClient _httpClient;
    private readonly IPluginLog _log;
    private readonly IObjectTable _objectTable;
    private readonly ConfigurationService _configService;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _rateLimitSemaphore = new(MaxRequestsPerSecond, MaxRequestsPerSecond);
    private readonly Queue<DateTime> _requestTimestamps = new();
    private readonly object _rateLimitLock = new();

    // Cached static data - only fetched once per plugin session
    private List<int>? _cachedMarketableItems;
    private List<UniversalisWorld>? _cachedWorlds;
    private List<UniversalisDataCenter>? _cachedDataCenters;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private volatile bool _disposed;

    private Configuration Config => _configService.Config;

    /// <summary>
    /// Creates and initializes the Universalis service.
    /// </summary>
    public UniversalisService(IPluginLog log, IObjectTable objectTable, ConfigurationService configService)
    {
        _log = log;
        _objectTable = objectTable;
        _configService = configService;
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _log.Debug($"UniversalisService initialized with rate limit of {MaxRequestsPerSecond} req/s");
    }

    /// <summary>
    /// Waits for rate limit before making a request. Uses a sliding window approach.
    /// </summary>
    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        // Clean up old timestamps and check if we need to wait
        TimeSpan waitTime;
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-1);
            
            // Remove timestamps older than 1 second
            while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < windowStart)
            {
                _requestTimestamps.Dequeue();
            }
            
            // If we're at the limit, calculate wait time
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
        
        // Wait outside the lock if needed
        if (waitTime > TimeSpan.Zero)
        {
            _log.Debug($"UniversalisService: Rate limiting, waiting {waitTime.TotalMilliseconds:F0}ms");
            await Task.Delay(waitTime, cancellationToken);
        }
        
        // Record this request timestamp
        lock (_rateLimitLock)
        {
            _requestTimestamps.Enqueue(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Gets the configured query scope string based on user settings and current character location.
    /// </summary>
    /// <returns>The world, data center, or region name to use for queries, or null if unavailable.</returns>
    public string? GetConfiguredScope()
    {
        return Config.UniversalisQueryScope switch
        {
            UniversalisScope.World => GetWorldScope(),
            UniversalisScope.DataCenter => GetDataCenterScope(),
            UniversalisScope.Region => GetRegionScope(),
            _ => GetDataCenterScope() // Default to DC
        };
    }

    private string? GetWorldScope()
    {
        if (!string.IsNullOrWhiteSpace(Config.UniversalisWorldOverride))
            return Config.UniversalisWorldOverride;

        // Use current character's world
        var player = _objectTable.LocalPlayer;
        var world = player?.CurrentWorld.Value;
        return world?.Name.ToString();
    }

    private string? GetDataCenterScope()
    {
        if (!string.IsNullOrWhiteSpace(Config.UniversalisDataCenterOverride))
            return Config.UniversalisDataCenterOverride;

        // Use current character's data center
        var player = _objectTable.LocalPlayer;
        var world = player?.CurrentWorld.Value;
        if (world == null)
            return null;
        
        var dc = world.Value.DataCenter.Value;
        return dc.Name.ToString();
    }

    private string? GetRegionScope()
    {
        if (!string.IsNullOrWhiteSpace(Config.UniversalisRegionOverride))
            return Config.UniversalisRegionOverride;

        // Use current character's region based on data center
        var player = _objectTable.LocalPlayer;
        var world = player?.CurrentWorld.Value;
        if (world == null)
            return null;
        
        var dcName = world.Value.DataCenter.Value.Name.ToString();
        
        if (string.IsNullOrEmpty(dcName))
            return null;

        // Map DC to region
        return dcName switch
        {
            "Elemental" or "Gaia" or "Mana" or "Meteor" => "Japan",
            "Aether" or "Crystal" or "Primal" or "Dynamis" => "North-America",
            "Chaos" or "Light" => "Europe",
            "Materia" => "Oceania",
            _ => null
        };
    }

    /// <summary>
    /// Gets market board data for an item using the configured scope.
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="listings">Optional: Number of listings to return.</param>
    /// <param name="entries">Optional: Number of history entries to return.</param>
    /// <param name="hqOnly">Optional: Filter for HQ listings only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Market board data for the item, or null if not found or scope unavailable.</returns>
    public async Task<MarketBoardData?> GetMarketBoardDataAsync(
        uint itemId,
        int? listings = null,
        int? entries = null,
        bool? hqOnly = null,
        CancellationToken cancellationToken = default)
    {
        var scope = GetConfiguredScope();
        if (string.IsNullOrEmpty(scope))
        {
            _log.Warning("UniversalisService: Cannot determine query scope - no character logged in?");
            return null;
        }

        return await GetMarketBoardDataAsync(scope, itemId, listings, entries, hqOnly, cancellationToken);
    }

    /// <summary>
    /// Gets aggregated market data for an item using the configured scope.
    /// Preferred over GetMarketBoardDataAsync when individual listings are not needed.
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated market data for the item, or null if not found or scope unavailable.</returns>
    public async Task<AggregatedMarketData?> GetAggregatedDataAsync(
        uint itemId,
        CancellationToken cancellationToken = default)
    {
        var scope = GetConfiguredScope();
        if (string.IsNullOrEmpty(scope))
        {
            _log.Warning("UniversalisService: Cannot determine query scope - no character logged in?");
            return null;
        }

        return await GetAggregatedDataAsync(scope, itemId, cancellationToken);
    }

    /// <summary>
    /// Gets the minimum price for an item using the configured scope.
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (NQ min price, HQ min price), or null if not found.</returns>
    public async Task<(int NqPrice, int HqPrice)?> GetMinPriceAsync(
        uint itemId,
        CancellationToken cancellationToken = default)
    {
        var scope = GetConfiguredScope();
        if (string.IsNullOrEmpty(scope))
            return null;

        return await GetMinPriceAsync(scope, itemId, cancellationToken);
    }

    /// <summary>
    /// Gets the lowest price (either NQ or HQ) for an item using the configured scope.
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lowest price, or null if not found.</returns>
    public async Task<int?> GetLowestPriceAsync(
        uint itemId,
        CancellationToken cancellationToken = default)
    {
        var scope = GetConfiguredScope();
        if (string.IsNullOrEmpty(scope))
            return null;

        return await GetLowestPriceAsync(scope, itemId, cancellationToken);
    }

    /// <summary>
    /// Gets the current market board data for an item on a specific world or data center.
    /// </summary>
    /// <param name="worldOrDc">World name, data center name, or region (e.g., "Gilgamesh", "Aether", "North-America").</param>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="listings">Optional: Number of listings to return. Default returns all.</param>
    /// <param name="entries">Optional: Number of history entries to return. Default is 5.</param>
    /// <param name="hqOnly">Optional: Filter for HQ listings only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Market board data for the item, or null if not found.</returns>
    public async Task<MarketBoardData?> GetMarketBoardDataAsync(
        string worldOrDc,
        uint itemId,
        int? listings = null,
        int? entries = null,
        bool? hqOnly = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = BuildQueryParams(listings, entries, hqOnly);
        var url = $"{Uri.EscapeDataString(worldOrDc)}/{itemId}{queryParams}";

        return await GetAsync<MarketBoardData>(url, cancellationToken);
    }

    /// <summary>
    /// Gets the current market board data for multiple items on a specific world or data center.
    /// </summary>
    /// <param name="worldOrDc">World name, data center name, or region.</param>
    /// <param name="itemIds">The item IDs to look up (up to 100).</param>
    /// <param name="listings">Optional: Number of listings to return per item.</param>
    /// <param name="entries">Optional: Number of history entries to return per item.</param>
    /// <param name="hqOnly">Optional: Filter for HQ listings only.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Market board data keyed by item ID.</returns>
    public async Task<MarketBoardMultiData?> GetMarketBoardDataAsync(
        string worldOrDc,
        IEnumerable<uint> itemIds,
        int? listings = null,
        int? entries = null,
        bool? hqOnly = null,
        CancellationToken cancellationToken = default)
    {
        var itemIdList = itemIds.ToList();
        if (itemIdList.Count == 0) return null;
        if (itemIdList.Count > 100)
        {
            _log.Warning("UniversalisService: Requested more than 100 items, truncating to first 100");
            itemIdList = itemIdList.Take(100).ToList();
        }

        var queryParams = BuildQueryParams(listings, entries, hqOnly);
        var itemIdsStr = string.Join(",", itemIdList);
        var url = $"{Uri.EscapeDataString(worldOrDc)}/{itemIdsStr}{queryParams}";

        return await GetAsync<MarketBoardMultiData>(url, cancellationToken);
    }

    /// <summary>
    /// Gets aggregated market board data for an item. Uses cached values for better performance.
    /// Preferred over GetMarketBoardDataAsync when individual listings are not needed.
    /// </summary>
    /// <param name="worldOrDc">World name, data center name, or region.</param>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated market data for the item, or null if not found.</returns>
    public async Task<AggregatedMarketData?> GetAggregatedDataAsync(
        string worldOrDc,
        uint itemId,
        CancellationToken cancellationToken = default)
    {
        var url = $"aggregated/{Uri.EscapeDataString(worldOrDc)}/{itemId}";
        return await GetAsync<AggregatedMarketData>(url, cancellationToken);
    }

    /// <summary>
    /// Gets aggregated market board data for multiple items.
    /// </summary>
    /// <param name="worldOrDc">World name, data center name, or region.</param>
    /// <param name="itemIds">The item IDs to look up (up to 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated market data for the items.</returns>
    public async Task<AggregatedMarketData?> GetAggregatedDataAsync(
        string worldOrDc,
        IEnumerable<uint> itemIds,
        CancellationToken cancellationToken = default)
    {
        var itemIdList = itemIds.ToList();
        if (itemIdList.Count == 0) return null;
        if (itemIdList.Count > 100)
        {
            _log.Warning("UniversalisService: Requested more than 100 items, truncating to first 100");
            itemIdList = itemIdList.Take(100).ToList();
        }

        var itemIdsStr = string.Join(",", itemIdList);
        var url = $"aggregated/{Uri.EscapeDataString(worldOrDc)}/{itemIdsStr}";
        return await GetAsync<AggregatedMarketData>(url, cancellationToken);
    }

    /// <summary>
    /// Gets the sale history for an item on a specific world or data center.
    /// </summary>
    /// <param name="worldOrDc">World name, data center name, or region.</param>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="entriesToReturn">Number of entries to return. Default is 1800, max is 99999.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sale history for the item, or null if not found.</returns>
    public async Task<MarketHistory?> GetHistoryAsync(
        string worldOrDc,
        uint itemId,
        int? entriesToReturn = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = entriesToReturn.HasValue ? $"?entriesToReturn={entriesToReturn.Value}" : "";
        var url = $"history/{Uri.EscapeDataString(worldOrDc)}/{itemId}{queryParams}";
        return await GetAsync<MarketHistory>(url, cancellationToken);
    }

    /// <summary>
    /// Gets the current tax rates for a specific world.
    /// </summary>
    /// <param name="world">World name or ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tax rates for the world, or null if not found.</returns>
    public async Task<TaxRates?> GetTaxRatesAsync(
        string world,
        CancellationToken cancellationToken = default)
    {
        var url = $"tax-rates?world={Uri.EscapeDataString(world)}";
        return await GetAsync<TaxRates>(url, cancellationToken);
    }

    /// <summary>
    /// Gets the list of all marketable item IDs.
    /// This data is cached for the entire plugin session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of marketable item IDs.</returns>
    public async Task<List<int>?> GetMarketableItemsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return _cachedMarketableItems;
        
        if (_cachedMarketableItems != null)
            return _cachedMarketableItems;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedMarketableItems != null)
                return _cachedMarketableItems;

            _log.Debug("UniversalisService: Fetching and caching marketable items");
            _cachedMarketableItems = await GetAsync<List<int>>("marketable", cancellationToken);
            return _cachedMarketableItems;
        }
        finally
        {
            try { _cacheLock.Release(); }
            catch (ObjectDisposedException) { /* Ignore - disposed during operation */ }
        }
    }

    /// <summary>
    /// Gets the minimum price for an item (NQ and HQ) from aggregated data.
    /// This is a convenience method for common use cases.
    /// </summary>
    /// <param name="worldOrDc">World name, data center name, or region.</param>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (NQ min price, HQ min price), or null if not found.</returns>
    public async Task<(int NqPrice, int HqPrice)?> GetMinPriceAsync(
        string worldOrDc,
        uint itemId,
        CancellationToken cancellationToken = default)
    {
        var data = await GetAggregatedDataAsync(worldOrDc, itemId, cancellationToken);
        if (data?.Results == null || data.Results.Count == 0)
            return null;

        var result = data.Results[0];
        var nqPrice = result.Nq?.MinListing?.World?.Price ?? 0;
        var hqPrice = result.Hq?.MinListing?.World?.Price ?? 0;

        return (nqPrice, hqPrice);
    }

    /// <summary>
    /// Gets the lowest price (either NQ or HQ) for an item from aggregated data.
    /// </summary>
    /// <param name="worldOrDc">World name, data center name, or region.</param>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lowest price, or null if not found.</returns>
    public async Task<int?> GetLowestPriceAsync(
        string worldOrDc,
        uint itemId,
        CancellationToken cancellationToken = default)
    {
        var prices = await GetMinPriceAsync(worldOrDc, itemId, cancellationToken);
        if (!prices.HasValue)
            return null;

        var (nq, hq) = prices.Value;
        if (nq == 0 && hq == 0) return null;
        if (nq == 0) return hq;
        if (hq == 0) return nq;
        return Math.Min(nq, hq);
    }

    /// <summary>
    /// Gets all available worlds from Universalis.
    /// This data is cached for the entire plugin session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of worlds, or null if failed.</returns>
    public async Task<List<UniversalisWorld>?> GetWorldsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return _cachedWorlds;
        
        if (_cachedWorlds != null)
            return _cachedWorlds;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedWorlds != null)
                return _cachedWorlds;

            _log.Debug("UniversalisService: Fetching and caching worlds");
            _cachedWorlds = await GetAsync<List<UniversalisWorld>>("worlds", cancellationToken);
            return _cachedWorlds;
        }
        finally
        {
            try { _cacheLock.Release(); }
            catch (ObjectDisposedException) { /* Ignore - disposed during operation */ }
        }
    }

    /// <summary>
    /// Gets all available data centers from Universalis.
    /// This data is cached for the entire plugin session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of data centers, or null if failed.</returns>
    public async Task<List<UniversalisDataCenter>?> GetDataCentersAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return _cachedDataCenters;
        
        if (_cachedDataCenters != null)
            return _cachedDataCenters;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedDataCenters != null)
                return _cachedDataCenters;

            _log.Debug("UniversalisService: Fetching and caching data centers");
            _cachedDataCenters = await GetAsync<List<UniversalisDataCenter>>("data-centers", cancellationToken);
            return _cachedDataCenters;
        }
        finally
        {
            try { _cacheLock.Release(); }
            catch (ObjectDisposedException) { /* Ignore - disposed during operation */ }
        }
    }

    private static string BuildQueryParams(int? listings, int? entries, bool? hqOnly)
    {
        var queryParams = new List<string>();
        if (listings.HasValue) queryParams.Add($"listings={listings.Value}");
        if (entries.HasValue) queryParams.Add($"entries={entries.Value}");
        if (hqOnly.HasValue && hqOnly.Value) queryParams.Add("hq=true");
        return queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        if (_disposed)
            return null;
        
        try
        {
            // Wait for rate limiter before making request
            await WaitForRateLimitAsync(cancellationToken);

            _log.Debug($"UniversalisService: GET {url}");
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warning($"UniversalisService: Request failed with status {response.StatusCode} for {url}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            
            return result;
        }
        catch (TaskCanceledException)
        {
            _log.Debug($"UniversalisService: Request cancelled for {url}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.Warning($"UniversalisService: HTTP error for {url}: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            _log.Error($"UniversalisService: JSON parse error for {url}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error($"UniversalisService: Unexpected error for {url}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        
        try { _rateLimitSemaphore.Dispose(); }
        catch (Exception) { /* Ignore disposal errors */ }
        
        try { _cacheLock.Dispose(); }
        catch (Exception) { /* Ignore disposal errors */ }
        
        try { _httpClient.Dispose(); }
        catch (Exception) { /* Ignore disposal errors */ }
        
        _log.Debug("UniversalisService disposed");
    }
}

/// <summary>
/// Response type for multi-item market board queries.
/// </summary>
public sealed class MarketBoardMultiData
{
    /// <summary>The item IDs that were requested.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("itemIDs")]
    public List<int>? ItemIds { get; set; }

    /// <summary>The item data that was requested, keyed on the item ID.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public Dictionary<string, MarketBoardData>? Items { get; set; }

    /// <summary>The ID of the world requested, if applicable.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("worldID")]
    public int? WorldId { get; set; }

    /// <summary>The name of the DC requested, if applicable.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("dcName")]
    public string? DcName { get; set; }

    /// <summary>The name of the region requested, if applicable.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("regionName")]
    public string? RegionName { get; set; }

    /// <summary>A list of IDs that could not be resolved to any item data.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("unresolvedItems")]
    public List<int>? UnresolvedItems { get; set; }

    /// <summary>The name of the world requested, if applicable.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    /// <summary>
    /// Gets market data for a specific item ID.
    /// </summary>
    public MarketBoardData? GetItem(uint itemId)
    {
        if (Items == null) return null;
        return Items.TryGetValue(itemId.ToString(), out var data) ? data : null;
    }
}
