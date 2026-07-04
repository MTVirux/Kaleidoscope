using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Inventory;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Item management category in the config window.
/// Allows users to manage tracked items and set custom colors for game items.
/// </summary>
public sealed class ItemsCategory : IConfigCategory
{
    /// <inheritdoc/>
    public string Label => "Items";

    /// <inheritdoc/>
    public bool IsDeveloper => false;

    private readonly ConfigurationService _configService;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    private readonly ITextureProvider? _textureProvider;
    private readonly FavoritesService? _favoritesService;
    private readonly CurrencyTrackerService? _currencyTrackerService;
    
    private uint? _editingColorItemId = null;
    private Vector4 _colorEditBuffer = Vector4.One;
    
    private string _searchFilter = string.Empty;
    private string _trackedItemsSearchFilter = string.Empty;
    
    private readonly ItemComboDropdown? _itemCombo;
    
    private readonly ItemComboDropdown? _trackItemCombo;
    
    private readonly Dictionary<uint, string> _itemNameCache = new();
    
    private bool _trackComboInitialized;

    public ItemsCategory(
        ConfigurationService configService,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null,
        CurrencyTrackerService? currencyTrackerService = null)
    {
        _configService = configService;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        _currencyTrackerService = currencyTrackerService;
        
        // Create item picker if we have the required services
        if (_dataManager != null && _textureProvider != null && _favoritesService != null)
        {
            _itemCombo = new ItemComboDropdown(
                _textureProvider,
                _dataManager,
                _favoritesService,
                null, // No price tracking service - include all items
                "GameItemsAdd",
                marketableOnly: false,
                configService: _configService,
                trackedDataRegistry: _currencyTrackerService?.Registry,
                excludeCurrencies: true);
            
            _trackItemCombo = new ItemComboDropdown(
                _textureProvider,
                _dataManager,
                _favoritesService,
                null, // No price tracking service - include all items
                "TrackItemAdd",
                marketableOnly: false,
                configService: _configService,
                trackedDataRegistry: _currencyTrackerService?.Registry,
                excludeCurrencies: true,
                multiSelect: true,
                emptySelectionText: "Select items to track...",
                showNoneBulkAction: true);
            
            _trackItemCombo.MultiSelectionChanged += OnTrackItemMultiSelectionChanged;
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
            ImGui.TextColored(UiColors.Info, 
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
            ImGui.TextColored(UiColors.Info, "No items are being tracked.");
            ImGui.TextColored(UiColors.Muted, "Use the item picker above to add items to track.");
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
            ImGui.TextColored(UiColors.Info, "No tracked items match your search.");
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

                ImGui.TableNextColumn();
                ImGuiHelpers.DrawGameIcon(_textureProvider, _itemDataService, info.ItemId, new Vector2(ImGuiHelpers.IconSize), allowRawIconFallback: false);

                ImGui.TableNextColumn();
                var itemName = GetItemName(info.ItemId);
                ImGui.TextUnformatted(itemName);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Item ID: {info.ItemId}");
                }

                ImGui.TableNextColumn();
                var sources = new List<string>();
                if (info.InItemTable) sources.Add("Table");
                if (info.InItemGraph) sources.Add("Graph");
                ImGui.TextDisabled(string.Join(", ", sources));

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

                ImGui.TableNextColumn();
                if (storeHistory)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1f), "Recording");
                }
                else
                {
                    ImGui.TextDisabled("Off");
                }
                
                ImGui.TableNextColumn();
                
                if (ImGui.SmallButton("Clear##clr"))
                {
                    itemToDeleteHistory = info.ItemId;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Delete all historical data for this item.\nThis cannot be undone.");
                }
                
                ImGui.SameLine();
                
                if (ImGuiHelpers.DangerSmallButton("×##del"))
                {
                    itemToDelete = info.ItemId;
                }
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
        ImGui.TextColored(UiColors.Info, 
            $"{trackedItems.Count} tracked items, {recordingCount} recording history");
    }
    
    private void DrawAddTrackedItemSection(Configuration config)
    {
        ImGui.TextUnformatted("Items to Track");
        
        if (_trackItemCombo != null)
        {
            // Sync combo selection with currently tracked items on first draw
            if (!_trackComboInitialized)
            {
                SyncTrackComboSelection(config);
                _trackComboInitialized = true;
            }
            
            _trackItemCombo.DrawMultiSelect(400);
        }
        else
        {
            ImGui.TextDisabled("Item picker not available.");
        }
    }
    
    /// <summary>
    /// Syncs the multi-select combo state with the currently tracked items from config.
    /// </summary>
    private void SyncTrackComboSelection(Configuration config)
    {
        var trackedIds = new HashSet<uint>();
        
        if (config.ItemTable?.Columns != null)
        {
            foreach (var col in config.ItemTable.Columns.Where(c => !c.IsCurrency))
                trackedIds.Add(col.Id);
        }
        
        if (config.ItemGraph?.Series != null)
        {
            foreach (var series in config.ItemGraph.Series.Where(s => !s.IsCurrency))
                trackedIds.Add(series.Id);
        }
        
        _trackItemCombo!.SetMultiSelection(trackedIds);
    }
    
    /// <summary>
    /// Handles multi-selection changes — adds/removes tracked items to match the selection.
    /// </summary>
    private void OnTrackItemMultiSelectionChanged(IReadOnlySet<uint> selectedIds)
    {
        var config = _configService.Config;
        
        // Build set of currently tracked item IDs
        var currentlyTracked = new HashSet<uint>();
        if (config.ItemTable?.Columns != null)
        {
            foreach (var col in config.ItemTable.Columns.Where(c => !c.IsCurrency))
                currentlyTracked.Add(col.Id);
        }
        if (config.ItemGraph?.Series != null)
        {
            foreach (var series in config.ItemGraph.Series.Where(s => !s.IsCurrency))
                currentlyTracked.Add(series.Id);
        }
        
        // Items to add (in selection but not tracked)
        foreach (var itemId in selectedIds)
        {
            if (!currentlyTracked.Contains(itemId))
                AddTrackedItem(config, itemId);
        }
        
        // Items to remove (tracked but no longer in selection)
        foreach (var itemId in currentlyTracked)
        {
            if (!selectedIds.Contains(itemId))
                RemoveTrackedItem(itemId);
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
            LogService.Debug(LogCategory.UI, $"[ItemsCategory] Added item {itemId} to tracking with StoreHistory=true");
        }
        else
        {
            LogService.Debug(LogCategory.UI, $"[ItemsCategory] Item {itemId} already being tracked");
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
            LogService.Debug(LogCategory.UI, $"[ItemsCategory] Removed item {itemId} from tracking");
            
            // Re-sync the multi-select combo so it reflects the removal
            SyncTrackComboSelection(config);
        }
    }
    
    private void DeleteItemHistory(uint itemId)
    {
        if (_currencyTrackerService == null)
        {
            LogService.Debug(LogCategory.UI, "[ItemsCategory] Cannot delete item history: CurrencyTrackerService not available");
            return;
        }
        
        var dbService = _currencyTrackerService.DbService;
        
        // Delete player inventory history
        var playerVariable = $"Item_{itemId}";
        var playerDeleted = dbService.ClearAllData(playerVariable);

        // Delete retainer inventory history
        var retainerVariable = $"ItemRetainer_{itemId}";
        var retainerDeleted = dbService.ClearAllData(retainerVariable);
        
        // Invalidate the time-series cache for these variables
        _currencyTrackerService.CacheService.InvalidateVariable(playerVariable);
        _currencyTrackerService.CacheService.InvalidateVariable(retainerVariable);
        
        var itemName = GetItemName(itemId);
        if (playerDeleted || retainerDeleted)
        {
            LogService.Info(LogCategory.UI, $"[ItemsCategory] Deleted historical data for item '{itemName}' (ID: {itemId})");
        }
        else
        {
            LogService.Debug(LogCategory.UI, $"[ItemsCategory] No historical data found for item '{itemName}' (ID: {itemId})");
        }
    }
    
    private sealed class TrackedItemInfo
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
            ImGui.TextColored(UiColors.Info, "No game items with custom colors yet.");
            ImGui.TextColored(UiColors.Muted, "Use the item picker above to add items.");
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
            ImGui.TextColored(UiColors.Info, "No items match your search.");
            return;
        }

        // Account for scrollbar width in fixed columns
        var scrollbarWidth = ImGui.GetStyle().ScrollbarSize;

        ConfigUiHelpers.DrawColorTable("GameItemColorsTable", 5,
            () =>
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 60);
                ImGui.TableSetupColumn("##Icon", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.NoSort, ImGuiHelpers.IconSize + 4);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 80);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 55 + scrollbarWidth);
            },
            () =>
            {
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
                ImGuiHelpers.DrawGameIcon(_textureProvider, _itemDataService, itemId, new Vector2(ImGuiHelpers.IconSize), allowRawIconFallback: false);

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
            },
            extraFlags: ImGuiTableFlags.Sortable);

        // Summary
        ImGui.Spacing();
        var totalCount = gameItemColors.Count;
        var filteredCount = filteredItems.Count;
        var summaryText = string.IsNullOrWhiteSpace(_searchFilter)
            ? $"{totalCount} items with custom colors"
            : $"Showing {filteredCount} of {totalCount} items";
        ImGui.TextColored(UiColors.Info, summaryText);
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
                        LogService.Debug(LogCategory.UI, $"[ItemsCategory] Added item {itemId} with default color");
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
        
        config.GameItemColors.TryGetValue(itemId, out var colorUint);
        
        ImGuiHelpers.InlineColorEditorAlwaysVisible(
            itemId,
            ref _editingColorItemId,
            ref _colorEditBuffer,
            colorUint,
            newColor => SaveGameItemColor(itemId, newColor));
        
        ImGui.PopID();
    }

    private void DrawActionsCell(uint itemId, Configuration config)
    {
        ImGui.PushID((int)itemId);
        ImGuiHelpers.InlineColorClearButton(
            config.GameItemColors.ContainsKey(itemId),
            ref _editingColorItemId,
            () => SaveGameItemColor(itemId, null),
            "Remove item");
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
            LogService.Debug(LogCategory.UI, $"[ItemsCategory] Saved color for item {itemId}: {color?.ToString("X8") ?? "(removed)"}");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, $"Failed to save game item color for {itemId}", ex);
        }
    }

}
