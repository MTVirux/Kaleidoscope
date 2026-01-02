using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using MTGui.Tree;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Price info for a top item, used for tooltips.
/// </summary>
public record TopItemPriceInfo(int UnitPrice, int WorldId, DateTime LastUpdated);

/// <summary>
/// Cached local sale data for tooltip display.
/// </summary>
public record LocalSaleInfo(int WorldId, int PricePerUnit, int Quantity, bool IsHq, DateTime Timestamp);

/// <summary>
/// Cached market data for tooltip display (from local database).
/// </summary>
public record TooltipMarketData(
    List<LocalSaleInfo>? RecentSales,
    DateTime FetchedAt,
    bool IsLoading = false,
    string? Warning = null);

/// <summary>
/// Tool component that shows the top items by value from character inventories.
/// Displays items that contribute the most to total liquid value.
/// 
/// Split into partial classes:
/// - TopInventoryValueTool.cs (this file): Main rendering and data refresh
/// - TopInventoryValueTool.ItemRendering.cs: Item/tooltip rendering
/// - TopInventoryValueTool.Settings.cs: Settings UI and import/export
/// </summary>
public partial class TopInventoryValueTool : ToolComponent
{
    public override string ToolName => "Top Inventory Value Items";
    
    private readonly PriceTrackingService _priceTrackingService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService _configService;
    private readonly CharacterDataService _characterDataService;
    private readonly ItemDataService _itemDataService;
    private readonly MTItemComboDropdown _itemCombo;
    private readonly InventoryChangeService? _inventoryChangeService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly ItemDetailsPopup _itemDetailsPopup;

    // Flag for pending refresh (set by event handlers, processed on next Draw)
    private volatile bool _pendingRefresh;

    // Cached data - now includes price info for tooltips
    private List<(int ItemId, long Quantity, long Value, string Name, TopItemPriceInfo? PriceInfo)> _topInventoryValueItems = new();
    private long _totalValue = 0;
    private long _gilValue = 0;
    private DateTime _lastRefresh = DateTime.MinValue;
    private const int RefreshIntervalSeconds = 30;
    private bool _isRefreshing = false;

    // Cache for tooltip market data (listings and sales)
    private readonly ConcurrentDictionary<int, TooltipMarketData> _tooltipCache = new();
    private const int TooltipCacheExpiryMinutes = 5;

    // Character selection
    private ulong _selectedCharacterId = 0;
    private string[] _characterNames = Array.Empty<string>();
    private ulong[] _characterIds = Array.Empty<ulong>();
    private int _selectedCharacterIndex = 0;
    private CharacterNameFormat _cachedNameFormat;

    // Instance-specific settings (persisted with layout, not global config)
    private readonly TopInventoryValueItemsSettings _instanceSettings;
    
    private TopInventoryValueItemsSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _currencyTrackerService.DbService;
    private TimeSeriesCacheService CacheService => _currencyTrackerService.CacheService;
    private CharacterDataCacheService CharacterDataCache => _currencyTrackerService.CharacterDataCache;

    public TopInventoryValueTool(
        PriceTrackingService priceTrackingService,
        CurrencyTrackerService currencyTrackerService,
        ConfigurationService configService,
        CharacterDataService characterDataService,
        ItemDataService itemDataService,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        FavoritesService favoritesService,
        InventoryChangeService? inventoryChangeService = null,
        InventoryCacheService? inventoryCacheService = null)
    {
        _priceTrackingService = priceTrackingService;
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;
        _characterDataService = characterDataService;
        _itemDataService = itemDataService;
        _inventoryChangeService = inventoryChangeService;
        _inventoryCacheService = inventoryCacheService;

        // Initialize instance settings (persisted with layout)
        _instanceSettings = new TopInventoryValueItemsSettings();
        
        // Create item combo for exclusion list (marketable only since we're dealing with prices)
        _itemCombo = new MTItemComboDropdown(
            textureProvider,
            dataManager,
            favoritesService,
            priceTrackingService,
            "TopInventoryValueItemsExclude",
            marketableOnly: true,
            configService: _configService,
            trackedDataRegistry: _currencyTrackerService.Registry,
            excludeCurrencies: true);

        // Create item details popup for showing listings and sales when clicking an item
        _itemDetailsPopup = new ItemDetailsPopup(
            priceTrackingService.UniversalisService,
            itemDataService,
            priceTrackingService,
            currencyTrackerService,
            inventoryCacheService,
            characterDataService);

        Title = "Top Inventory Value Items";
        Size = new Vector2(400, 350);

        // Subscribe to inventory change events for automatic refresh
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnValuesChanged += OnInventoryChanged;
        }

        // Subscribe to price update events for automatic refresh when prices change
        _priceTrackingService.OnPriceDataUpdated += OnPriceDataUpdated;

