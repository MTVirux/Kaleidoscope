using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models.FFXIVMT;
using Kaleidoscope.Models.Settings;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using Kaleidoscope.Services.FFXIVMT;
using Kaleidoscope.Services.Universalis;
using Kaleidoscope.Gui.Widgets.Table;
using Kaleidoscope.Gui.Widgets.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.FFXIVMT;

/// <summary>
/// Represents an ignored item with an optional expiration time.
/// </summary>
public sealed class IgnoredItemEntry
{
    /// <summary>Item ID being ignored.</summary>
    public int ItemId { get; set; }

    /// <summary>When the ignore expires (UTC), or null for permanent.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Display name cached at time of ignore.</summary>
    public string? ItemName { get; set; }
}

/// <summary>
/// Settings for the GilFlux tool.
/// </summary>
public sealed class GilFluxToolSettings
{
    public HashSet<int> SelectedWorldIds { get; set; } = new();
    public HashSet<string> SelectedDataCenters { get; set; } = new();
    public HashSet<string> SelectedRegions { get; set; } = new();
    public int Scope { get; set; } = (int)WorldSelectionMode.Worlds;
    public bool CraftedOnly { get; set; } = true;

    /// <summary>Items ignored from display. Key = ItemId.</summary>
    public Dictionary<int, IgnoredItemEntry> IgnoredItems { get; set; } = new();

    /// <summary>Pinned item IDs for the item picker filter (only show these).</summary>
    public HashSet<uint> PinnedItemIds { get; set; } = new();

    /// <summary>How often to re-fetch API data (minutes). 0 = never auto-refresh.</summary>
    public int RefreshIntervalMinutes { get; set; } = 5;

    // Standard table settings
    /// <summary>Optional custom color for the table header row background.</summary>
    public Vector4? HeaderColor { get; set; }

    /// <summary>Optional custom color for even-numbered rows.</summary>
    public Vector4? EvenRowColor { get; set; }

    /// <summary>Optional custom color for odd-numbered rows.</summary>
    public Vector4? OddRowColor { get; set; }

    /// <summary>Whether to freeze the header row when scrolling.</summary>
    public bool FreezeHeader { get; set; } = true;
}

/// <summary>
/// Tool that displays Gilflux ranking data from the FFXIVMT API.
/// Shows which items move the most gil on the market for a given set of worlds.
/// Queries each world individually and merges results.
/// </summary>
public sealed class GilFluxTool : ToolComponent
{
    public override string ToolName => "GilFlux";

    private readonly FFXIVMTService _ffxivmtService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly ItemDataService? _itemDataService;
    private readonly GilFluxToolSettings _settings = new();
    private WorldSelectionWidget? _worldSelector;
    private bool _worldSelectorInitialized;

    // Item picker for filtering to specific items
    private readonly ItemComboDropdown? _itemPicker;

    private static readonly SettingsSchema<GilFluxToolSettings> Schema = SettingsSchema.For<GilFluxToolSettings>()
        .Checkbox(s => s.CraftedOnly, "Crafted Only", "Only show crafted items", true)
        .SliderInt(s => s.RefreshIntervalMinutes, "Refresh Interval (min)", 0, 30,
            "How often to re-fetch API data to keep rankings fresh. 0 = never auto-refresh.");

    /// <summary>
    /// A WebSocket-sourced sale entry with a timestamp for time-window pruning.
    /// </summary>
    private sealed class TimestampedSale
    {
        public DateTime ReceivedAtUtc { get; init; }
        public int ItemId { get; init; }
        public long Total { get; init; }
    }

    // State
    private List<GilfluxItem>? _items;

    /// <summary>Items from the most recent API fetch (replaced on each re-fetch).</summary>
    private List<GilfluxItem> _baseItems = new();

    /// <summary>WebSocket-sourced sales with timestamps, pruned by time window.</summary>
    private List<TimestampedSale> _liveItems = new();

    private readonly object _itemsLock = new();
    private List<string> _timeframeLabels = new() { "1h", "3h", "6h", "12h", "1d", "3d", "7d" };
    private bool _isLoading;
    private bool _initialLoadComplete;
    private string? _errorMessage;
    private string? _currentFetchLocation;
    private string _filterText = string.Empty;
    private string _ignoredItemsFilter = string.Empty;
    private CancellationTokenSource? _cts;

    // Periodic re-fetch state
    private DateTime _lastApiFetchUtc = DateTime.MinValue;

