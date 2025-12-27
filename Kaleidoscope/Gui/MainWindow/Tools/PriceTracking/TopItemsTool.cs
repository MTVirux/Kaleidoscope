using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Price info for a top item, used for tooltips.
/// </summary>
public record TopItemPriceInfo(int UnitPrice, int WorldId, DateTime LastUpdated);

/// <summary>
/// Cached market data for tooltip display.
/// </summary>
public record TooltipMarketData(
    MarketListing? LowestListing,
    MarketSale? LatestSale,
    DateTime FetchedAt,
    bool IsLoading = false,
    string? Error = null);

/// <summary>
/// Tool component that shows the top items by value from character inventories.
/// Displays items that contribute the most to total liquid value.
/// </summary>
public class TopItemsTool : ToolComponent
{
    public override string ToolName => "Top Items";
    
    private readonly PriceTrackingService _priceTrackingService;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly ItemDataService _itemDataService;
    private readonly ItemComboDropdown _itemCombo;
    private readonly InventoryChangeService? _inventoryChangeService;
    private readonly ItemDetailsPopup _itemDetailsPopup;

    // Flag for pending refresh (set by event handlers, processed on next Draw)
    private volatile bool _pendingRefresh;

    // Cached data - now includes price info for tooltips
    private List<(int ItemId, long Quantity, long Value, string Name, TopItemPriceInfo? PriceInfo)> _topItems = new();
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
    private readonly TopItemsSettings _instanceSettings;
    
    private TopItemsSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _samplerService.DbService;
    private TimeSeriesCacheService CacheService => _samplerService.CacheService;

    public TopItemsTool(
        PriceTrackingService priceTrackingService,
        SamplerService samplerService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        FavoritesService favoritesService,
        InventoryChangeService? inventoryChangeService = null)
    {
        _priceTrackingService = priceTrackingService;
        _samplerService = samplerService;
        _configService = configService;
        _itemDataService = itemDataService;
        _inventoryChangeService = inventoryChangeService;

        // Initialize instance settings (persisted with layout)
        _instanceSettings = new TopItemsSettings();
        
        // Create item combo for exclusion list (marketable only since we're dealing with prices)
        _itemCombo = new ItemComboDropdown(
            textureProvider,
            dataManager,
            favoritesService,
            priceTrackingService,
            "TopItemsExclude",
            marketableOnly: true,
            configService: _configService,
            trackedDataRegistry: _samplerService.Registry,
            excludeCurrencies: true);

        // Create item details popup for showing listings and sales when clicking an item
        _itemDetailsPopup = new ItemDetailsPopup(
            priceTrackingService.UniversalisService,
            itemDataService,
            priceTrackingService,
            samplerService);

        Title = "Top Items";
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
            var chars = DbService.GetAllCharacterNames()
                .Select(c => (c.characterId, c.name))
                .DistinctBy(c => c.characterId)
                .OrderBy(c => c.name)
                .ToList();

            // Include "All Characters" option
            _characterNames = new string[chars.Count + 1];
            _characterIds = new ulong[chars.Count + 1];

            _characterNames[0] = "All Characters";
            _characterIds[0] = 0;

            for (int i = 0; i < chars.Count; i++)
            {
                // Use formatted character name from cache service
                _characterNames[i + 1] = CacheService.GetFormattedCharacterName(chars[i].characterId)
                    ?? chars[i].name ?? $"Character {chars[i].characterId}";
                _characterIds[i + 1] = chars[i].characterId;
            }

            // Update selected index
            var idx = Array.IndexOf(_characterIds, _selectedCharacterId);
            _selectedCharacterIndex = idx >= 0 ? idx : 0;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[TopItemsTool] Error refreshing characters: {ex.Message}");
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
            _topItems = namedItems.Take(settings.MaxItems).ToList();

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
                var allChars = DbService.GetAllCharacterNames().Select(c => c.characterId).Distinct().ToList();
                
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
            LogService.Debug($"[TopItemsTool] Error refreshing: {ex.Message}");
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
            LogService.Debug($"[TopItemsTool] Draw error: {ex.Message}");
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
        if (ImGui.BeginChild("##TopItemsList", new Vector2(0, 0), false))
        {
            // Gil row first if included
            if (settings.IncludeGil && _gilValue > 0)
            {
                DrawGilRow();
            }

            // Item rows
            if (_topItems.Count == 0)
            {
                ImGui.TextDisabled("No items to display");
                ImGui.TextDisabled("Make sure price tracking is enabled");
            }
            else
            {
                int rank = 1;
                foreach (var item in _topItems)
                {
                    if (item.Value < settings.MinValueThreshold)
                        continue;

                    DrawItemRow(rank++, item);
                }
            }
            ImGui.EndChild();
        }
    }

