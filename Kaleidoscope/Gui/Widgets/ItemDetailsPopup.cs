using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models.Inventory;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Data for a single inventory row showing item distribution per character/retainer.
/// </summary>
public class ItemInventoryRow
{
    public ulong CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string? WorldName { get; set; }
    public bool IsRetainer { get; set; }
    public ulong RetainerId { get; set; }
    public string? RetainerName { get; set; }
    public int Quantity { get; set; }
    public long UnitPrice { get; set; }
    public long TotalValue { get; set; }
}

/// <summary>
/// A popup window that displays current listings and recent sales for a market item.
/// </summary>
public class ItemDetailsPopup
{
    private readonly UniversalisService _universalisService;
    private readonly ItemDataService _itemDataService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly CurrencyTrackerService? _currencyTrackerService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly CharacterDataService? _characterDataService;
    private readonly SalePriceCacheService? _salePriceCacheService;

    // Current state
    private bool _isOpen;
    private uint _currentItemId;
    private string _currentItemName = string.Empty;
    private MarketBoardData? _marketData;
    private bool _isLoading;
    private string? _errorMessage;

    // Display settings
    private int _maxListings = 20;
    private int _maxSales = 20;

    // World selector state (used when no character is logged in)
    private string? _selectedScope;
    private string[] _scopeOptions = Array.Empty<string>();
    private int _selectedScopeIndex;
    private bool _scopeOptionsLoaded;

    // Local sales state (from database)
    private List<(long Id, int WorldId, int PricePerUnit, int Quantity, bool IsHq, int Total, DateTime Timestamp, string? BuyerName)> _localSales = new();
    private bool _localSalesLoaded;
    private bool _localSalesLoading;

    // Inventory distribution state
    private List<ItemInventoryRow> _inventoryRows = new();
    private bool _inventoryLoaded;
    private bool _inventoryLoading;
    private long _inventoryUnitPrice;
    private int _inventoryTotalQuantity;
    private long _inventoryTotalValue;

    // Focus state - used to bring window to front when opened
    private bool _shouldFocus;

    /// <summary>
    /// Creates a new ItemDetailsPopup.
    /// </summary>
    public ItemDetailsPopup(
        UniversalisService universalisService,
        ItemDataService itemDataService,
        PriceTrackingService priceTrackingService,
        CurrencyTrackerService? CurrencyTrackerService = null,
        InventoryCacheService? inventoryCacheService = null,
        CharacterDataService? characterDataService = null,
        SalePriceCacheService? salePriceCacheService = null)
    {
        _universalisService = universalisService;
        _itemDataService = itemDataService;
        _priceTrackingService = priceTrackingService;
        _currencyTrackerService = CurrencyTrackerService;
        _inventoryCacheService = inventoryCacheService;
        _characterDataService = characterDataService;
        _salePriceCacheService = salePriceCacheService;
    }

    /// <summary>
    /// Opens the popup for a specific item.
    /// </summary>
    /// <param name="itemId">The item ID to show details for.</param>
    public void Open(int itemId)
    {
        _currentItemId = (uint)itemId;
        _currentItemName = _itemDataService.GetItemName(itemId);
        _isOpen = true;
        _shouldFocus = true;
        _marketData = null;
        _errorMessage = null;
        _localSales.Clear();
        _localSalesLoaded = false;
        _localSalesLoading = false;
        _inventoryRows.Clear();
        _inventoryLoaded = false;
        _inventoryLoading = false;
        _inventoryUnitPrice = 0;
        _inventoryTotalQuantity = 0;
        _inventoryTotalValue = 0;

        // Start async loading for all data sources
        _ = FetchMarketDataAsync();
        _ = LoadInventoryDataAsync();
        _ = LoadLocalSalesAsync();
    }

    /// <summary>
    /// Closes the popup.
    /// </summary>
    public void Close()
    {
        _isOpen = false;
        _currentItemId = 0;
        _currentItemName = string.Empty;
        _marketData = null;
    }