    /// <summary>
    /// Maps timeframe labels to their durations for bucket-aware sale assignment.
    /// Populated from the API's gilflux_timeframe_in_ms response.
    /// </summary>
    private static readonly Dictionary<string, TimeSpan> DefaultTimeframeDurations = new()
    {
        ["1h"] = TimeSpan.FromHours(1),
        ["3h"] = TimeSpan.FromHours(3),
        ["6h"] = TimeSpan.FromHours(6),
        ["12h"] = TimeSpan.FromHours(12),
        ["1d"] = TimeSpan.FromDays(1),
        ["3d"] = TimeSpan.FromDays(3),
        ["7d"] = TimeSpan.FromDays(7),
    };

    private Dictionary<string, TimeSpan> _timeframeDurations = new(DefaultTimeframeDurations);

    // WebSocket live update state
    private HashSet<int>? _effectiveWorldIds;
    private bool _subscribedToWebSocket;
    private volatile bool _rebuildPending;
    private DateTime _lastRebuildTime = DateTime.MinValue;
    private const double RebuildDebounceMs = 500;

    public GilFluxTool(
        FFXIVMTService ffxivmtService,
        PriceTrackingService? priceTrackingService,
        ItemDataService? itemDataService = null,
        ITextureProvider? textureProvider = null,
        IDataManager? dataManager = null,
        FavoritesService? favoritesService = null,
        ConfigurationService? configService = null)
    {
        _ffxivmtService = ffxivmtService;
        _priceTrackingService = priceTrackingService;
        _itemDataService = itemDataService;
        Title = "GilFlux";
        Size = new Vector2(700, 400);

        // Create item picker if all required services are available
        if (textureProvider != null && dataManager != null && favoritesService != null)
        {
            _itemPicker = new ItemComboDropdown(
                textureProvider, dataManager, favoritesService, priceTrackingService,
                "gilflux_items", marketableOnly: true, configService: configService,
                multiSelect: true, emptySelectionText: "All Items",
                showAllBulkAction: true, showNoneBulkAction: true);
            _itemPicker.MultiSelectionChanged += OnItemPickerSelectionChanged;
        }
    }

    private void OnItemPickerSelectionChanged(IReadOnlySet<uint> ids)
    {
        _settings.PinnedItemIds = new HashSet<uint>(ids);
        NotifyToolSettingsChanged();
    }

    private void EnsureWorldSelector()
    {
        if (_worldSelectorInitialized) return;

        var worldData = _priceTrackingService?.WorldData;
        if (worldData == null) return;

        _worldSelectorInitialized = true;
        _worldSelector = new WorldSelectionWidget(worldData, "gilflux_worlds")
        {
            Mode = (WorldSelectionMode)_settings.Scope,
            Width = 250f
        };

        _worldSelector.InitializeFrom(_settings.SelectedRegions, _settings.SelectedDataCenters, _settings.SelectedWorldIds);

        // Auto-fetch on load if locations are already configured
        if (_worldSelector.GetSelectedLocationNames().Count > 0)
            FetchData();
    }

