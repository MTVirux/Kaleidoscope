using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Item management category in the config window.
/// Allows users to manage tracked items and set custom colors for game items.
/// </summary>
public sealed class ItemsCategory
{
    private readonly ConfigurationService _configService;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    private readonly ITextureProvider? _textureProvider;
    private readonly FavoritesService? _favoritesService;
    private readonly CurrencyTrackerService? _currencyTrackerService;
    
    // Color editing state
    private uint? _editingColorItemId = null;
    private Vector4 _colorEditBuffer = Vector4.One;
    
    // Search state
    private string _searchFilter = string.Empty;
    private string _trackedItemsSearchFilter = string.Empty;
    
    // Item picker for adding new items (for colors section)
    private readonly MTItemComboDropdown? _itemCombo;
    
    // Item picker for tracking items (for tracked items section)
    private readonly MTItemComboDropdown? _trackItemCombo;
    
    // Cached item names for display
    private readonly Dictionary<uint, string> _itemNameCache = new();

    public ItemsCategory(
        ConfigurationService configService,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null,
        CurrencyTrackerService? CurrencyTrackerService = null)
    {
        _configService = configService;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        _currencyTrackerService = CurrencyTrackerService;
        
        // Create item picker if we have the required services
        if (_dataManager != null && _textureProvider != null && _favoritesService != null)
        {
            _itemCombo = new MTItemComboDropdown(
                _textureProvider,
                _dataManager,
                _favoritesService,
                null, // No price tracking service - include all items
                "GameItemsAdd",
                marketableOnly: false,
                configService: _configService,
                trackedDataRegistry: _currencyTrackerService?.Registry,
                excludeCurrencies: true);
            
            _trackItemCombo = new MTItemComboDropdown(
                _textureProvider,
                _dataManager,
                _favoritesService,
                null, // No price tracking service - include all items
                "TrackItemAdd",
                marketableOnly: false,
                configService: _configService,
                trackedDataRegistry: _currencyTrackerService?.Registry,
                excludeCurrencies: true);
        }
    }

    public void Draw()
    {
        // Draw tracked items section first
        DrawTrackedItemsSection();
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Then draw colors section
        DrawColorsSection();
    }
    