    private void DrawGilRow()
    {
        var percentage = _totalValue > 0 ? (float)_gilValue / _totalValue * 100 : 0;
        
        // Gold color for gil
        ImGui.TextColored(new Vector4(1f, 0.84f, 0f, 1f), "●");
        ImGui.SameLine();
        ImGui.TextUnformatted("Gil");
        
        // Value and percentage (right-aligned, matching item rows)
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        ImGui.TextUnformatted($"{FormatUtils.FormatGil(_gilValue)} ({percentage:F1}%)");
    }

    private void DrawItemRow(int rank, (int ItemId, long Quantity, long Value, string Name, TopItemPriceInfo? PriceInfo) item)
    {
        var percentage = _totalValue > 0 ? (float)item.Value / _totalValue * 100 : 0;
        
        // Color gradient from green to yellow to orange based on rank (scales with MaxItems setting)
        var maxItems = Math.Max(1, Settings.MaxItems);
        var gradientStep = 0.33f / maxItems;
        var hue = Math.Max(0, 0.33f - (rank * gradientStep));
        var color = FormatUtils.HsvToRgb(hue, 0.8f, 0.9f);

        // Start invisible button for hover detection (covers the whole row)
        var cursorPos = ImGui.GetCursorPos();

        // Rank indicator
        ImGui.TextColored(color, $"#{rank}");
        ImGui.SameLine();

        // Item name and quantity
        var text = Settings.GroupByItem 
            ? $"{item.Name} x{item.Quantity}"
            : item.Name;
        ImGui.TextUnformatted(text);

        // Value and percentage (right-aligned)
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        ImGui.TextUnformatted($"{FormatUtils.FormatGil(item.Value)} ({percentage:F1}%)");

        // Create an invisible button over the row for hover detection and click handling
        var endCursorPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(cursorPos);
        var rowHeight = endCursorPos.Y - cursorPos.Y;
        if (rowHeight < ImGui.GetTextLineHeight()) rowHeight = ImGui.GetTextLineHeight();
        ImGui.InvisibleButton($"##row_{item.ItemId}_{rank}", new Vector2(ImGui.GetContentRegionAvail().X, rowHeight));
        
        // Handle click to open item details popup
        if (ImGui.IsItemClicked())
        {
            _itemDetailsPopup.Open(item.ItemId);
        }
        
        ImGui.SetCursorPos(endCursorPos);

        // Show tooltip on hover
        if (ImGui.IsItemHovered())
        {
            // Trigger async fetch if needed
            EnsureTooltipDataLoaded(item.ItemId);
            DrawItemTooltip(item);
        }
    }

    private void EnsureTooltipDataLoaded(int itemId)
    {
        // Check if we have cached data that's still valid
        if (_tooltipCache.TryGetValue(itemId, out var cached))
        {
            // If loading or data is fresh enough, don't refetch
            if (cached.IsLoading || (DateTime.UtcNow - cached.FetchedAt).TotalMinutes < TooltipCacheExpiryMinutes)
                return;
        }

        // Mark as loading and start fetch
        _tooltipCache[itemId] = new TooltipMarketData(null, null, DateTime.UtcNow, IsLoading: true);
        _ = FetchTooltipDataAsync(itemId);
    }