        RefreshCharacterList();
    }

    private void OnInventoryChanged(IReadOnlyDictionary<Models.TrackedDataType, long> changedValues)
    {
        // Flag that inventory has changed - will trigger refresh on next Draw()
        _pendingRefresh = true;
    }

    private void OnPriceDataUpdated(int itemId)
    {
        // Flag that price data has changed - will trigger refresh on next Draw()
        _pendingRefresh = true;
    }

    private void RefreshCharacterList()
    {
        try
        {
            var (names, ids) = _characterDataService.GetCharacterArrays(includeAllCharactersOption: true);
            _characterNames = names;
            _characterIds = ids;

            // Update selected index
            var idx = Array.IndexOf(_characterIds, _selectedCharacterId);
            _selectedCharacterIndex = idx >= 0 ? idx : 0;
        }
        catch (Exception ex)
        {
            LogDebug($"Error refreshing characters: {ex.Message}");
        }
    }

    private async Task RefreshTopItemsAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        try
        {
            var settings = Settings;
            var charId = settings.ShowAllCharacters ? (ulong?)null : 
                (_selectedCharacterId == 0 ? (ulong?)null : _selectedCharacterId);

            // Get top items (request more to account for filtering)
            var requestCount = settings.MaxItems + settings.ExcludedItemIds.Count;
            var items = await _priceTrackingService.GetTopItemsByValueAsync(
                charId,
                requestCount,
                settings.IncludeRetainers);

            // Get detailed price info for all items (for tooltips)
            var itemIds = items.Select(i => i.ItemId).ToList();
            var detailedPrices = DbService.GetItemPricesDetailedBatch(itemIds);

            // Resolve item names using ItemDataService and filter out excluded items
            var namedItems = new List<(int ItemId, long Quantity, long Value, string Name, TopItemPriceInfo? PriceInfo)>();
            foreach (var (itemId, qty, value) in items)
            {
                // Skip excluded items
                if (settings.ExcludedItemIds.Contains((uint)itemId))
                    continue;

                var name = _itemDataService.GetItemName(itemId);
                
                // Get price info for tooltip
                TopItemPriceInfo? priceInfo = null;
                if (detailedPrices.TryGetValue(itemId, out var priceData))
                {
                    priceInfo = new TopItemPriceInfo(priceData.MinPrice, priceData.WorldId, priceData.LastUpdated);
                }

                namedItems.Add((itemId, qty, value, name, priceInfo));
            }

            // Limit to MaxItems after filtering
            _topInventoryValueItems = namedItems.Take(settings.MaxItems).ToList();

            // Get gil value
            if (charId.HasValue)
            {
                var (total, gil, item, _) = await _priceTrackingService.CalculateInventoryValueAsync(charId.Value, settings.IncludeRetainers);
                _totalValue = total;
                _gilValue = gil;
            }
            else
            {
                // Calculate for all characters in parallel for better CPU utilization
                // Get character list from cache (no DB access)
                var allChars = CharacterDataCache.GetAllCharacterIds();
                
                if (allChars.Count > 0)
                {
                    var includeRetainers = settings.IncludeRetainers;
                    var tasks = allChars.Select(async cid =>
                    {
                        var (total, gil, item, _) = await _priceTrackingService.CalculateInventoryValueAsync(cid, includeRetainers);
                        return (total, gil);
                    }).ToList();

                    var results = await Task.WhenAll(tasks);
                    
                    _gilValue = results.Sum(r => r.gil);
                    _totalValue = results.Sum(r => r.total);
                }
                else
                {
                    _gilValue = 0;
                    _totalValue = 0;
                }
            }

            _lastRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LogDebug($"Error refreshing: {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public override void RenderToolContent()
    {
        try
        {
            // Check if name format changed - refresh character list
            var currentFormat = _configService.Config.CharacterNameFormat;
            if (_cachedNameFormat != currentFormat)
            {
                _cachedNameFormat = currentFormat;
                RefreshCharacterList();
            }
            
            // Auto-refresh on pending changes or time interval
            if (_pendingRefresh || (DateTime.UtcNow - _lastRefresh).TotalSeconds > RefreshIntervalSeconds)
            {
                _pendingRefresh = false;
                _ = Task.Run(RefreshTopItemsAsync);
            }

            // Header with character selector and totals
            using (ProfilerService.BeginStaticChildScope("DrawHeader"))
            {
                DrawHeader();
            }

            ImGui.Separator();

            // Items list
            using (ProfilerService.BeginStaticChildScope("DrawItemsList"))
            {
                DrawItemsList();
            }

            // Draw the item details popup (if open)
            _itemDetailsPopup.Draw();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    private void DrawHeader()
    {
        var settings = Settings;

        // Character selector (only if not "All" mode)
        if (!settings.ShowAllCharacters && _characterNames.Length > 0)
        {
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("##CharSelector", ref _selectedCharacterIndex, _characterNames, _characterNames.Length))
            {
                _selectedCharacterId = _characterIds[_selectedCharacterIndex];
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ImGui.SameLine();
        }

        // Totals
        if (settings.IncludeGil)
        {
            ImGui.TextUnformatted($"Total: {FormatUtils.FormatGil(_totalValue)} (Gil: {FormatUtils.FormatGil(_gilValue)})");
        }
        else
        {
            var itemValue = _totalValue - _gilValue;
            ImGui.TextUnformatted($"Item Value: {FormatUtils.FormatGil(itemValue)}");
        }

        // Refresh button - hidden when socket is connected for real-time updates
        if (!_priceTrackingService.IsSocketConnected)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(_isRefreshing ? "..." : "↻"))
            {
                _ = Task.Run(RefreshTopItemsAsync);
            }
        }
    }

    private void DrawItemsList()
    {
        var settings = Settings;

        // Use 0 height to auto-fit content and avoid unnecessary scrollbar
        if (ImGui.BeginChild("##TopInventoryValueItemsList", new Vector2(0, 0), false))
        {
            // Gil row first if included
            if (settings.IncludeGil && _gilValue > 0)
            {
                DrawGilRow();
            }

            // Item rows
            if (_topInventoryValueItems.Count == 0)
            {
                ImGui.TextDisabled("No items to display");
                ImGui.TextDisabled("Make sure price tracking is enabled");
            }
            else
            {
                int rank = 1;
                foreach (var item in _topInventoryValueItems)
                {
                    if (item.Value < settings.MinValueThreshold)
                        continue;

                    DrawItemRow(rank++, item);
                }
            }
            ImGui.EndChild();
        }
    }

    public override void Dispose()
    {
        // Unsubscribe from events
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnValuesChanged -= OnInventoryChanged;
        }
        _priceTrackingService.OnPriceDataUpdated -= OnPriceDataUpdated;
    }
}

