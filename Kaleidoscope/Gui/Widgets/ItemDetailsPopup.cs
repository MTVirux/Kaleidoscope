using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A popup window that displays current listings and recent sales for a market item.
/// </summary>
public class ItemDetailsPopup
{
    private readonly UniversalisService _universalisService;
    private readonly ItemDataService _itemDataService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly SamplerService? _samplerService;

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

    /// <summary>
    /// Creates a new ItemDetailsPopup.
    /// </summary>
    public ItemDetailsPopup(
        UniversalisService universalisService,
        ItemDataService itemDataService,
        PriceTrackingService priceTrackingService,
        SamplerService? samplerService = null)
    {
        _universalisService = universalisService;
        _itemDataService = itemDataService;
        _priceTrackingService = priceTrackingService;
        _samplerService = samplerService;
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
        _marketData = null;
        _errorMessage = null;
        _localSales.Clear();
        _localSalesLoaded = false;

        // Fetch market data
        _ = FetchMarketDataAsync();
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
            // Local Sales is the default tab when available
            if (_samplerService != null)
            {
                var localSalesFlags = ImGuiTabItemFlags.None;
                // Set as default on first open
                if (!_localSalesLoaded)
                {
                    localSalesFlags = ImGuiTabItemFlags.SetSelected;
                }
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
            ImGui.TextDisabled($"Scope: {displayScope} | Last Updated: {GetTimeAgo(_marketData.LastUploadDateTime)}");

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
                var timeAgo = GetTimeAgo(sale.SaleDateTime);
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
        if (_samplerService == null)
        {
            ImGui.TextDisabled("Database service not available.");
            return;
        }

        // Load local sales on first view
        if (!_localSalesLoaded)
        {
            LoadLocalSales();
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
                var timeAgo = GetTimeAgo(sale.Timestamp.ToLocalTime());
                
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
        if (_samplerService == null) return;

        try
        {
            // Use the method that also cleans up inventory value history
            if (_samplerService.DbService.DeleteSaleRecordWithHistoryCleanup(saleId, saleTimestamp))
            {
                // Reload the sales list
                _localSalesLoaded = false;
                LoadLocalSales();
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
        _localSalesLoaded = true;
        _localSales.Clear();

        if (_samplerService == null || _currentItemId == 0)
            return;

        try
        {
            var sales = _samplerService.DbService.GetSaleRecords((int)_currentItemId, limit: 100);
            _localSales = sales;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemDetailsPopup] Error loading local sales: {ex.Message}");
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
            _errorMessage = "No world/data center selected. Please select a scope.";
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

    private static string GetTimeAgo(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;

        if (span.TotalMinutes < 1)
            return "Just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)}w ago";

        return dateTime.ToString("MMM d");
    }
}