    private async Task FetchTooltipDataAsync(int itemId)
    {
        try
        {
            var scope = _priceTrackingService.UniversalisService.GetConfiguredScope();
            if (string.IsNullOrEmpty(scope))
            {
                _tooltipCache[itemId] = new TooltipMarketData(null, null, DateTime.UtcNow, Error: "No scope configured");
                return;
            }

            var marketData = await _priceTrackingService.UniversalisService.GetMarketBoardDataAsync(
                scope,
                (uint)itemId,
                listings: 10,
                entries: 10);

            if (marketData == null)
            {
                _tooltipCache[itemId] = new TooltipMarketData(null, null, DateTime.UtcNow, Error: "Failed to fetch");
                return;
            }

            // Get the lowest listing (by price per unit)
            var lowestListing = marketData.Listings?
                .OrderBy(l => l.PricePerUnit)
                .FirstOrDefault();

            // Get the most recent sale
            var latestSale = marketData.RecentHistory?
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefault();

            _tooltipCache[itemId] = new TooltipMarketData(lowestListing, latestSale, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[TopItemsTool] Error fetching tooltip data for item {itemId}: {ex.Message}");
            _tooltipCache[itemId] = new TooltipMarketData(null, null, DateTime.UtcNow, Error: ex.Message);
        }
    }

    private void DrawItemTooltip((int ItemId, long Quantity, long Value, string Name, TopItemPriceInfo? PriceInfo) item)
    {
        ImGui.BeginTooltip();
        
        // Item name header
        ImGui.TextUnformatted(item.Name);
        ImGui.Separator();

        // Basic price info
        if (item.PriceInfo != null)
        {
            ImGui.TextUnformatted($"Unit Price: {FormatUtils.FormatGil(item.PriceInfo.UnitPrice)}");
            ImGui.TextUnformatted($"Quantity: {item.Quantity:N0}");
            ImGui.TextUnformatted($"Total Value: {FormatUtils.FormatGil(item.Value)}");

            // World info
            var worldName = _priceTrackingService.WorldData?.GetWorldName(item.PriceInfo.WorldId);
            if (!string.IsNullOrEmpty(worldName))
            {
                ImGui.TextUnformatted($"Best Price From: {worldName}");
            }

            // Last updated
            var timeSince = DateTime.UtcNow - item.PriceInfo.LastUpdated;
            ImGui.TextDisabled($"Updated: {FormatUtils.FormatTimeAgo(timeSince)}");
        }

        ImGui.Spacing();

        // Check for cached tooltip data
        if (_tooltipCache.TryGetValue(item.ItemId, out var tooltipData))
        {
            if (tooltipData.IsLoading)
            {
                ImGui.TextDisabled("Loading market data...");
            }
            else if (!string.IsNullOrEmpty(tooltipData.Error))
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Error: {tooltipData.Error}");
            }
            else
            {
                // Draw Latest Sale table
                DrawLatestSaleTable(tooltipData.LatestSale);
                
                ImGui.Spacing();
                
                // Draw Lowest Listing table
                DrawLowestListingTable(tooltipData.LowestListing);
            }
        }
        else
        {
            ImGui.TextDisabled("Hover to load market data...");
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Click to view full listings & sales");

        ImGui.EndTooltip();
    }

    private void DrawLatestSaleTable(MarketSale? sale)
    {
        ImGui.TextUnformatted("Latest Sale:");
        
        if (sale == null)
        {
            ImGui.TextDisabled("  No recent sales");
            return;
        }

        if (ImGui.BeginTable("##LatestSaleTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 25);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Buyer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();
            
            // HQ
            ImGui.TableNextColumn();
            if (sale.IsHq)
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.3f, 1f), "★");
            else
                ImGui.TextDisabled("-");
            
            // Price
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatUtils.FormatGil(sale.PricePerUnit));
            
            // Qty
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(sale.Quantity.ToString());
            
            // Total
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatUtils.FormatGil(sale.Total));
            