    private void DrawTrackedItemsSection()
    {
        ImGui.TextUnformatted("Tracked Items - Historical Data");
        ImGui.Separator();
        ImGui.TextWrapped("Add items to track their quantity over time. " +
            "Enable historical tracking per-item to record time-series data for graphing.");
        ImGui.Spacing();
        
        var config = _configService.Config;
        
        // Show summary of items with tracking enabled
        var itemsWithTracking = config.ItemsWithHistoricalTracking.Count;
        if (itemsWithTracking > 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), 
                $"{itemsWithTracking} item(s) have historical tracking enabled.");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), 
                "No items have historical tracking enabled. Enable tracking per-item below.");
        }
        
        ImGui.Spacing();
        
        // Add item picker
        DrawAddTrackedItemSection(config);
        
        ImGui.Spacing();
        
        // Collect all tracked items from ItemTable and ItemGraph
        var trackedItems = new Dictionary<uint, TrackedItemInfo>();
        
        // From ItemTable
        if (config.ItemTable?.Columns != null)
        {
            foreach (var col in config.ItemTable.Columns.Where(c => !c.IsCurrency))
            {
                if (!trackedItems.ContainsKey(col.Id))
                {
                    trackedItems[col.Id] = new TrackedItemInfo { ItemId = col.Id };
                }
                trackedItems[col.Id].InItemTable = true;
                trackedItems[col.Id].ItemTableConfig = col;
            }
        }
        
        // From ItemGraph
        if (config.ItemGraph?.Series != null)
        {
            foreach (var series in config.ItemGraph.Series.Where(s => !s.IsCurrency))
            {
                if (!trackedItems.ContainsKey(series.Id))
                {
                    trackedItems[series.Id] = new TrackedItemInfo { ItemId = series.Id };
                }
                trackedItems[series.Id].InItemGraph = true;
                trackedItems[series.Id].ItemGraphConfig = series;
            }
        }
        
        if (trackedItems.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No items are being tracked.");
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Use the item picker above to add items to track.");
            return;
        }
        
        // Search bar
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##trackedSearch", "Search tracked items...", ref _trackedItemsSearchFilter, 100);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear##tracked"))
        {
            _trackedItemsSearchFilter = string.Empty;
        }
        ImGui.Spacing();
        
        // Filter items
        var filteredItems = string.IsNullOrWhiteSpace(_trackedItemsSearchFilter)
            ? trackedItems.Values.ToList()
            : trackedItems.Values.Where(info =>
            {
                var name = GetItemName(info.ItemId);
                return name.Contains(_trackedItemsSearchFilter, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        
        if (filteredItems.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No tracked items match your search.");
            return;
        }
        
        // Draw table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        
        var availableHeight = Math.Min(ImGui.GetContentRegionAvail().Y * 0.4f, 200);
        if (availableHeight < 80) availableHeight = 80;

        uint? itemToDelete = null;
        uint? itemToDeleteHistory = null;
        
        // Account for scrollbar width in fixed columns
        var scrollbarWidth = ImGui.GetStyle().ScrollbarSize;
        
        if (ImGui.BeginTable("TrackedItemsTable", 6, tableFlags, new Vector2(0, availableHeight)))
        {
            ImGui.TableSetupColumn("##Icon", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, ImGuiHelpers.IconSize + 4);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("In use by", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Store History", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 100 + scrollbarWidth);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var info in filteredItems.OrderBy(i => GetItemName(i.ItemId)))
            {
                ImGui.TableNextRow();
                ImGui.PushID((int)info.ItemId);

                // Icon column
                ImGui.TableNextColumn();
                DrawItemIcon(info.ItemId);

                // Item name column
                ImGui.TableNextColumn();
                var itemName = GetItemName(info.ItemId);
                ImGui.TextUnformatted(itemName);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Item ID: {info.ItemId}");
                }

                // Source column
                ImGui.TableNextColumn();
                var sources = new List<string>();
                if (info.InItemTable) sources.Add("Table");
                if (info.InItemGraph) sources.Add("Graph");
                ImGui.TextDisabled(string.Join(", ", sources));

                // Store History checkbox column (per-item toggle)
                ImGui.TableNextColumn();
                var storeHistory = config.ItemsWithHistoricalTracking.Contains(info.ItemId);
                if (ImGui.Checkbox("##storeHistory", ref storeHistory))
                {
                    if (storeHistory)
                    {
                        config.ItemsWithHistoricalTracking.Add(info.ItemId);
                    }
                    else
                    {
                        config.ItemsWithHistoricalTracking.Remove(info.ItemId);
                    }
                    _configService.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Enable/disable historical tracking for this item.\n" +
                        "This setting applies across all tools in the project.");
                }

                // Status column
                ImGui.TableNextColumn();
                if (storeHistory)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1f), "Recording");
                }
                else
                {
                    ImGui.TextDisabled("Off");
                }
                
                // Actions column
                ImGui.TableNextColumn();
                
                // Delete history button
                if (ImGui.SmallButton("Clear##clr"))
                {
                    itemToDeleteHistory = info.ItemId;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Delete all historical data for this item.\nThis cannot be undone.");
                }
                
                ImGui.SameLine();
                
                // Remove item button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.15f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.2f, 0.2f, 1f));
                if (ImGui.SmallButton("×##del"))
                {
                    itemToDelete = info.ItemId;
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Remove item from tracking");
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
        
        // Process deletion after iteration
        if (itemToDeleteHistory.HasValue)
        {
            DeleteItemHistory(itemToDeleteHistory.Value);
        }
        
        if (itemToDelete.HasValue)
        {
            RemoveTrackedItem(itemToDelete.Value);
        }
        
        // Summary
        var recordingCount = trackedItems.Values.Count(i => config.ItemsWithHistoricalTracking.Contains(i.ItemId));
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), 
            $"{trackedItems.Count} tracked items, {recordingCount} recording history");
    }
    
    private void DrawAddTrackedItemSection(Configuration config)
    {
        ImGui.TextUnformatted("Add Item to Track");
        
        if (_trackItemCombo != null)
        {
            if (_trackItemCombo.Draw(300))
            {
                if (_trackItemCombo.SelectedItemId > 0)
                {
                    var itemId = _trackItemCombo.SelectedItemId;
                    AddTrackedItem(config, itemId);
                    _trackItemCombo.ClearSelection();
                }
            }
        }
        else
        {
            ImGui.TextDisabled("Item picker not available.");
        }
    }
    
    private void AddTrackedItem(Configuration config, uint itemId)
    {
        // Ensure ItemGraph config exists
        config.ItemGraph ??= new ItemGraphSettings();
        config.ItemGraph.Series ??= new List<ItemColumnConfig>();
        
        // Check if already tracked in ItemGraph
        var existsInGraph = config.ItemGraph.Series.Any(s => s.Id == itemId && !s.IsCurrency);
        
        if (!existsInGraph)
        {
            // Add to ItemGraph with StoreHistory enabled by default
            config.ItemGraph.Series.Add(new ItemColumnConfig
            {
                Id = itemId,
                IsCurrency = false,
                StoreHistory = true
            });
            _configService.MarkDirty();
            LogService.Debug($"[ItemsCategory] Added item {itemId} to tracking with StoreHistory=true");
        }
        else
        {
            LogService.Debug($"[ItemsCategory] Item {itemId} already being tracked");
        }
    }
    
    private void RemoveTrackedItem(uint itemId)
    {
        var config = _configService.Config;
        var changed = false;
        
        // Remove from ItemTable
        if (config.ItemTable?.Columns != null)
        {
            var removed = config.ItemTable.Columns.RemoveAll(c => c.Id == itemId && !c.IsCurrency);
            if (removed > 0) changed = true;
        }
        
        // Remove from ItemGraph
        if (config.ItemGraph?.Series != null)
        {
            var removed = config.ItemGraph.Series.RemoveAll(s => s.Id == itemId && !s.IsCurrency);
            if (removed > 0) changed = true;
        }
        
        if (changed)
        {
            _configService.MarkDirty();
            _itemNameCache.Remove(itemId);
            LogService.Debug($"[ItemsCategory] Removed item {itemId} from tracking");
        }
    }
    
    private void DeleteItemHistory(uint itemId)
    {
        if (_currencyTrackerService == null)
        {
            LogService.Debug("[ItemsCategory] Cannot delete item history: CurrencyTrackerService not available");
            return;
        }
        
        var dbService = _currencyTrackerService.DbService;
        
        // Delete player inventory history
        var playerVariable = InventoryCacheService.GetItemVariableName(itemId);
        var playerDeleted = dbService.ClearAllData(playerVariable);
        
        // Delete retainer inventory history
        var retainerVariable = InventoryCacheService.GetRetainerItemVariableName(itemId);
        var retainerDeleted = dbService.ClearAllData(retainerVariable);
        
        // Invalidate the time-series cache for these variables
        _currencyTrackerService.CacheService.InvalidateVariable(playerVariable);
        _currencyTrackerService.CacheService.InvalidateVariable(retainerVariable);
        
        var itemName = GetItemName(itemId);
        if (playerDeleted || retainerDeleted)
        {
            LogService.Info($"[ItemsCategory] Deleted historical data for item '{itemName}' (ID: {itemId})");
        }
        else
        {
            LogService.Debug($"[ItemsCategory] No historical data found for item '{itemName}' (ID: {itemId})");
        }
    }
    
    private class TrackedItemInfo
    {
        public uint ItemId { get; set; }
        public bool InItemTable { get; set; }
        public bool InItemGraph { get; set; }
        public ItemColumnConfig? ItemTableConfig { get; set; }
        public ItemColumnConfig? ItemGraphConfig { get; set; }
    }

    private void DrawColorsSection()
    {
        ImGui.TextUnformatted("Game Item Colors");
        ImGui.Separator();
        ImGui.TextWrapped("Set custom colors for game items tracked in the Item Table tool. " +
            "These colors are applied to item columns in the table.");
        ImGui.Spacing();
        
        var config = _configService.Config;
        var gameItemColors = config.GameItemColors;
        
        // Add new item section
        DrawAddItemSection(gameItemColors);
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Search bar
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##search", "Search items...", ref _searchFilter, 100);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _searchFilter = string.Empty;
        }
        ImGui.Spacing();

        if (gameItemColors.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No game items with custom colors yet.");
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Use the item picker above to add items.");
            return;
        }
        
        // Filter items by search
        var filteredItems = string.IsNullOrWhiteSpace(_searchFilter)
            ? gameItemColors.Keys.ToList()
            : gameItemColors.Keys.Where(id => 
            {
                var name = GetItemName(id);
                return name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        
        if (filteredItems.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No items match your search.");
            return;
        }

        // Draw table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable;
        
        // Calculate available height for table
        var availableHeight = ImGui.GetContentRegionAvail().Y - 30;
        if (availableHeight < 100) availableHeight = 100;

        // Account for scrollbar width in fixed columns
        var scrollbarWidth = ImGui.GetStyle().ScrollbarSize;
        
        if (ImGui.BeginTable("GameItemColorsTable", 5, tableFlags, new Vector2(0, availableHeight)))
        {
            // Setup columns
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 60);
            ImGui.TableSetupColumn("##Icon", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.NoSort, ImGuiHelpers.IconSize + 4);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 80);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 55 + scrollbarWidth);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Get sort specs and apply sorting
            var sortSpecs = ImGui.TableGetSortSpecs();
            IEnumerable<uint> sortedItems = filteredItems;
            
            if (sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
                
                sortedItems = spec.ColumnIndex switch
                {
                    0 => ascending ? filteredItems.OrderBy(id => id) : filteredItems.OrderByDescending(id => id),
                    2 => ascending ? filteredItems.OrderBy(id => GetItemName(id)) : filteredItems.OrderByDescending(id => GetItemName(id)),
                    _ => filteredItems.OrderBy(id => GetItemName(id))
                };
            }
            else
            {
                // Default sort by item name
                sortedItems = filteredItems.OrderBy(id => GetItemName(id));
            }

            foreach (var itemId in sortedItems)
            {
                ImGui.TableNextRow();

                // ID column (centered)
                ImGui.TableNextColumn();
                var idText = $"{itemId}";
                var idTextWidth = ImGui.CalcTextSize(idText).X;
                var columnWidth = ImGui.GetContentRegionAvail().X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (columnWidth - idTextWidth) * 0.5f);
                ImGui.TextDisabled(idText);

                // Icon column
                ImGui.TableNextColumn();
                DrawItemIcon(itemId);

                // Item name column
                ImGui.TableNextColumn();
                var itemName = GetItemName(itemId);
                ImGui.TextUnformatted(itemName);

                // Color column
                ImGui.TableNextColumn();
                DrawColorCell(itemId, config);

                // Actions column
                ImGui.TableNextColumn();
                DrawActionsCell(itemId, config);
            }

            ImGui.EndTable();
        }

        // Summary
        ImGui.Spacing();
        var totalCount = gameItemColors.Count;
        var filteredCount = filteredItems.Count;
        var summaryText = string.IsNullOrWhiteSpace(_searchFilter)
            ? $"{totalCount} items with custom colors"
            : $"Showing {filteredCount} of {totalCount} items";
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), summaryText);
    }

    private void DrawAddItemSection(Dictionary<uint, uint> gameItemColors)
    {
        ImGui.TextUnformatted("Add Item");
        
        if (_itemCombo != null)
        {
            if (_itemCombo.Draw(300))
            {
                // Item selected - add it with a default white color
                if (_itemCombo.SelectedItemId > 0)
                {
                    var itemId = _itemCombo.SelectedItemId;
                    if (!gameItemColors.ContainsKey(itemId))
                    {
                        // Add with white color (0xFFFFFFFF in ABGR)
                        gameItemColors[itemId] = 0xFFFFFFFF;
                        _configService.MarkDirty();
                        LogService.Debug($"[ItemsCategory] Added item {itemId} with default color");
                    }
                    _itemCombo.ClearSelection();
                }
            }
        }
        else
        {
            ImGui.TextDisabled("Item picker not available.");
        }
    }

    private void DrawColorCell(uint itemId, Configuration config)
    {
        ImGui.PushID((int)itemId);
        
        var hasColor = config.GameItemColors.TryGetValue(itemId, out var colorUint);
        Vector4 colorValue;
        
        if (_editingColorItemId == itemId)
        {
            colorValue = _colorEditBuffer;
        }
        else if (hasColor)
        {
            colorValue = ColorUtils.UintToVector4(colorUint);
        }
        else
        {
            colorValue = new Vector4(1f, 1f, 1f, 1f);
        }
        
        // Color picker
        if (ImGui.ColorEdit4("##color", ref colorValue,
            ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
        {
            _colorEditBuffer = colorValue;
        }
        
        // Track when we start editing
        if (ImGui.IsItemActivated())
        {
            _editingColorItemId = itemId;
            _colorEditBuffer = colorValue;
        }
        
        // Save when the user finishes editing
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            SaveGameItemColor(itemId, ColorUtils.Vector4ToUint(_colorEditBuffer));
            _editingColorItemId = null;
        }
        
        ImGui.PopID();
    }

    private void DrawActionsCell(uint itemId, Configuration config)
    {
        ImGui.PushID((int)itemId);
        if (ImGui.SmallButton("X"))
        {
            SaveGameItemColor(itemId, null);
            _editingColorItemId = null;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Remove item");
            ImGui.EndTooltip();
        }
        ImGui.PopID();
    }

    private string GetItemName(uint itemId)
    {
        if (_itemNameCache.TryGetValue(itemId, out var cached))
            return cached;
        
        var name = _itemDataService?.GetItemName(itemId) ?? $"Item #{itemId}";
        _itemNameCache[itemId] = name;
        return name;
    }

    private void SaveGameItemColor(uint itemId, uint? color)
    {
        try
        {
            var config = _configService.Config;
            
            if (color.HasValue)
            {
                config.GameItemColors[itemId] = color.Value;
            }
            else
            {
                config.GameItemColors.Remove(itemId);
                _itemNameCache.Remove(itemId);
            }
            
            _configService.MarkDirty();
            LogService.Debug($"[ItemsCategory] Saved color for item {itemId}: {color?.ToString("X8") ?? "(removed)"}");
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to save game item color for {itemId}", ex);
        }
    }

    private void DrawItemIcon(uint itemId)
    {
        if (_textureProvider == null || _itemDataService == null)
        {
            ImGui.Dummy(new Vector2(ImGuiHelpers.IconSize));
            return;
        }

        try
        {
            var iconId = _itemDataService.GetItemIconId(itemId);
            if (iconId > 0)
            {
                var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                if (icon.TryGetWrap(out var wrap, out _))
                {
                    ImGui.Image(wrap.Handle, new Vector2(ImGuiHelpers.IconSize));
                    return;
                }
            }
        }
        catch
        {
            // Ignore errors - use placeholder
        }

        // Placeholder if icon not loaded
        ImGui.Dummy(new Vector2(ImGuiHelpers.IconSize));
    }
}