    /// <summary>
    /// Draws the popup. Should be called every frame.
    /// </summary>
    public void Draw()
    {
        if (!_isOpen)
            return;

        // Set up the popup window
        ImGui.SetNextWindowSize(new Vector2(550, 450), ImGuiCond.FirstUseEver);
        
        // Bring window to front when first opened
        if (_shouldFocus)
        {
            ImGui.SetNextWindowFocus();
            _shouldFocus = false;
        }
        
        var windowFlags = ImGuiWindowFlags.NoCollapse;
        if (ImGui.Begin($"{_currentItemName}###ItemDetailsPopup", ref _isOpen, windowFlags))
        {
            DrawContent();
        }
        ImGui.End();

        // If window was closed via the X button
        if (!_isOpen)
        {
            Close();
        }
    }

    private void DrawContent()
    {
        // Header with item info
        DrawHeader();

        ImGui.Separator();

        // Tabs for Listings and Sales
        if (ImGui.BeginTabBar("ItemDetailsTabs"))
        {
            // Inventory tab - show item distribution across characters/retainers
            if (_inventoryCacheService != null)
            {
                var inventoryFlags = ImGuiTabItemFlags.None;
                // Set as default on first open
                if (!_inventoryLoaded)
                {
                    inventoryFlags = ImGuiTabItemFlags.SetSelected;
                }
                if (ImGui.BeginTabItem("Inventory", inventoryFlags))
                {
                    DrawInventoryTab();
                    ImGui.EndTabItem();
                }
            }

            // Local Sales is the default tab when available
            if (_currencyTrackerService != null)
            {
                var localSalesFlags = ImGuiTabItemFlags.None;
                if (ImGui.BeginTabItem("Local Sales", localSalesFlags))
                {
                    DrawLocalSalesTab();
                    ImGui.EndTabItem();
                }
            }

            if (ImGui.BeginTabItem("Current Listings"))
            {
                DrawListingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Recent Sales"))
            {
                DrawSalesTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        // Item name with refresh button
        ImGui.TextUnformatted(_currentItemName);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30);
        
        if (ImGui.Button(_isLoading ? "..." : "↻"))
        {
            _ = FetchMarketDataAsync();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Refresh market data");
        }

        // Check if we have a configured scope (character logged in or override set)
        var configuredScope = _universalisService.GetConfiguredScope();
        
        // Show world selector if no character is logged in
        if (string.IsNullOrEmpty(configuredScope))
        {
            DrawWorldSelector();
        }

        // Summary info if data is loaded
        if (_marketData != null)
        {
            var displayScope = configuredScope ?? _selectedScope ?? "Unknown";
            ImGui.TextDisabled($"Scope: {displayScope} | Last Updated: {FormatUtils.FormatTimeAgo(_marketData.LastUploadDateTime)}");

            // Price summary
            if (_marketData.MinPrice > 0)
            {
                ImGui.TextUnformatted($"Min: {FormatUtils.FormatGil(_marketData.MinPrice)}");
                ImGui.SameLine();
                if (_marketData.MinPriceNQ != _marketData.MinPriceHQ)
                {
                    ImGui.TextDisabled($"(NQ: {FormatUtils.FormatGil(_marketData.MinPriceNQ)}, HQ: {FormatUtils.FormatGil(_marketData.MinPriceHQ)})");
                }
            }

            // Sale velocity
            if (_marketData.RegularSaleVelocity > 0)
            {
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
                ImGui.TextDisabled($"~{_marketData.RegularSaleVelocity:F1} sales/day");
            }
        }
    }

    private void DrawWorldSelector()
    {
        // Load scope options from world data if not yet loaded
        if (!_scopeOptionsLoaded)
        {
            LoadScopeOptions();
        }

        if (_scopeOptions.Length == 0)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1), "No character logged in. World data not available.");
            return;
        }

        ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1), "No character logged in.");
        ImGui.SameLine();
        ImGui.TextUnformatted("Select scope:");
        ImGui.SameLine();
        
        ImGui.SetNextItemWidth(180);
        if (ImGui.Combo("##ScopeSelector", ref _selectedScopeIndex, _scopeOptions, _scopeOptions.Length))
        {
            _selectedScope = ExtractScopeName(_scopeOptions[_selectedScopeIndex]);
            // Refetch data with new scope
            _ = FetchMarketDataAsync();
        }
    }

    private void LoadScopeOptions()
    {
        _scopeOptionsLoaded = true;
        
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null)
        {
            _scopeOptions = Array.Empty<string>();
            return;
        }

        var options = new List<string>();
        
        // Add regions first
        foreach (var region in worldData.Regions)
        {
            options.Add($"[Region] {region}");
        }
        
        // Add data centers
        foreach (var dc in worldData.DataCenters.OrderBy(d => d.Name))
        {
            if (!string.IsNullOrEmpty(dc.Name))
            {
                options.Add($"[DC] {dc.Name}");
            }
        }
        
        // Add worlds
        foreach (var world in worldData.Worlds.OrderBy(w => w.Name))
        {
            if (!string.IsNullOrEmpty(world.Name))
            {
                options.Add(world.Name);
            }
        }

        _scopeOptions = options.ToArray();
        
        // Default to first option if available
        if (_scopeOptions.Length > 0 && string.IsNullOrEmpty(_selectedScope))
        {
            _selectedScopeIndex = 0;
            _selectedScope = ExtractScopeName(_scopeOptions[0]);
        }
    }

    private static string ExtractScopeName(string option)
    {
        // Remove [Region] or [DC] prefix if present
        if (option.StartsWith("[Region] "))
            return option.Substring(9);
        if (option.StartsWith("[DC] "))
            return option.Substring(5);
        return option;
    }

    private void DrawListingsTab()
    {
        if (_isLoading)
        {
            ImGui.TextDisabled("Loading listings...");
            return;
        }

        if (!string.IsNullOrEmpty(_errorMessage))
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), _errorMessage);
            return;
        }

        if (_marketData?.Listings == null || _marketData.Listings.Count == 0)
        {
            ImGui.TextDisabled("No listings available for this item.");
            return;
        }

        // Summary
        ImGui.TextUnformatted($"{_marketData.ListingsCount} listings | {_marketData.UnitsForSale:N0} units for sale");
        ImGui.Spacing();

        // Table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        var availHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("ListingsTable", 5, tableFlags, new Vector2(0, availHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            var listings = _marketData.Listings.Take(_maxListings);
            foreach (var listing in listings)
            {
                ImGui.TableNextRow();

                // Price (with HQ indicator)
                ImGui.TableNextColumn();
                var priceText = FormatUtils.FormatGil(listing.PricePerUnit);
                if (listing.IsHq)
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), priceText);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("High Quality");
                    }
                }
                else
                {
                    ImGui.TextUnformatted(priceText);
                }

                // Quantity
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(listing.Quantity.ToString());

                // Total
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatUtils.FormatGil(listing.Total));

                // World
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(listing.WorldName ?? "Unknown");

                // Retainer
                ImGui.TableNextColumn();
                ImGui.TextDisabled(listing.RetainerName ?? "-");
            }

            ImGui.EndTable();
        }
    }

    private void DrawSalesTab()
    {
        if (_isLoading)
        {
            ImGui.TextDisabled("Loading sales history...");
            return;
        }

        if (!string.IsNullOrEmpty(_errorMessage))
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), _errorMessage);
            return;
        }

        if (_marketData?.RecentHistory == null || _marketData.RecentHistory.Count == 0)
        {
            ImGui.TextDisabled("No recent sales for this item.");
            return;
        }

        // Summary
        var sales = _marketData.RecentHistory;
        var avgPrice = sales.Count > 0 ? sales.Average(s => s.PricePerUnit) : 0;
        ImGui.TextUnformatted($"{sales.Count} recent sales | Avg: {FormatUtils.FormatGil((long)avgPrice)}");
        ImGui.Spacing();

        // Table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        var availHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("SalesTable", 5, tableFlags, new Vector2(0, availHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            var salesList = sales.Take(_maxSales);
            foreach (var sale in salesList)
            {
                ImGui.TableNextRow();

                // Time
                ImGui.TableNextColumn();
                var timeAgo = FormatUtils.FormatTimeAgo(sale.SaleDateTime);
                ImGui.TextUnformatted(timeAgo);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(sale.SaleDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                // Price (with HQ indicator)
                ImGui.TableNextColumn();
                var priceText = FormatUtils.FormatGil(sale.PricePerUnit);
                if (sale.IsHq)
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), priceText);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("High Quality");
                    }
                }
                else
                {
                    ImGui.TextUnformatted(priceText);
                }

                // Quantity
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sale.Quantity.ToString());

                // Total
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatUtils.FormatGil(sale.Total));

                // World
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sale.WorldName ?? "Unknown");
            }

            ImGui.EndTable();
        }
    }

    private void DrawLocalSalesTab()
    {
        if (_currencyTrackerService == null)
        {
            ImGui.TextDisabled("Database service not available.");
            return;
        }

        // Show loading state
        if (_localSalesLoading)
        {
            ImGui.TextDisabled("Loading local sales...");
            return;
        }

        // Trigger async load if not yet started
        if (!_localSalesLoaded && !_localSalesLoading)
        {
            _ = LoadLocalSalesAsync();
        }

        if (_localSales.Count == 0)
        {
            ImGui.TextDisabled("No local sales recorded for this item.");
            ImGui.TextDisabled("Sales are recorded from WebSocket updates when tracking is enabled.");
            return;
        }

        // Summary
        var avgPrice = _localSales.Count > 0 ? _localSales.Average(s => s.PricePerUnit) : 0;
        ImGui.TextUnformatted($"{_localSales.Count} recorded sales | Avg: {FormatUtils.FormatGil((long)avgPrice)}");
        ImGui.Spacing();

        // Table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        var availHeight = ImGui.GetContentRegionAvail().Y;

        (long Id, DateTime Timestamp)? saleToDelete = null;

        if (ImGui.BeginTable("LocalSalesTable", 5, tableFlags, new Vector2(0, availHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var sale in _localSales)
            {
                ImGui.TableNextRow();

                // Make the row selectable for right-click context menu
                ImGui.TableNextColumn();
                var timeAgo = FormatUtils.FormatTimeAgo(sale.Timestamp.ToLocalTime());
                
                // Use Selectable spanning all columns for right-click detection
                ImGui.PushID(sale.Id.GetHashCode());
                if (ImGui.Selectable($"{timeAgo}##row", false, ImGuiSelectableFlags.SpanAllColumns))
                {
                    // Left click does nothing for now
                }
                
                // Right-click context menu
                if (ImGui.BeginPopupContextItem($"sale_context_{sale.Id}"))
                {
                    var worldName = _priceTrackingService.WorldData?.GetWorldName(sale.WorldId) ?? $"World {sale.WorldId}";
                    ImGui.TextDisabled($"{FormatUtils.FormatGil(sale.PricePerUnit)} x{sale.Quantity} on {worldName}");
                    ImGui.Separator();
                    
                    if (ImGui.MenuItem("Delete Sale Record"))
                    {
                        saleToDelete = (sale.Id, sale.Timestamp);
                    }
                    
                    ImGui.EndPopup();
                }
                ImGui.PopID();
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"{sale.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}\nRight-click to delete");
                }

                // Price (with HQ indicator)
                ImGui.TableNextColumn();
                var priceText = FormatUtils.FormatGil(sale.PricePerUnit);
                if (sale.IsHq)
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), priceText);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("High Quality");
                    }
                }
                else
                {
                    ImGui.TextUnformatted(priceText);
                }

                // Quantity
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sale.Quantity.ToString());

                // Total
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatUtils.FormatGil(sale.Total));

                // World
                ImGui.TableNextColumn();
                var worldNameCol = _priceTrackingService.WorldData?.GetWorldName(sale.WorldId) ?? $"World {sale.WorldId}";
                ImGui.TextUnformatted(worldNameCol);
            }

            ImGui.EndTable();
        }

        // Handle deletion outside the loop to avoid modifying collection while iterating
        if (saleToDelete.HasValue)
        {
            DeleteLocalSale(saleToDelete.Value.Id, saleToDelete.Value.Timestamp);
        }
    }

    private void DeleteLocalSale(long saleId, DateTime saleTimestamp)
    {
        if (_currencyTrackerService == null) return;

        try
        {
            // Use the method that also cleans up inventory value history
            if (_currencyTrackerService.DbService.DeleteSaleRecordWithHistoryCleanup(saleId, saleTimestamp))
            {
                // Reload the sales list
                _localSalesLoaded = false;
                LoadLocalSales();
                
                // Notify that inventory value history was modified
                _currencyTrackerService.NotifyInventoryValueHistoryChanged();
                
                LogService.Debug($"[ItemDetailsPopup] Deleted sale record {saleId} and cleaned up history after {saleTimestamp}");
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemDetailsPopup] Error deleting sale: {ex.Message}");
        }
    }

    private void LoadLocalSales()
    {
        // Synchronous version for reload after delete
        _localSales.Clear();

        if (_currencyTrackerService == null || _currentItemId == 0)
            return;

        try
        {
            var sales = _currencyTrackerService.DbService.GetSaleRecords((int)_currentItemId, limit: 100);
            _localSales = sales;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemDetailsPopup] Error loading local sales: {ex.Message}");
        }
    }

    private async Task LoadLocalSalesAsync()
    {
        if (_localSalesLoading || _localSalesLoaded)
            return;

        _localSalesLoading = true;

        try
        {
            var itemId = _currentItemId;
            var currencyService = _currencyTrackerService;

            if (currencyService == null || itemId == 0)
            {
                _localSalesLoaded = true;
                _localSalesLoading = false;
                return;
            }

            var sales = await Task.Run(() => 
                currencyService.DbService.GetSaleRecords((int)itemId, limit: 100)
            ).ConfigureAwait(false);

            _localSales = sales;
            _localSalesLoaded = true;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemDetailsPopup] Error loading local sales: {ex.Message}");
            _localSalesLoaded = true;
        }
        finally
        {
            _localSalesLoading = false;
        }
    }

    private void DrawInventoryTab()
    {
        if (_inventoryCacheService == null)
        {
            ImGui.TextDisabled("Inventory cache service not available.");
            return;
        }

        // Show loading state
        if (_inventoryLoading)
        {
            ImGui.TextDisabled("Loading inventory data...");
            return;
        }

        // Trigger async load if not yet started
        if (!_inventoryLoaded && !_inventoryLoading)
        {
            _ = LoadInventoryDataAsync();
        }

        if (_inventoryRows.Count == 0)
        {
            ImGui.TextDisabled("You don't own any of this item.");
            return;
        }

        // Summary
        ImGui.TextUnformatted($"Total: {_inventoryTotalQuantity:N0} owned | Value: {FormatUtils.FormatGil(_inventoryTotalValue)}");
        if (_inventoryUnitPrice > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"(@ {FormatUtils.FormatGil(_inventoryUnitPrice)} each)");
        }
        ImGui.Spacing();

        // Table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        var availHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("InventoryTable", 4, tableFlags, new Vector2(0, availHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Character / Retainer", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Unit Price", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableHeadersRow();

            ulong lastCharacterId = 0;
            foreach (var row in _inventoryRows)
            {
                ImGui.TableNextRow();

                // Name column
                ImGui.TableNextColumn();
                if (row.IsRetainer)
                {
                    // Indent retainers under their character
                    ImGui.TextDisabled("  └");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(row.RetainerName ?? "Unknown Retainer");
                }
                else
                {
                    // Character row - show with world
                    var displayName = !string.IsNullOrEmpty(row.WorldName)
                        ? $"{row.CharacterName} @ {row.WorldName}"
                        : row.CharacterName;
                    ImGui.TextUnformatted(displayName);
                    lastCharacterId = row.CharacterId;
                }

                // Quantity
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.Quantity.ToString("N0"));

                // Unit Price
                ImGui.TableNextColumn();
                if (row.UnitPrice > 0)
                {
                    ImGui.TextUnformatted(FormatUtils.FormatGil(row.UnitPrice));
                }
                else
                {
                    ImGui.TextDisabled("-");
                }

                // Value
                ImGui.TableNextColumn();
                if (row.TotalValue > 0)
                {
                    ImGui.TextUnformatted(FormatUtils.FormatGil(row.TotalValue));
                }
                else
                {
                    ImGui.TextDisabled("-");
                }
            }

            ImGui.EndTable();
        }
    }

    private void LoadInventoryData()
    {
        _inventoryLoaded = true;
        _inventoryRows.Clear();
        _inventoryUnitPrice = 0;
        _inventoryTotalQuantity = 0;
        _inventoryTotalValue = 0;

        if (_inventoryCacheService == null || _currentItemId == 0)
            return;

        try
        {
            // Get all inventories across all characters
            var allInventories = _inventoryCacheService.GetAllInventories();
            
            // Get the current price for this item using cache
            long unitPrice = 0;
            if (_salePriceCacheService != null)
            {
                var prices = _salePriceCacheService.GetLatestSalePrices(
                    new[] { (int)_currentItemId }, 
                    includedWorldIds: null);
                if (prices.TryGetValue((int)_currentItemId, out var price))
                {
                    unitPrice = price.LastSaleNq > 0 ? price.LastSaleNq : price.LastSaleHq;
                }
            }
            _inventoryUnitPrice = unitPrice;

            // Group by character, then by player/retainer
            var characterGroups = allInventories
                .GroupBy(i => i.CharacterId)
                .OrderBy(g => g.First().Name ?? string.Empty);

            foreach (var charGroup in characterGroups)
            {
                var characterId = charGroup.Key;
                var characterName = string.Empty;
                var worldName = string.Empty;

                // Get character display name
                if (_characterDataService != null)
                {
                    var charInfo = _characterDataService.GetCharacter(characterId);
                    if (charInfo != null)
                    {
                        characterName = charInfo.Name;
                        worldName = charInfo.WorldName ?? string.Empty;
                    }
                }

                // Fallback to inventory cache name
                if (string.IsNullOrEmpty(characterName))
                {
                    var playerCache = charGroup.FirstOrDefault(c => c.SourceType == InventorySourceType.Player);
                    characterName = playerCache?.Name ?? $"Character {characterId}";
                    worldName = playerCache?.World ?? string.Empty;
                }

                // Calculate player inventory quantity
                var playerInventories = charGroup.Where(c => c.SourceType == InventorySourceType.Player);
                var playerQuantity = playerInventories
                    .SelectMany(c => c.Items)
                    .Where(i => i.ItemId == _currentItemId)
                    .Sum(i => i.Quantity);

                // Calculate retainer quantities
                var retainerData = charGroup
                    .Where(c => c.SourceType == InventorySourceType.Retainer)
                    .Select(r => new
                    {
                        RetainerId = r.RetainerId,
                        RetainerName = r.Name,
                        Quantity = r.Items.Where(i => i.ItemId == _currentItemId).Sum(i => i.Quantity)
                    })
                    .Where(r => r.Quantity > 0)
                    .OrderBy(r => r.RetainerName)
                    .ToList();

                // Skip this character if they have no items
                var totalCharQuantity = playerQuantity + retainerData.Sum(r => r.Quantity);
                if (totalCharQuantity == 0)
                    continue;

                // Add character row (showing player inventory only)
                if (playerQuantity > 0)
                {
                    var playerValue = unitPrice * playerQuantity;
                    _inventoryRows.Add(new ItemInventoryRow
                    {
                        CharacterId = characterId,
                        CharacterName = characterName,
                        WorldName = worldName,
                        IsRetainer = false,
                        Quantity = playerQuantity,
                        UnitPrice = unitPrice,
                        TotalValue = playerValue
                    });
                    _inventoryTotalQuantity += playerQuantity;
                    _inventoryTotalValue += playerValue;
                }
                else
                {
                    // Add a character header row with 0 quantity if they only have retainer items
                    _inventoryRows.Add(new ItemInventoryRow
                    {
                        CharacterId = characterId,
                        CharacterName = characterName,
                        WorldName = worldName,
                        IsRetainer = false,
                        Quantity = 0,
                        UnitPrice = 0,
                        TotalValue = 0
                    });
                }

                // Add retainer rows
                foreach (var retainer in retainerData)
                {
                    var retainerValue = unitPrice * retainer.Quantity;
                    _inventoryRows.Add(new ItemInventoryRow
                    {
                        CharacterId = characterId,
                        CharacterName = characterName,
                        WorldName = worldName,
                        IsRetainer = true,
                        RetainerId = retainer.RetainerId,
                        RetainerName = retainer.RetainerName,
                        Quantity = retainer.Quantity,
                        UnitPrice = unitPrice,
                        TotalValue = retainerValue
                    });
                    _inventoryTotalQuantity += retainer.Quantity;
                    _inventoryTotalValue += retainerValue;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemDetailsPopup] Error loading inventory data: {ex.Message}");
        }
    }

    private async Task LoadInventoryDataAsync()
    {
        if (_inventoryLoading || _inventoryLoaded)
            return;

        _inventoryLoading = true;

        try
        {
            var itemId = _currentItemId;
            var inventoryCacheService = _inventoryCacheService;
            var currencyService = _currencyTrackerService;
            var characterDataService = _characterDataService;

            if (inventoryCacheService == null || itemId == 0)
            {
                _inventoryLoaded = true;
                _inventoryLoading = false;
                return;
            }

            // Capture the cache service reference for the background thread
            var salePriceCacheService = _salePriceCacheService;

            // Offload the data processing to a background thread
            var result = await Task.Run(() =>
            {
                var rows = new List<ItemInventoryRow>();
                long unitPrice = 0;
                int totalQuantity = 0;
                long totalValue = 0;

                // Get all inventories across all characters
                var allInventories = inventoryCacheService.GetAllInventories();

                // Get the current price for this item using cache
                if (salePriceCacheService != null)
                {
                    var prices = salePriceCacheService.GetLatestSalePrices(
                        new[] { (int)itemId },
                        includedWorldIds: null);
                    if (prices.TryGetValue((int)itemId, out var price))
                    {
                        unitPrice = price.LastSaleNq > 0 ? price.LastSaleNq : price.LastSaleHq;
                    }
                }

                // Group by character, then by player/retainer
                var characterGroups = allInventories
                    .GroupBy(i => i.CharacterId)
                    .OrderBy(g => g.First().Name ?? string.Empty);

                foreach (var charGroup in characterGroups)
                {
                    var characterId = charGroup.Key;
                    var characterName = string.Empty;
                    var worldName = string.Empty;

                    // Get character display name
                    if (characterDataService != null)
                    {
                        var charInfo = characterDataService.GetCharacter(characterId);
                        if (charInfo != null)
                        {
                            characterName = charInfo.Name;
                            worldName = charInfo.WorldName ?? string.Empty;
                        }
                    }

                    // Fallback to inventory cache name
                    if (string.IsNullOrEmpty(characterName))
                    {
                        var playerCache = charGroup.FirstOrDefault(c => c.SourceType == InventorySourceType.Player);
                        characterName = playerCache?.Name ?? $"Character {characterId}";
                        worldName = playerCache?.World ?? string.Empty;
                    }

                    // Calculate player inventory quantity
                    var playerInventories = charGroup.Where(c => c.SourceType == InventorySourceType.Player);
                    var playerQuantity = playerInventories
                        .SelectMany(c => c.Items)
                        .Where(i => i.ItemId == itemId)
                        .Sum(i => i.Quantity);

                    // Calculate retainer quantities
                    var retainerData = charGroup
                        .Where(c => c.SourceType == InventorySourceType.Retainer)
                        .Select(r => new
                        {
                            RetainerId = r.RetainerId,
                            RetainerName = r.Name,
                            Quantity = r.Items.Where(i => i.ItemId == itemId).Sum(i => i.Quantity)
                        })
                        .Where(r => r.Quantity > 0)
                        .OrderBy(r => r.RetainerName)
                        .ToList();

                    // Skip this character if they have no items
                    var totalCharQuantity = playerQuantity + retainerData.Sum(r => r.Quantity);
                    if (totalCharQuantity == 0)
                        continue;

                    // Add character row (showing player inventory only)
                    if (playerQuantity > 0)
                    {
                        var playerValue = unitPrice * playerQuantity;
                        rows.Add(new ItemInventoryRow
                        {
                            CharacterId = characterId,
                            CharacterName = characterName,
                            WorldName = worldName,
                            IsRetainer = false,
                            Quantity = playerQuantity,
                            UnitPrice = unitPrice,
                            TotalValue = playerValue
                        });
                        totalQuantity += playerQuantity;
                        totalValue += playerValue;
                    }
                    else
                    {
                        // Add a character header row with 0 quantity if they only have retainer items
                        rows.Add(new ItemInventoryRow
                        {
                            CharacterId = characterId,
                            CharacterName = characterName,
                            WorldName = worldName,
                            IsRetainer = false,
                            Quantity = 0,
                            UnitPrice = 0,
                            TotalValue = 0
                        });
                    }

                    // Add retainer rows
                    foreach (var retainer in retainerData)
                    {
                        var retainerValue = unitPrice * retainer.Quantity;
                        rows.Add(new ItemInventoryRow
                        {
                            CharacterId = characterId,
                            CharacterName = characterName,
                            WorldName = worldName,
                            IsRetainer = true,
                            RetainerId = retainer.RetainerId,
                            RetainerName = retainer.RetainerName,
                            Quantity = retainer.Quantity,
                            UnitPrice = unitPrice,
                            TotalValue = retainerValue
                        });
                        totalQuantity += retainer.Quantity;
                        totalValue += retainerValue;
                    }
                }

                return (rows, unitPrice, totalQuantity, totalValue);
            }).ConfigureAwait(false);

            _inventoryRows = result.rows;
            _inventoryUnitPrice = result.unitPrice;
            _inventoryTotalQuantity = result.totalQuantity;
            _inventoryTotalValue = result.totalValue;
            _inventoryLoaded = true;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemDetailsPopup] Error loading inventory data async: {ex.Message}");
            _inventoryLoaded = true;
        }
        finally
        {
            _inventoryLoading = false;
        }
    }

    private async Task FetchMarketDataAsync()
    {
        if (_isLoading || _currentItemId == 0)
            return;

        // Determine the scope to use
        var scope = _universalisService.GetConfiguredScope();
        if (string.IsNullOrEmpty(scope))
        {
            // Use manually selected scope if no character is logged in
            scope = _selectedScope;
        }

        if (string.IsNullOrEmpty(scope))
        {
            _errorMessage = "Log in or set a world/DC override in Universalis settings to view live market data.";
            return;
        }

        _isLoading = true;
        _errorMessage = null;

        try
        {
            // Get both listings and history in one call (uses the combined endpoint)
            _marketData = await _universalisService.GetMarketBoardDataAsync(
                scope,
                _currentItemId,
                listings: _maxListings,
                entries: _maxSales);

            if (_marketData == null)
            {
                _errorMessage = "Failed to fetch market data from Universalis.";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error: {ex.Message}";
            LogService.Debug($"[ItemDetailsPopup] Fetch error: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }
}