    public override void RenderToolContent()
    {
        try
        {
            EnsureWorldSelector();

            // Process debounced rebuild from WebSocket updates
            if (_rebuildPending && (DateTime.UtcNow - _lastRebuildTime).TotalMilliseconds >= RebuildDebounceMs)
            {
                _rebuildPending = false;
                _lastRebuildTime = DateTime.UtcNow;
                RebuildAggregatedItems();
            }

            // Periodic re-fetch of API data to keep server-side rankings fresh
            CheckPeriodicRefresh();

            DrawControls();
            ImGui.Separator();
            DrawTable();
        }
        catch (Exception ex)
        {
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    private void DrawControls()
    {
        // Search filter
        ImGui.SetNextItemWidth(200f);
        ImGui.InputTextWithHint("##gilflux_filter", "Filter items...", ref _filterText, 256);

        // Item picker (multi-select combo)
        if (_itemPicker != null)
        {
            ImGui.SameLine();
            _itemPicker.DrawMultiSelect(200f);
        }

        // Crafted-only toggle inline
        ImGui.SameLine();
        var craftedOnly = _settings.CraftedOnly;
        if (ImGui.Checkbox("Crafted Only##gilflux_inline", ref craftedOnly))
        {
            _settings.CraftedOnly = craftedOnly;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Only show crafted items");

        // Inline status / error after controls
        if (_isLoading && !_initialLoadComplete && _currentFetchLocation != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.Muted, _currentFetchLocation);
        }
        else if (_errorMessage != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.Bad, _errorMessage);
        }

        // WebSocket status circle
        DrawWebSocketStatusCircle();
    }

    /// <summary>
    /// Draws a small colored circle indicating WebSocket connection status, with a tooltip on hover.
    /// </summary>
    private void DrawWebSocketStatusCircle()
    {
        var ws = _priceTrackingService?.WebSocketService;
        if (ws == null) return;

        var isConnected = ws.IsConnected;
        var color = isConnected ? UiColors.Connected : UiColors.Disconnected;
        var tooltip = isConnected
            ? "Universalis WebSocket: Connected"
            : "Universalis WebSocket: Disconnected";

        ImGui.SameLine();
        ImGui.TextColored(color, "●");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private void DrawTable()
    {
        if (_items == null || _items.Count == 0)
        {
            if (!_isLoading)
                ImGui.TextColored(UiColors.Muted, _items == null ? "Configure worlds in settings to load data." : "No results.");
            return;
        }

        // Purge expired ignores
        PurgeExpiredIgnores();

        // Apply filters: ignored items, search text, pinned items
        var filtered = _items.AsEnumerable();

        // Filter crafted-only if enabled (client-side via RecipeLookup sheet)
        if (_settings.CraftedOnly && _itemDataService != null)
            filtered = filtered.Where(i => _itemDataService.IsCraftable(i.ItemId));

        // Hide ignored items
        var now = DateTime.UtcNow;
        filtered = filtered.Where(i => !IsItemIgnored(i.ItemId, now));

        // If item picker has selections, only show those
        if (_settings.PinnedItemIds.Count > 0)
            filtered = filtered.Where(i => _settings.PinnedItemIds.Contains((uint)i.ItemId));

        // Text search filter
        if (!string.IsNullOrWhiteSpace(_filterText))
            filtered = filtered.Where(i => i.ItemName != null && i.ItemName.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

        var displayItems = filtered.ToList();
        var columnCount = 1 + _timeframeLabels.Count; // Item + dynamic timeframe columns

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable |
                    ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("gilflux_table", columnCount, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
        {
            if (_settings.FreezeHeader)
                ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.DefaultSort, 200f);

            for (var i = 0; i < _timeframeLabels.Count; i++)
            {
                // Last column (longest timeframe) gets DefaultSort
                var isLast = i == _timeframeLabels.Count - 1;
                var colFlags = isLast ? ImGuiTableColumnFlags.DefaultSort : ImGuiTableColumnFlags.None;
                ImGui.TableSetupColumn(_timeframeLabels[i], colFlags, isLast ? 80f : 70f);
            }

            // Apply header color if set
            if (_settings.HeaderColor.HasValue)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(_settings.HeaderColor.Value));
            }
            ImGui.TableHeadersRow();

            var sortSpecs = ImGui.TableGetSortSpecs();
            SortItems(displayItems, sortSpecs);
            sortSpecs.SpecsDirty = false;

            var rowIndex = 0;
            foreach (var item in displayItems)
            {
                ImGui.TableNextRow();

                // Apply even/odd row colors if set
                var isEven = rowIndex % 2 == 0;
                if (isEven && _settings.EvenRowColor.HasValue)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(_settings.EvenRowColor.Value));
                else if (!isEven && _settings.OddRowColor.HasValue)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(_settings.OddRowColor.Value));

                ImGui.TableNextColumn();
                var displayName = string.IsNullOrEmpty(item.ItemName) ? "(No Name)" : item.ItemName;
                ImGui.Text(displayName);

                // Right-click context menu for ignore options
                if (ImGui.BeginPopupContextItem($"gilflux_ctx_{item.ItemId}"))
                {
                    ImGui.TextDisabled(displayName);
                    ImGui.Separator();

                    if (ImGui.MenuItem("Ignore permanently"))
                        IgnoreItem(item.ItemId, item.ItemName, null);
                    if (ImGui.MenuItem("Ignore for 7 days"))
                        IgnoreItem(item.ItemId, item.ItemName, TimeSpan.FromDays(7));
                    if (ImGui.MenuItem("Ignore for 3 days"))
                        IgnoreItem(item.ItemId, item.ItemName, TimeSpan.FromDays(3));
                    if (ImGui.MenuItem("Ignore for 2 days"))
                        IgnoreItem(item.ItemId, item.ItemName, TimeSpan.FromDays(2));
                    if (ImGui.MenuItem("Ignore for 1 day"))
                        IgnoreItem(item.ItemId, item.ItemName, TimeSpan.FromDays(1));

                    ImGui.EndPopup();
                }

                foreach (var label in _timeframeLabels)
                {
                    ImGui.TableNextColumn();
                    DrawGilValue(item.GetRanking(label));
                }

                rowIndex++;
            }

            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Returns true if the given item is currently ignored.
    /// </summary>
    private bool IsItemIgnored(int itemId, DateTime utcNow)
    {
        if (!_settings.IgnoredItems.TryGetValue(itemId, out var entry))
            return false;

        // Permanent ignore
        if (entry.ExpiresAtUtc == null)
            return true;

        // Timed ignore — still active?
        return utcNow < entry.ExpiresAtUtc.Value;
    }

    /// <summary>
    /// Adds an item to the ignore list with an optional duration.
    /// </summary>
    private void IgnoreItem(int itemId, string? itemName, TimeSpan? duration)
    {
        _settings.IgnoredItems[itemId] = new IgnoredItemEntry
        {
            ItemId = itemId,
            ItemName = itemName ?? _itemDataService?.GetItemName(itemId) ?? $"Item #{itemId}",
            ExpiresAtUtc = duration.HasValue ? DateTime.UtcNow + duration.Value : null,
        };
        NotifyToolSettingsChanged();
    }

    /// <summary>
    /// Removes expired timed ignores from the settings.
    /// </summary>
    private void PurgeExpiredIgnores()
    {
        var now = DateTime.UtcNow;
        var expired = _settings.IgnoredItems
            .Where(kv => kv.Value.ExpiresAtUtc != null && now >= kv.Value.ExpiresAtUtc.Value)
            .Select(kv => kv.Key)
            .ToList();

        if (expired.Count <= 0) return;

        foreach (var id in expired)
            _settings.IgnoredItems.Remove(id);
        NotifyToolSettingsChanged();
    }

    private static void DrawGilValue(long value)
    {
        if (value == 0)
        {
            ImGui.TextColored(UiColors.Muted, "-");
        }
        else
        {
            ImGui.TextColored(UiColors.Value, FormatGil(value));
        }
    }

    private static string FormatGil(long value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000.0:F1}M";
        if (value >= 1_000)
            return $"{value / 1_000.0:F1}K";
        return value.ToString("N0");
    }