            // Buyer
            ImGui.TableNextColumn();
            var buyerName = string.IsNullOrEmpty(sale.BuyerName) ? "Unknown" : sale.BuyerName;
            ImGui.TextUnformatted(buyerName);

            ImGui.EndTable();
        }

        // Show world and time
        var saleWorld = sale.WorldName ?? (sale.WorldId.HasValue ? _priceTrackingService.WorldData?.GetWorldName(sale.WorldId.Value) : null);
        var saleTime = FormatUtils.FormatTimeAgo(DateTime.UtcNow - sale.SaleDateTime.ToUniversalTime());
        ImGui.TextDisabled($"  {saleWorld ?? "Unknown World"} • {saleTime}");
    }

    private void DrawLowestListingTable(MarketListing? listing)
    {
        ImGui.TextUnformatted("Lowest Current Listing:");
        
        if (listing == null)
        {
            ImGui.TextDisabled("  No current listings");
            return;
        }

        if (ImGui.BeginTable("##LowestListingTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 25);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();
            
            // HQ
            ImGui.TableNextColumn();
            if (listing.IsHq)
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.3f, 1f), "★");
            else
                ImGui.TextDisabled("-");
            
            // Price
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatUtils.FormatGil(listing.PricePerUnit));
            
            // Qty
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.Quantity.ToString());
            
            // Total
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatUtils.FormatGil(listing.Total));
            
            // Retainer
            ImGui.TableNextColumn();
            var retainerName = string.IsNullOrEmpty(listing.RetainerName) ? "Unknown" : listing.RetainerName;
            ImGui.TextUnformatted(retainerName);

            ImGui.EndTable();
        }

        // Show world
        var listingWorld = listing.WorldName ?? (listing.WorldId.HasValue ? _priceTrackingService.WorldData?.GetWorldName(listing.WorldId.Value) : null);
        ImGui.TextDisabled($"  {listingWorld ?? "Unknown World"}");
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            if (!ImGui.CollapsingHeader("Top Items Settings", ImGuiTreeNodeFlags.DefaultOpen))
                return;
                
            var settings = Settings;

            var maxItems = settings.MaxItems;
            if (ImGui.SliderInt("Max items", ref maxItems, 10, 500))
            {
                settings.MaxItems = maxItems;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Maximum number of items to display.", "100");

            var showAllCharacters = settings.ShowAllCharacters;
            if (ImGui.Checkbox("Show all characters combined", ref showAllCharacters))
            {
                settings.ShowAllCharacters = showAllCharacters;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Combine items from all characters, or select a specific character.", "On");

            var includeRetainers = settings.IncludeRetainers;
            if (ImGui.Checkbox("Include retainer inventories", ref includeRetainers))
            {
                settings.IncludeRetainers = includeRetainers;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Include items from retainer inventories.", "On");

            var includeGil = settings.IncludeGil;
            if (ImGui.Checkbox("Include gil in list", ref includeGil))
            {
                settings.IncludeGil = includeGil;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Show gil as a row in the top items list.", "On");

            var groupByItem = settings.GroupByItem;
            if (ImGui.Checkbox("Group by item", ref groupByItem))
            {
                settings.GroupByItem = groupByItem;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Combine quantities of the same item across inventories.", "On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Filters");
            ImGui.Separator();

            var minThreshold = (int)settings.MinValueThreshold;
            if (ImGui.InputInt("Min value threshold", ref minThreshold, 1000, 10000))
            {
                settings.MinValueThreshold = Math.Max(0, minThreshold);
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Only show items worth at least this much gil.", "0");

            // Item exclusion section
            ImGui.Spacing();
            ImGui.TextUnformatted("Excluded Items");
            ImGui.Separator();

            // Item picker for adding exclusions
            ImGui.TextDisabled("Add item to exclude:");
            if (_itemCombo.Draw(250))
            {
                if (_itemCombo.SelectedItemId > 0)
                {
                    settings.ExcludedItemIds.Add(_itemCombo.SelectedItemId);
                    NotifyToolSettingsChanged();
                    _itemCombo.ClearSelection();
                    _ = Task.Run(RefreshTopItemsAsync);
                }
            }
            ShowSettingTooltip("Select an item to exclude from the top items list.", "");

            // Show current exclusions
            if (settings.ExcludedItemIds.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled($"Currently excluded ({settings.ExcludedItemIds.Count}):");
                
                uint? itemToRemove = null;
                foreach (var itemId in settings.ExcludedItemIds)
                {
                    var itemName = _itemDataService.GetItemName(itemId);
                    ImGui.BulletText(itemName);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"X##{itemId}"))
                    {
                        itemToRemove = itemId;
                    }
                }

                // Remove outside iteration to avoid collection modification
                if (itemToRemove.HasValue)
                {
                    settings.ExcludedItemIds.Remove(itemToRemove.Value);
                    NotifyToolSettingsChanged();
                    _ = Task.Run(RefreshTopItemsAsync);
                }

                // Clear all button
                if (ImGui.Button("Clear All Exclusions"))
                {
                    settings.ExcludedItemIds.Clear();
                    NotifyToolSettingsChanged();
                    _ = Task.Run(RefreshTopItemsAsync);
                }
            }
            else
            {
                ImGui.TextDisabled("No items excluded");
            }

            // Refresh button
            ImGui.Spacing();
            if (ImGui.Button("Refresh Now"))
            {
                _ = Task.Run(RefreshTopItemsAsync);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[TopItemsTool] Settings error: {ex.Message}");
        }
    }

    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["MaxItems"] = Settings.MaxItems,
            ["ShowAllCharacters"] = Settings.ShowAllCharacters,
            ["SelectedCharacterId"] = Settings.SelectedCharacterId,
            ["IncludeRetainers"] = Settings.IncludeRetainers,
            ["IncludeGil"] = Settings.IncludeGil,
            ["MinValueThreshold"] = Settings.MinValueThreshold,
            ["GroupByItem"] = Settings.GroupByItem,
            ["ExcludedItemIds"] = Settings.ExcludedItemIds.ToList()
        };
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        _instanceSettings.MaxItems = GetSetting(settings, "MaxItems", _instanceSettings.MaxItems);
        _instanceSettings.ShowAllCharacters = GetSetting(settings, "ShowAllCharacters", _instanceSettings.ShowAllCharacters);
        _instanceSettings.SelectedCharacterId = GetSetting(settings, "SelectedCharacterId", _instanceSettings.SelectedCharacterId);
        _instanceSettings.IncludeRetainers = GetSetting(settings, "IncludeRetainers", _instanceSettings.IncludeRetainers);
        _instanceSettings.IncludeGil = GetSetting(settings, "IncludeGil", _instanceSettings.IncludeGil);
        _instanceSettings.MinValueThreshold = GetSetting(settings, "MinValueThreshold", _instanceSettings.MinValueThreshold);
        _instanceSettings.GroupByItem = GetSetting(settings, "GroupByItem", _instanceSettings.GroupByItem);
        
        // Deserialize ExcludedItemIds from JsonElement array to HashSet<uint>
        var excludedIds = GetSetting<List<uint>>(settings, "ExcludedItemIds", null);
        if (excludedIds != null)
        {
            _instanceSettings.ExcludedItemIds = new HashSet<uint>(excludedIds);
        }
        
        // Sync selected character from settings
        _selectedCharacterId = _instanceSettings.SelectedCharacterId;
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