    private void SortItems(List<GilfluxItem> items, ImGuiTableSortSpecsPtr sortSpecs)
    {
        if (sortSpecs.SpecsCount == 0) return;

        var spec = sortSpecs.Specs;
        var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
        var colIdx = spec.ColumnIndex;

        items.Sort((a, b) =>
        {
            int result;
            if (colIdx == 0)
            {
                result = string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Dynamic timeframe columns start at index 1
                var tfIdx = colIdx - 1;
                if (tfIdx >= 0 && tfIdx < _timeframeLabels.Count)
                {
                    var label = _timeframeLabels[tfIdx];
                    result = a.GetRanking(label).CompareTo(b.GetRanking(label));
                }
                else
                {
                    result = 0;
                }
            }
            return ascending ? result : -result;
        });
    }

    private void FetchData(bool isRefresh = false)
    {
        // Skip if initial load hasn't completed yet and this is a refresh
        if (isRefresh && !_initialLoadComplete) return;

        // Skip if already in the middle of a fetch
        if (_isLoading) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _isLoading = true;
        _errorMessage = null;
        _currentFetchLocation = null;
        var newBaseItems = new List<GilfluxItem>();
        var token = _cts.Token;

        var locationNames = _worldSelector?.GetSelectedLocationNames() ?? new List<string>();

        if (locationNames.Count == 0)
        {
            _isLoading = false;
            _errorMessage = "No locations selected.";
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var failed = 0;

                foreach (var location in locationNames)
                {
                    if (token.IsCancellationRequested) return;

                    _currentFetchLocation = isRefresh
                        ? $"Refreshing {location}..."
                        : $"Fetching {location}...";

                    var result = await _ffxivmtService.GetGilfluxAsync(location, token);
                    if (result != null)
                    {
                        newBaseItems.AddRange(result.Items);

                        // Capture timeframe labels and durations from the first successful response
                        if (result.TimeframeLabels is { Count: > 0 })
                            _timeframeLabels = result.TimeframeLabels;

                        if (result.TimeframeDurations is { Count: > 0 })
                            _timeframeDurations = result.TimeframeDurations;

                        // Replace base items and rebuild progressively
                        lock (_itemsLock)
                        {
                            _baseItems = new List<GilfluxItem>(newBaseItems);
                        }

                        RebuildAggregatedItems();
                    }
                    else
                    {
                        failed++;
                    }
                }

                if (token.IsCancellationRequested) return;

                _lastApiFetchUtc = DateTime.UtcNow;
                _initialLoadComplete = true;

                // Subscribe to WebSocket for live updates (idempotent)
                SubscribeToWebSocket();

                lock (_itemsLock)
                {
                    _baseItems = newBaseItems;
                }

                // Final rebuild with complete data
                RebuildAggregatedItems();

                if (newBaseItems.Count == 0)
                {
                    _items = new List<GilfluxItem>();
                    _errorMessage = $"No data returned. {failed} location(s) failed.";
                }
                else
                {
                    _errorMessage = failed > 0 ? $"{failed} location(s) failed" : null;
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _errorMessage = $"Error: {ex.Message}";
                }
            }
            finally
            {
                _isLoading = false;
                _currentFetchLocation = null;
            }
        }, token);
    }

    /// <summary>
    /// Checks if it's time to periodically re-fetch API data to keep rankings fresh.
    /// </summary>
    private void CheckPeriodicRefresh()
    {
        if (!_initialLoadComplete || _isLoading) return;

        var intervalMinutes = _settings.RefreshIntervalMinutes;
        if (intervalMinutes <= 0) return;

        var elapsed = DateTime.UtcNow - _lastApiFetchUtc;
        if (elapsed.TotalMinutes < intervalMinutes) return;

        LogService.Debug(LogCategory.PriceTracking,
            $"[GilFlux] Periodic refresh triggered ({elapsed.TotalMinutes:F0}m since last fetch)");
        FetchData(isRefresh: true);
    }

    /// <summary>
    /// Rebuilds the aggregated and name-resolved item list by merging base (API) data
    /// with time-pruned live (WebSocket) data. Each live sale only contributes to
    /// timeframe buckets it still falls within based on its age.
    /// </summary>
    private void RebuildAggregatedItems()
    {
        List<GilfluxItem> baseSnapshot;
        List<TimestampedSale> liveSnapshot;
        var now = DateTime.UtcNow;

        lock (_itemsLock)
        {
            baseSnapshot = _baseItems.ToList();

            // Prune live items older than the max tracked window (7d + 1h buffer)
            var maxAge = TimeSpan.FromDays(7) + TimeSpan.FromHours(1);
            _liveItems.RemoveAll(s => (now - s.ReceivedAtUtc) > maxAge);
            liveSnapshot = _liveItems.ToList();
        }

        // Step 1: Aggregate base items by ItemId (server-side totals)
        var aggregated = baseSnapshot
            .GroupBy(i => i.ItemId)
            .ToDictionary(
                g => g.Key,
                g => new GilfluxItem
                {
                    ItemId = g.Key,
                    ItemName = _itemDataService?.GetItemName(g.Key) ?? $"Item #{g.Key}",
                    RankingAllTime = g.Sum(i => i.RankingAllTime),
                    Ranking1h = g.Sum(i => i.Ranking1h),
                    Ranking3h = g.Sum(i => i.Ranking3h),
                    Ranking6h = g.Sum(i => i.Ranking6h),
                    Ranking12h = g.Sum(i => i.Ranking12h),
                    Ranking1d = g.Sum(i => i.Ranking1d),
                    Ranking3d = g.Sum(i => i.Ranking3d),
                    Ranking7d = g.Sum(i => i.Ranking7d),
                });

        // Step 2: Merge live sales, only contributing to applicable time buckets
        foreach (var sale in liveSnapshot)
        {
            var age = now - sale.ReceivedAtUtc;

            if (!aggregated.TryGetValue(sale.ItemId, out var item))
            {
                item = new GilfluxItem
                {
                    ItemId = sale.ItemId,
                    ItemName = _itemDataService?.GetItemName(sale.ItemId) ?? $"Item #{sale.ItemId}",
                };
                aggregated[sale.ItemId] = item;
            }

            // Always add to alltime
            item.RankingAllTime += sale.Total;

            // Only add to buckets the sale still falls within
            if (age <= GetTimeframeDuration("1h"))  item.Ranking1h  += sale.Total;
            if (age <= GetTimeframeDuration("3h"))  item.Ranking3h  += sale.Total;
            if (age <= GetTimeframeDuration("6h"))  item.Ranking6h  += sale.Total;
            if (age <= GetTimeframeDuration("12h")) item.Ranking12h += sale.Total;
            if (age <= GetTimeframeDuration("1d"))  item.Ranking1d  += sale.Total;
            if (age <= GetTimeframeDuration("3d"))  item.Ranking3d  += sale.Total;
            if (age <= GetTimeframeDuration("7d"))  item.Ranking7d  += sale.Total;
        }

        var result = aggregated.Values.ToList();
        var primaryLabel = _timeframeLabels.Count > 0 ? _timeframeLabels[^1] : "7d";
        result.Sort((a, b) => b.GetRanking(primaryLabel).CompareTo(a.GetRanking(primaryLabel)));
        _items = result;
    }

    /// <summary>
    /// Returns the duration for a timeframe label, using the API-reported value if available,
    /// otherwise falling back to built-in defaults.
    /// </summary>
    private TimeSpan GetTimeframeDuration(string label)
    {
        if (_timeframeDurations.TryGetValue(label, out var duration))
            return duration;
        if (DefaultTimeframeDurations.TryGetValue(label, out duration))
            return duration;
        return TimeSpan.FromDays(7); // Safe fallback
    }

    /// <summary>
    /// Subscribes to the Universalis WebSocket for real-time sale updates.
    /// Caches the effective world IDs for fast filtering.
    /// </summary>
    private void SubscribeToWebSocket()
    {
        if (_subscribedToWebSocket) return;

        var ws = _priceTrackingService?.WebSocketService;
        if (ws == null) return;

        _effectiveWorldIds = _worldSelector?.GetEffectiveWorldIds();
        ws.OnPriceUpdate += OnWebSocketPriceUpdate;
        _subscribedToWebSocket = true;
    }

    /// <summary>
    /// Unsubscribes from the WebSocket.
    /// </summary>
    private void UnsubscribeFromWebSocket()
    {
        if (!_subscribedToWebSocket) return;

        var ws = _priceTrackingService?.WebSocketService;
        if (ws != null)
            ws.OnPriceUpdate -= OnWebSocketPriceUpdate;

        _subscribedToWebSocket = false;
    }

    /// <summary>
    /// Handles incoming WebSocket price updates.
    /// For sale events on tracked worlds, stores a timestamped sale record.
    /// The timestamp allows RebuildAggregatedItems to only contribute the sale
    /// to timeframe buckets it still falls within.
    /// </summary>
    private void OnWebSocketPriceUpdate(PriceFeedEntry entry)
    {
        // Only process sales (actual gil movement)
        if (entry.EventType != "Sale") return;
        if (entry.Total <= 0) return;

        // Filter to selected worlds
        if (_effectiveWorldIds == null || !_effectiveWorldIds.Contains(entry.WorldId)) return;

        // Log the sale event details
        var itemName = _itemDataService?.GetItemName(entry.ItemId) ?? $"Item #{entry.ItemId}";
        var worldName = entry.WorldName ?? $"World {entry.WorldId}";
        LogService.Verbose(LogCategory.PriceTracking,
            $"[GilFlux] Sale update: {itemName} (ID: {entry.ItemId}) on {worldName} — Qty: {entry.Quantity}, Value: {entry.PricePerUnit:N0}, Total: {entry.Total:N0}");

        // Store timestamped sale — bucket assignment is deferred to RebuildAggregatedItems
        var sale = new TimestampedSale
        {
            ReceivedAtUtc = DateTime.UtcNow,
            ItemId = entry.ItemId,
            Total = entry.Total,
        };

        lock (_itemsLock)
        {
            _liveItems.Add(sale);
        }

        _rebuildPending = true;
    }

    protected override bool HasToolSettings => true;
    protected override object? GetToolSettingsSchema() => null;
    protected override object? GetToolSettingsObject() => null;

    protected override void DrawToolSettings()
    {
        var changed = false;

        // World picker in settings
        if (_worldSelector != null)
        {
            _worldSelector.Mode = (WorldSelectionMode)_settings.Scope;

            if (_worldSelector.Draw("Worlds"))
            {
                _settings.Scope = (int)_worldSelector.Mode;
                _settings.SelectedWorldIds = new HashSet<int>(_worldSelector.SelectedWorldIds);
                _settings.SelectedDataCenters = new HashSet<string>(_worldSelector.SelectedDataCenters);
                _settings.SelectedRegions = new HashSet<string>(_worldSelector.SelectedRegions);
                changed = true;

                // Reset state so worlds change triggers a fresh fetch
                _initialLoadComplete = false;
                UnsubscribeFromWebSocket();
                lock (_itemsLock)
                {
                    _baseItems.Clear();
                    _liveItems.Clear();
                }

                FetchData();
            }
        }
        else
        {
            ImGui.TextDisabled("Waiting for world data...");
        }

        // Standard table settings
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Table Settings");
        ImGui.Spacing();

        var freezeHeader = _settings.FreezeHeader;
        if (ImGui.Checkbox("Freeze header row", ref freezeHeader))
        {
            _settings.FreezeHeader = freezeHeader;
            changed = true;
        }

        ImGui.Spacing();
        if (TreeHelpers.DrawSection("Row Colors"))
        {
            changed |= TableHelpers.DrawColorOption("Header", _settings.HeaderColor, c => _settings.HeaderColor = c);
            changed |= TableHelpers.DrawColorOption("Even Rows", _settings.EvenRowColor, c => _settings.EvenRowColor = c);
            changed |= TableHelpers.DrawColorOption("Odd Rows", _settings.OddRowColor, c => _settings.OddRowColor = c);
            TreeHelpers.EndSection();
        }

        // Ignored items management
        if (_settings.IgnoredItems.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted($"Ignored Items ({_settings.IgnoredItems.Count})");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##IgnoredItemsFilter", "Search ignored items...", ref _ignoredItemsFilter, 256);
            ImGui.Spacing();

            int? removeId = null;
            var now = DateTime.UtcNow;
            var filteredIgnored = _settings.IgnoredItems
                .OrderBy(kv => kv.Value.ItemName)
                .Where(kv => string.IsNullOrEmpty(_ignoredItemsFilter) 
                    || (kv.Value.ItemName ?? $"Item #{kv.Key}").Contains(_ignoredItemsFilter, StringComparison.OrdinalIgnoreCase));
            foreach (var (itemId, entry) in filteredIgnored)
            {
                var label = entry.ItemName ?? $"Item #{itemId}";
                if (entry.ExpiresAtUtc != null)
                {
                    var remaining = entry.ExpiresAtUtc.Value - now;
                    if (remaining.TotalHours >= 24)
                        label += $" ({remaining.Days}d {remaining.Hours}h remaining)";
                    else if (remaining.TotalMinutes >= 60)
                        label += $" ({remaining.Hours}h {remaining.Minutes}m remaining)";
                    else if (remaining.TotalSeconds > 0)
                        label += $" ({remaining.Minutes}m remaining)";
                    else
                        label += " (expired)";
                }
                else
                {
                    label += " (permanent)";
                }

                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextUnformatted(label);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Unignore##{itemId}"))
                    removeId = itemId;
            }

            if (removeId.HasValue)
            {
                _settings.IgnoredItems.Remove(removeId.Value);
                changed = true;
            }
        }

        if (changed)
            NotifyToolSettingsChanged();
    }

    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var dict = Schema.ToDictionary(_settings)!;
        dict["SelectedWorldIds"] = _settings.SelectedWorldIds.ToList();
        dict["SelectedDataCenters"] = _settings.SelectedDataCenters.ToList();
        dict["SelectedRegions"] = _settings.SelectedRegions.ToList();
        dict["PinnedItemIds"] = _settings.PinnedItemIds.ToList();

        // Serialize ignored items as list of objects
        var ignoredList = _settings.IgnoredItems.Values.Select(e => new Dictionary<string, object?>
        {
            ["ItemId"] = e.ItemId,
            ["ExpiresAtUtc"] = e.ExpiresAtUtc?.ToString("O"),
            ["ItemName"] = e.ItemName,
        }).ToList();
        dict["IgnoredItems"] = ignoredList;

        // Table settings
        dict["HeaderColor"] = _settings.HeaderColor.HasValue ? new float[] { _settings.HeaderColor.Value.X, _settings.HeaderColor.Value.Y, _settings.HeaderColor.Value.Z, _settings.HeaderColor.Value.W } : null;
        dict["EvenRowColor"] = _settings.EvenRowColor.HasValue ? new float[] { _settings.EvenRowColor.Value.X, _settings.EvenRowColor.Value.Y, _settings.EvenRowColor.Value.Z, _settings.EvenRowColor.Value.W } : null;
        dict["OddRowColor"] = _settings.OddRowColor.HasValue ? new float[] { _settings.OddRowColor.Value.X, _settings.OddRowColor.Value.Y, _settings.OddRowColor.Value.Z, _settings.OddRowColor.Value.W } : null;
        dict["FreezeHeader"] = _settings.FreezeHeader;

        return dict;
    }

    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        Schema.FromDictionary(_settings, settings);
        if (settings != null)
        {
            if (settings.TryGetValue("SelectedWorldIds", out var worldIdsObj))
                _settings.SelectedWorldIds = DeserializeWorldIds(worldIdsObj);
            if (settings.TryGetValue("SelectedDataCenters", out var dcObj))
                _settings.SelectedDataCenters = DeserializeStringSet(dcObj);
            if (settings.TryGetValue("SelectedRegions", out var regionObj))
                _settings.SelectedRegions = DeserializeStringSet(regionObj);
            if (settings.TryGetValue("PinnedItemIds", out var pinnedObj))
                _settings.PinnedItemIds = DeserializeUintSet(pinnedObj);
            if (settings.TryGetValue("IgnoredItems", out var ignoredObj))
                _settings.IgnoredItems = DeserializeIgnoredItems(ignoredObj);

            // Table settings
            _settings.HeaderColor = ImportColorArray(settings, "HeaderColor");
            _settings.EvenRowColor = ImportColorArray(settings, "EvenRowColor");
            _settings.OddRowColor = ImportColorArray(settings, "OddRowColor");
            _settings.FreezeHeader = GetSetting(settings, "FreezeHeader", _settings.FreezeHeader);

            if (_worldSelector != null)
            {
                _worldSelector.Mode = (WorldSelectionMode)_settings.Scope;
                _worldSelector.InitializeFrom(_settings.SelectedRegions, _settings.SelectedDataCenters, _settings.SelectedWorldIds);
            }

            // Restore item picker multi-selection
            if (_itemPicker != null && _settings.PinnedItemIds.Count > 0)
                _itemPicker.SetMultiSelection(_settings.PinnedItemIds);
        }
    }

    private static HashSet<int> DeserializeWorldIds(object? value)
    {
        var result = new HashSet<int>();
        if (value is Newtonsoft.Json.Linq.JArray jArray)
        {
            foreach (var item in jArray)
                result.Add(item.ToObject<int>());
        }
        else if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in jsonElement.EnumerateArray())
                result.Add(item.GetInt32());
        }
        else if (value is IEnumerable<object> enumerable)
        {
            foreach (var item in enumerable)
                result.Add(Convert.ToInt32(item));
        }
        return result;
    }

    private static HashSet<string> DeserializeStringSet(object? value)
    {
        var result = new HashSet<string>();
        if (value is Newtonsoft.Json.Linq.JArray jArray)
        {
            foreach (var item in jArray)
                result.Add(item.ToObject<string>()!);
        }
        else if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in jsonElement.EnumerateArray())
                result.Add(item.GetString()!);
        }
        else if (value is IEnumerable<object> enumerable)
        {
            foreach (var item in enumerable)
                result.Add(item.ToString()!);
        }
        return result;
    }

    private static HashSet<uint> DeserializeUintSet(object? value)
    {
        var result = new HashSet<uint>();
        if (value is Newtonsoft.Json.Linq.JArray jArray)
        {
            foreach (var item in jArray)
                result.Add(item.ToObject<uint>());
        }
        else if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in jsonElement.EnumerateArray())
                result.Add(item.GetUInt32());
        }
        else if (value is IEnumerable<object> enumerable)
        {
            foreach (var item in enumerable)
                result.Add(Convert.ToUInt32(item));
        }
        return result;
    }

    private static Dictionary<int, IgnoredItemEntry> DeserializeIgnoredItems(object? value)
    {
        var result = new Dictionary<int, IgnoredItemEntry>();
        try
        {
            if (value is Newtonsoft.Json.Linq.JArray jArray)
            {
                foreach (var obj in jArray)
                {
                    var entry = new IgnoredItemEntry
                    {
                        ItemId = obj["ItemId"]?.ToObject<int>() ?? 0,
                        ItemName = obj["ItemName"]?.ToObject<string>(),
                    };
                    var expiresStr = obj["ExpiresAtUtc"]?.ToObject<string>();
                    if (!string.IsNullOrEmpty(expiresStr) && DateTime.TryParse(expiresStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                        entry.ExpiresAtUtc = dt;
                    if (entry.ItemId != 0)
                        result[entry.ItemId] = entry;
                }
            }
            else if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in jsonElement.EnumerateArray())
                {
                    var entry = new IgnoredItemEntry
                    {
                        ItemId = el.TryGetProperty("ItemId", out var idEl) ? idEl.GetInt32() : 0,
                        ItemName = el.TryGetProperty("ItemName", out var nameEl) ? nameEl.GetString() : null,
                    };
                    if (el.TryGetProperty("ExpiresAtUtc", out var expEl) && expEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = expEl.GetString();
                        if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                            entry.ExpiresAtUtc = dt;
                    }
                    if (entry.ItemId != 0)
                        result[entry.ItemId] = entry;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"[GilFluxTool] Failed to deserialize IgnoredItems: {ex.Message}");
        }
        return result;
    }

    public override void Dispose()
    {
        UnsubscribeFromWebSocket();
        if (_itemPicker != null)
        {
            _itemPicker.MultiSelectionChanged -= OnItemPickerSelectionChanged;
            _itemPicker.Dispose();
        }
        _cts?.Cancel();
        _cts?.Dispose();
        base.Dispose();
    }
}
