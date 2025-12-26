using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using CrystalElement = Kaleidoscope.CrystalElement;
using CrystalTier = Kaleidoscope.CrystalTier;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.DataTable;

/// <summary>
/// Tool component that displays a customizable table of items/currencies across characters.
/// Users can add items and currencies to track, customize column names and colors.
/// </summary>
public class DataTableTool : ToolComponent
{
    public override string ToolName => "Data Table";
    
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly FavoritesService? _favoritesService;
    private readonly ITextureProvider? _textureProvider;
    
    private readonly ItemTableWidget _tableWidget;
    private readonly ItemComboDropdown? _itemCombo;
    private readonly CurrencyComboDropdown? _currencyCombo;
    private readonly CharacterCombo? _characterCombo;
    
    // Instance-specific settings (not shared with other tool instances)
    private readonly ItemTableSettings _instanceSettings;
    
    // Cached data
    private PreparedItemTableData? _cachedData;
    private DateTime _lastRefresh = DateTime.MinValue;
    private volatile bool _pendingRefresh = true;
    private CharacterNameFormat _cachedNameFormat;
    private CharacterSortOrder _cachedSortOrder;
    
    /// <summary>
    /// The name of the preset used to create this tool, if any.
    /// When set, the title will display as "Data Table - PresetName".
    /// </summary>
    public string? PresetName { get; set; }
    
    private ItemTableSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _samplerService.DbService;
    private TimeSeriesCacheService CacheService => _samplerService.CacheService;
    
    public DataTableTool(
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null,
        AutoRetainerIpcService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
    {
        _samplerService = samplerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _trackedDataRegistry = trackedDataRegistry;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        _favoritesService = favoritesService;
        _textureProvider = textureProvider;
        
        // Initialize instance-specific settings with defaults
        _instanceSettings = new ItemTableSettings();
        
        Size = new Vector2(500, 300);
        UpdateTitle();
        
        // Create the table widget
        _tableWidget = new ItemTableWidget(
            new ItemTableWidget.TableConfig
            {
                TableId = "CustomItemTable",
                NoDataText = "No data yet. Add items or currencies to track."
            },
            itemDataService,
            trackedDataRegistry,
            configService.Config,
            samplerService.CacheService);
        
        // Bind to instance-specific settings (not global config)
        _tableWidget.BindSettings(
            _instanceSettings,
            () => NotifyToolSettingsChanged(),
            "Table Settings");
        
        // Create item combo if we have the required services
        if (_dataManager != null && _itemDataService != null && textureProvider != null && favoritesService != null)
        {
            _itemCombo = new ItemComboDropdown(
                textureProvider,
                _dataManager,
                favoritesService,
                null, // No price tracking service - include all items
                "ItemTableAdd",
                marketableOnly: false);
        }
        
        // Create currency combo if we have the required services
        if (textureProvider != null && trackedDataRegistry != null && favoritesService != null)
        {
            _currencyCombo = new CurrencyComboDropdown(
                textureProvider,
                trackedDataRegistry,
                favoritesService,
                "ItemTableCurrencyAdd",
                itemDataService);
        }
        
        // Create character combo for filtering
        if (favoritesService != null)
        {
            _characterCombo = new CharacterCombo(
                samplerService, 
                favoritesService, 
                configService, 
                "ItemTableCharFilter",
                autoRetainerService,
                priceTrackingService);
            _characterCombo.MultiSelectEnabled = true;
            _characterCombo.MultiSelectionChanged += OnCharacterSelectionChanged;
            
            // Restore selection from settings
            if (_instanceSettings.UseCharacterFilter && _instanceSettings.SelectedCharacterIds.Count > 0)
            {
                _characterCombo.SetSelection(_instanceSettings.SelectedCharacterIds);
            }
        }
        
        // Register widget as settings provider
        RegisterSettingsProvider(_tableWidget);
    }
    
    /// <summary>
    /// Sets the columns for this table. Used by presets to pre-configure the table.
    /// </summary>
    public void SetColumns(List<Gui.Widgets.ItemColumnConfig> columns)
    {
        _instanceSettings.Columns.Clear();
        _instanceSettings.Columns.AddRange(columns);
        _pendingRefresh = true;
    }
    
    /// <summary>
    /// Configures table settings. Used by presets to pre-configure display options.
    /// </summary>
    public void ConfigureSettings(Action<ItemTableSettings> configure)
    {
        configure(_instanceSettings);
        _pendingRefresh = true;
    }
    
    /// <summary>
    /// Updates the Title based on whether a PresetName is set.
    /// </summary>
    private void UpdateTitle()
    {
        Title = string.IsNullOrWhiteSpace(PresetName) ? "Data Table" : $"Data Table - {PresetName}";
    }
    
    /// <summary>
    /// Sets the preset name and updates the title accordingly.
    /// </summary>
    public void SetPresetName(string presetName)
    {
        PresetName = presetName;
        UpdateTitle();
    }
    
    /// <summary>
    /// Gets the current columns/items being tracked.
    /// </summary>
    public IReadOnlyList<Gui.Widgets.ItemColumnConfig> GetColumns() => _instanceSettings.Columns;
    
    /// <summary>
    /// Gets the list of items that don't have historical data enabled.
    /// These items won't have time-series data when converted to a graph.
    /// Currencies are excluded since they always have historical data.
    /// </summary>
    public IReadOnlyList<Gui.Widgets.ItemColumnConfig> GetItemsWithoutHistory()
    {
        return _instanceSettings.Columns
            .Where(c => !c.IsCurrency && !c.StoreHistory)
            .ToList();
    }
    
    /// <summary>
    /// Gets the list of items that don't have historical data enabled, with resolved names.
    /// Returns tuples of (ItemId, ItemName) for display purposes.
    /// </summary>
    public IReadOnlyList<(uint Id, string Name)> GetItemsWithoutHistoryWithNames()
    {
        return _instanceSettings.Columns
            .Where(c => !c.IsCurrency && !c.StoreHistory)
            .Select(c => (c.Id, c.CustomName ?? _itemDataService?.GetItemName(c.Id) ?? $"Item #{c.Id}"))
            .ToList();
    }
    
    private void OnCharacterSelectionChanged(IReadOnlySet<ulong> selectedIds)
    {
        var settings = Settings;
        settings.SelectedCharacterIds.Clear();
        settings.SelectedCharacterIds.AddRange(selectedIds);
        settings.UseCharacterFilter = selectedIds.Count > 0;
        _pendingRefresh = true;
        NotifyToolSettingsChanged();
    }
    
    public override void RenderToolContent()
    {
        try
        {
            var settings = Settings;
            
            // Check if name format changed - force refresh
            var currentFormat = _configService.Config.CharacterNameFormat;
            if (_cachedNameFormat != currentFormat)
            {
                _cachedNameFormat = currentFormat;
                _pendingRefresh = true;
            }
            
            // Check if sort order changed - force refresh
            var currentSortOrder = _configService.Config.CharacterSortOrder;
            if (_cachedSortOrder != currentSortOrder)
            {
                _cachedSortOrder = currentSortOrder;
                _pendingRefresh = true;
            }
            
            // Auto-refresh every 0.5s
            var shouldAutoRefresh = (DateTime.UtcNow - _lastRefresh).TotalSeconds > 0.5;
            
            if (_pendingRefresh || shouldAutoRefresh)
            {
                RefreshData();
            }
            
            // Draw action buttons (if enabled)
            if (settings.ShowActionButtons)
            {
                DrawActionButtons();
                ImGui.Separator();
            }
            
            // Draw the table
            _tableWidget.Draw(_cachedData, settings);
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[DataTableTool] Draw error: {ex.Message}");
        }
    }
    
    private void DrawActionButtons()
    {
        // Character filter combo
        if (_characterCombo != null)
        {
            ImGui.SetNextItemWidth(180);
            _characterCombo.Draw(180);
            ImGui.SameLine();
        }
        
        // Item dropdown (multi-select) - sync selection with current columns
        if (_itemCombo != null)
        {
            // Sync combo selection with current item columns
            var currentItemIds = Settings.Columns
                .Where(c => !c.IsCurrency)
                .Select(c => c.Id)
                .ToHashSet();
            
            // Only update if different to avoid clearing user's pending selections
            var comboSelection = _itemCombo.GetMultiSelection();
            if (!currentItemIds.SetEquals(comboSelection))
            {
                _itemCombo.SetMultiSelection(currentItemIds);
            }
            
            _itemCombo.DrawMultiSelect(180);
            
            // Sync columns based on current selection (add/remove as needed)
            var newSelection = _itemCombo.GetMultiSelection();
            SyncItemColumns(newSelection);
            
            ImGui.SameLine();
        }
        
        // Currency dropdown (multi-select) - sync selection with current columns
        if (_currencyCombo != null)
        {
            // Sync combo selection with current currency columns
            var currentCurrencyTypes = Settings.Columns
                .Where(c => c.IsCurrency)
                .Select(c => (TrackedDataType)c.Id)
                .ToHashSet();
            
            // Only update if different
            var comboSelection = _currencyCombo.GetMultiSelection();
            if (!currentCurrencyTypes.SetEquals(comboSelection))
            {
                _currencyCombo.SetMultiSelection(currentCurrencyTypes);
            }
            
            _currencyCombo.DrawMultiSelect(180);
            
            // Sync columns based on current selection (add/remove as needed)
            var newSelection = _currencyCombo.GetMultiSelection();
            SyncCurrencyColumns(newSelection);
        }
    }
    
    private void SyncItemColumns(IReadOnlySet<uint> selectedItemIds)
    {
        var changed = false;
        
        // Add new items
        foreach (var itemId in selectedItemIds)
        {
            if (!Settings.Columns.Any(c => !c.IsCurrency && c.Id == itemId))
            {
                Settings.Columns.Add(new ItemColumnConfig { Id = itemId, IsCurrency = false });
                changed = true;
            }
        }
        
        // Remove deselected items
        var toRemove = Settings.Columns
            .Where(c => !c.IsCurrency && !selectedItemIds.Contains(c.Id))
            .ToList();
        
        foreach (var col in toRemove)
        {
            Settings.Columns.Remove(col);
            changed = true;
        }
        
        if (changed)
        {
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
    }
    
    private void SyncCurrencyColumns(IReadOnlySet<TrackedDataType> selectedTypes)
    {
        var changed = false;
        
        // Add new currencies
        foreach (var type in selectedTypes)
        {
            if (!Settings.Columns.Any(c => c.IsCurrency && c.Id == (uint)type))
            {
                Settings.Columns.Add(new ItemColumnConfig { Id = (uint)type, IsCurrency = true });
                changed = true;
            }
        }
        
        // Remove deselected currencies
        var selectedIds = selectedTypes.Select(t => (uint)t).ToHashSet();
        var toRemove = Settings.Columns
            .Where(c => c.IsCurrency && !selectedIds.Contains(c.Id))
            .ToList();
        
        foreach (var col in toRemove)
        {
            Settings.Columns.Remove(col);
            changed = true;
        }
        
        if (changed)
        {
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
    }
    
    private void AddColumn(uint id, bool isCurrency)
    {
        // Check if already exists
        if (Settings.Columns.Any(c => c.Id == id && c.IsCurrency == isCurrency))
            return;
        
        Settings.Columns.Add(new ItemColumnConfig
        {
            Id = id,
            IsCurrency = isCurrency
        });
        
        _pendingRefresh = true;
        NotifyToolSettingsChanged();
    }
    
    private void RefreshData()
    {
        try
        {
            var settings = Settings;
            var allColumns = settings.Columns;
            
            // Apply special grouping filter to get visible columns
            var columns = SpecialGroupingHelper.ApplySpecialGroupingFilter(allColumns, settings.SpecialGrouping);
            
            if (columns.Count == 0)
            {
                _cachedData = new PreparedItemTableData
                {
                    Rows = Array.Empty<ItemTableCharacterRow>(),
                    Columns = columns
                };
                _lastRefresh = DateTime.UtcNow;
                _pendingRefresh = false;
                return;
            }
            
            // Get all character names with disambiguation
            var characterNames = DbService.GetAllCharacterNamesDict();
            var disambiguatedNames = CacheService.GetDisambiguatedNames(characterNames.Keys);
            var rows = new Dictionary<ulong, ItemTableCharacterRow>();
            
            // Get world data for DC/Region lookups (from PriceTrackingService)
            var worldData = _priceTrackingService?.WorldData;
            
            // Get character world info from AutoRetainer (maps CID to world name)
            var characterWorlds = new Dictionary<ulong, string>();
            if (_autoRetainerService != null && _autoRetainerService.IsAvailable)
            {
                var arData = _autoRetainerService.GetAllCharacterData();
                foreach (var (_, world, _, cid) in arData)
                {
                    if (!string.IsNullOrEmpty(world))
                    {
                        characterWorlds[cid] = world;
                    }
                }
            }
            
            // Get character filter (if using multi-select)
            HashSet<ulong>? allowedCharacters = null;
            if (settings.UseCharacterFilter && settings.SelectedCharacterIds.Count > 0)
            {
                allowedCharacters = settings.SelectedCharacterIds.ToHashSet();
            }
            
            // Initialize rows for all known characters (filtered if applicable)
            foreach (var (charId, name) in characterNames)
            {
                // Skip characters not in the allowed set (if filtering is enabled)
                if (allowedCharacters != null && !allowedCharacters.Contains(charId))
                    continue;
                
                var displayName = disambiguatedNames.TryGetValue(charId, out var formatted) 
                    ? formatted : name ?? $"CID:{charId}";
                
                // Get world info for this character
                var worldName = characterWorlds.TryGetValue(charId, out var w) ? w : string.Empty;
                var dcName = !string.IsNullOrEmpty(worldName) ? worldData?.GetDataCenterForWorld(worldName)?.Name ?? string.Empty : string.Empty;
                var regionName = !string.IsNullOrEmpty(worldName) ? worldData?.GetRegionForWorld(worldName) ?? string.Empty : string.Empty;
                
                rows[charId] = new ItemTableCharacterRow
                {
                    CharacterId = charId,
                    Name = displayName,
                    WorldName = worldName,
                    DataCenterName = dcName,
                    RegionName = regionName,
                    ItemCounts = new Dictionary<uint, long>()
                };
            }
            
            // Populate data for each column (including hidden ones that will be merged)
            // When merging gil, we need to populate FC Gil and Retainer Gil even though they're filtered out
            foreach (var column in allColumns)
            {
                if (column.IsCurrency)
                {
                    // Get currency data from sampler database (time series)
                    PopulateCurrencyData(column, rows);
                }
                else
                {
                    // Get item data from inventory cache
                    PopulateItemData(column, rows, settings.IncludeRetainers);
                }
            }
            
            // Apply gil merging if enabled
            if (settings.SpecialGrouping.AllGilEnabled && settings.SpecialGrouping.MergeGilCurrencies)
            {
                ApplyGilMerging(rows);
            }
            
            // Build result with sorted rows
            var sortedRows = CharacterSortHelper.SortByCharacter(
                rows.Values,
                _configService,
                _autoRetainerService,
                r => r.CharacterId,
                r => r.Name).ToList();
            
            _cachedData = new PreparedItemTableData
            {
                Rows = sortedRows,
                Columns = columns
            };
            
            _lastRefresh = DateTime.UtcNow;
            _pendingRefresh = false;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTableTool] RefreshData error: {ex.Message}");
        }
    }
    
    private void PopulateCurrencyData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        try
        {
            var dataType = (TrackedDataType)column.Id;
            var variableName = dataType.ToString();
            
            // Get latest value for each character
            var allPoints = DbService.GetAllPointsBatch(variableName, null);
            
            if (allPoints.TryGetValue(variableName, out var points))
            {
                var latestByChar = points
                    .GroupBy(p => p.characterId)
                    .Select(g => (charId: g.Key, value: g.OrderByDescending(p => p.timestamp).First().value));
                
                foreach (var (charId, value) in latestByChar)
                {
                    if (rows.TryGetValue(charId, out var row))
                    {
                        row.ItemCounts[column.Id] = value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTableTool] PopulateCurrencyData error: {ex.Message}");
        }
    }
    
    private void PopulateItemData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows, bool includeRetainers)
    {
        try
        {
            if (_inventoryCacheService == null) return;
            
            var allInventories = _inventoryCacheService.GetAllInventories();
            
            foreach (var cache in allInventories)
            {
                // Skip retainers if not included
                if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                    continue;
                
                if (!rows.TryGetValue(cache.CharacterId, out var row))
                    continue;
                
                // Sum up item count
                var count = cache.Items
                    .Where(i => i.ItemId == column.Id)
                    .Sum(i => (long)i.Quantity);
                
                if (!row.ItemCounts.ContainsKey(column.Id))
                    row.ItemCounts[column.Id] = 0;
                
                row.ItemCounts[column.Id] += count;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTableTool] PopulateItemData error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Merges FC Gil and Retainer Gil values into the main Gil column.
    /// </summary>
    private void ApplyGilMerging(Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        var gilId = (uint)TrackedDataType.Gil;
        var fcGilId = (uint)TrackedDataType.FreeCompanyGil;
        var retainerGilId = (uint)TrackedDataType.RetainerGil;
        
        foreach (var row in rows.Values)
        {
            // Get current values (default to 0 if not present)
            var gilValue = row.ItemCounts.TryGetValue(gilId, out var g) ? g : 0;
            var fcGilValue = row.ItemCounts.TryGetValue(fcGilId, out var fc) ? fc : 0;
            var retainerGilValue = row.ItemCounts.TryGetValue(retainerGilId, out var r) ? r : 0;
            
            // Merge into Gil
            row.ItemCounts[gilId] = gilValue + fcGilValue + retainerGilValue;
        }
    }
    
    protected override bool HasToolSettings => true;
    
    protected override void DrawToolSettings()
    {
        var settings = Settings;
        
        // Display Options Section
        ImGui.TextUnformatted("Display Options");
        ImGui.Separator();
        
        // Grouping mode
        var groupingMode = (int)settings.GroupingMode;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Group By", ref groupingMode, "Character\0World\0Data Center\0Region\0All\0"))
        {
            settings.GroupingMode = (Widgets.TableGroupingMode)groupingMode;
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Group rows by character, world, data center, region, or combine all into one row.\nWorld/DC/Region grouping requires character world information from AutoRetainer.");
        }
        
        // Include retainer inventory
        var includeRetainers = settings.IncludeRetainers;
        if (ImGui.Checkbox("Include Retainers", ref includeRetainers))
        {
            settings.IncludeRetainers = includeRetainers;
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Include retainer inventory in item counts");
        }
        
        // Show action buttons
        var showActionButtons = settings.ShowActionButtons;
        if (ImGui.Checkbox("Show Action Buttons", ref showActionButtons))
        {
            settings.ShowActionButtons = showActionButtons;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show the Add Item, Add Currency, and Refresh buttons");
        }
        
        // Compact numbers
        var useCompactNumbers = settings.UseCompactNumbers;
        if (ImGui.Checkbox("Compact Numbers", ref useCompactNumbers))
        {
            settings.UseCompactNumbers = useCompactNumbers;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Display large numbers in compact form (e.g., 10M instead of 10,000,000)");
        }
        
        // Color mode
        var textColorMode = (int)settings.TextColorMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Color Mode", ref textColorMode, "Don't use\0Use preferred item colors\0Use preferred character colors\0"))
        {
            settings.TextColorMode = (Widgets.TableTextColorMode)textColorMode;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Determines how cell text colors are applied.\n- Don't use: Only custom column colors are used.\n- Preferred item colors: Use colors from Data > Currencies and Data > Game Items config.\n- Preferred character colors: Use colors from Data > Characters config.");
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        ImGui.TextUnformatted("Column Management");
        ImGui.Separator();
        
        if (settings.Columns.Count == 0)
        {
            ImGui.TextDisabled("No columns configured. Add items or currencies above.");
        }
        else
        {
            // Build set of column indices that are part of merged groups
            var mergedColumnIndices = new HashSet<int>();
            foreach (var group in settings.MergedColumnGroups)
            {
                foreach (var idx in group.ColumnIndices)
                {
                    mergedColumnIndices.Add(idx);
                }
            }
            
            // Track which column to delete or swap (can't modify list during iteration)
            int deleteIndex = -1;
            int swapUpIndex = -1;
            int swapDownIndex = -1;
            
            for (int i = 0; i < settings.Columns.Count; i++)
            {
                // Skip columns that are part of a merged group
                if (mergedColumnIndices.Contains(i))
                    continue;
                
                var column = settings.Columns[i];
                var defaultName = _tableWidget.GetColumnHeader(new ItemColumnConfig { Id = column.Id, IsCurrency = column.IsCurrency });
                
                ImGui.PushID(i);
                
                // Color picker (small button)
                var color = column.Color ?? new Vector4(1f, 1f, 1f, 1f);
                var hasColor = column.Color.HasValue;
                
                // Use a colored button as a color indicator/picker trigger
                if (hasColor)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, color);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * 1.1f);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * 0.9f);
                }
                
                if (ImGui.Button(hasColor ? "##color" : "○##color", new Vector2(20, 0)))
                {
                    // Toggle color on/off when clicking the button
                    if (hasColor)
                    {
                        column.Color = null;
                        NotifyToolSettingsChanged();
                    }
                    else
                    {
                        ImGui.OpenPopup("ColorPicker");
                    }
                }
                
                if (hasColor)
                {
                    ImGui.PopStyleColor(3);
                }
                
                // Color picker popup
                if (ImGui.BeginPopup("ColorPicker"))
                {
                    if (ImGui.ColorPicker4("##picker", ref color, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoSmallPreview))
                    {
                        column.Color = color;
                        NotifyToolSettingsChanged();
                    }
                    ImGui.EndPopup();
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(hasColor ? "Click to remove color" : "Click to set color");
                }
                
                ImGui.SameLine();
                
                // Custom name input
                var customName = column.CustomName ?? string.Empty;
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputTextWithHint("##name", defaultName, ref customName, 64))
                {
                    column.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName;
                    NotifyToolSettingsChanged();
                }
                
                ImGui.SameLine();
                
                // Type label
                ImGui.TextDisabled(column.IsCurrency ? "[C]" : "[I]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(column.IsCurrency ? "Currency" : "Item");
                }
                
                ImGui.SameLine();
                
                // Store history checkbox (only for items, not currencies)
                if (!column.IsCurrency)
                {
                    var storeHistory = column.StoreHistory;
                    if (ImGui.Checkbox("##history", ref storeHistory))
                    {
                        column.StoreHistory = storeHistory;
                        NotifyToolSettingsChanged();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Store historical time-series data for this item");
                    }
                    ImGui.SameLine();
                }
                
                // Move up button
                ImGui.BeginDisabled(i == 0);
                if (ImGui.Button("▲##up", new Vector2(20, 0)))
                {
                    swapUpIndex = i;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move up");
                }
                
                ImGui.SameLine();
                
                // Move down button
                ImGui.BeginDisabled(i == settings.Columns.Count - 1);
                if (ImGui.Button("▼##down", new Vector2(20, 0)))
                {
                    swapDownIndex = i;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move down");
                }
                
                ImGui.SameLine();
                
                // Delete button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.15f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.2f, 0.2f, 1f));
                if (ImGui.Button("×##del", new Vector2(20, 0)))
                {
                    deleteIndex = i;
                }
                ImGui.PopStyleColor(2);
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Remove column");
                }
                
                ImGui.PopID();
            }
            
            // Process reordering and deletion after iteration
            if (swapUpIndex > 0)
            {
                var temp = settings.Columns[swapUpIndex - 1];
                settings.Columns[swapUpIndex - 1] = settings.Columns[swapUpIndex];
                settings.Columns[swapUpIndex] = temp;
                NotifyToolSettingsChanged();
            }
            else if (swapDownIndex >= 0 && swapDownIndex < settings.Columns.Count - 1)
            {
                var temp = settings.Columns[swapDownIndex + 1];
                settings.Columns[swapDownIndex + 1] = settings.Columns[swapDownIndex];
                settings.Columns[swapDownIndex] = temp;
                NotifyToolSettingsChanged();
            }
            else if (deleteIndex >= 0)
            {
                settings.Columns.RemoveAt(deleteIndex);
                _pendingRefresh = true;
                NotifyToolSettingsChanged();
            }
            
            // Merged columns section (inline with column management)
            if (settings.MergedColumnGroups.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Merged Column Groups:");
                
                int? groupToRemove = null;
                for (int i = 0; i < settings.MergedColumnGroups.Count; i++)
                {
                    var group = settings.MergedColumnGroups[i];
                    ImGui.PushID($"merged_{i}");
                    
                    // Color option
                    var hasGroupColor = group.Color.HasValue;
                    var groupColor = group.Color ?? new Vector4(1f, 1f, 1f, 1f);
                    
                    if (hasGroupColor)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, groupColor);
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, groupColor * 1.1f);
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, groupColor * 0.9f);
                    }
                    
                    if (ImGui.Button(hasGroupColor ? "##grpcolor" : "○##grpcolor", new Vector2(20, 0)))
                    {
                        if (hasGroupColor)
                        {
                            group.Color = null;
                            NotifyToolSettingsChanged();
                        }
                        else
                        {
                            ImGui.OpenPopup("MergedColorPicker");
                        }
                    }
                    
                    if (hasGroupColor)
                    {
                        ImGui.PopStyleColor(3);
                    }
                    
                    // Color picker popup
                    if (ImGui.BeginPopup("MergedColorPicker"))
                    {
                        if (ImGui.ColorPicker4("##grppicker", ref groupColor, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoSmallPreview))
                        {
                            group.Color = groupColor;
                            NotifyToolSettingsChanged();
                        }
                        ImGui.EndPopup();
                    }
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(hasGroupColor ? "Click to remove color" : "Click to set color");
                    }
                    
                    ImGui.SameLine();
                    
                    // Editable name
                    var name = group.Name;
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.InputText("##grpname", ref name, 64))
                    {
                        group.Name = name;
                        NotifyToolSettingsChanged();
                    }
                    
                    ImGui.SameLine();
                    
                    // Show which columns are merged
                    var columnNames = new List<string>();
                    foreach (var colIdx in group.ColumnIndices)
                    {
                        if (colIdx >= 0 && colIdx < settings.Columns.Count)
                        {
                            columnNames.Add(_tableWidget.GetColumnHeader(settings.Columns[colIdx]));
                        }
                    }
                    ImGui.TextDisabled($"({string.Join(" + ", columnNames)})");
                    
                    ImGui.SameLine();
                    
                    // Unmerge button
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.15f, 0.15f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.2f, 0.2f, 1f));
                    if (ImGui.Button("×##unmerge", new Vector2(20, 0)))
                    {
                        groupToRemove = i;
                    }
                    ImGui.PopStyleColor(2);
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Unmerge group");
                    }
                    
                    ImGui.PopID();
                }
                
                // Handle removal after iteration
                if (groupToRemove.HasValue)
                {
                    settings.MergedColumnGroups.RemoveAt(groupToRemove.Value);
                    _pendingRefresh = true;
                    NotifyToolSettingsChanged();
                }
            }
            
            // Hint for merging columns
            ImGui.Spacing();
            ImGui.TextDisabled("Tip: SHIFT+click column headers to select, then right-click to merge.");
        }
        
        // Special Grouping Section
        DrawSpecialGroupingSettings();
    }
    
    /// <summary>
    /// Draws the Special Grouping settings section.
    /// This section always appears and shows all possible special groupings.
    /// Unlocked groupings appear at the top, locked ones show requirements in tooltips.
    /// </summary>
    private void DrawSpecialGroupingSettings()
    {
        var settings = Settings;
        
        // Detect which special grouping is available
        var detectedGrouping = SpecialGroupingHelper.DetectSpecialGrouping(settings.Columns);
        
        // Update the active grouping setting
        if (settings.SpecialGrouping.ActiveGrouping != detectedGrouping)
        {
            settings.SpecialGrouping.ActiveGrouping = detectedGrouping;
            
            // If grouping was lost, disable filtering
            if (detectedGrouping == SpecialGroupingType.None)
            {
                settings.SpecialGrouping.Enabled = false;
            }
            
            NotifyToolSettingsChanged();
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        ImGui.TextUnformatted("Special Grouping");
        ImGui.Separator();
        
        // Build list of all special groupings, with unlocked ones first
        var allGroupings = new List<(SpecialGroupingType type, bool unlocked, string name, string tooltip)>
        {
            (
                SpecialGroupingType.AllGil,
                detectedGrouping.HasFlag(SpecialGroupingType.AllGil),
                "All Gil Currencies",
                "Requires all 3 gil currencies:\n" +
                "• Gil (personal)\n" +
                "• Free Company Gil\n" +
                "• Retainer Gil\n\n" +
                "Unlocks option to merge FC Gil and Retainer Gil into Gil."
            ),
            (
                SpecialGroupingType.AllCrystals,
                detectedGrouping.HasFlag(SpecialGroupingType.AllCrystals),
                "All Crystals",
                "Requires all 18 crystal types:\n" +
                "• Fire, Ice, Wind, Earth, Lightning, Water Shards\n" +
                "• Fire, Ice, Wind, Earth, Lightning, Water Crystals\n" +
                "• Fire, Ice, Wind, Earth, Lightning, Water Clusters\n\n" +
                "Unlocks element and tier filtering."
            )
        };
        
        // Sort so unlocked groupings appear first
        var sortedGroupings = allGroupings.OrderByDescending(g => g.unlocked).ToList();
        
        // Draw each grouping
        foreach (var (type, unlocked, name, tooltip) in sortedGroupings)
        {
            DrawSpecialGroupingItem(settings, type, unlocked, name, tooltip, detectedGrouping);
        }
    }
    
    /// <summary>
    /// Draws a single special grouping item with checkbox (if unlocked) or disabled text (if locked).
    /// </summary>
    private void DrawSpecialGroupingItem(ItemTableSettings settings, SpecialGroupingType type, bool unlocked, string name, string tooltip, SpecialGroupingType detectedGrouping)
    {
        if (unlocked)
        {
            // Get the appropriate enabled state for this specific grouping type
            var isEnabled = type switch
            {
                SpecialGroupingType.AllCrystals => settings.SpecialGrouping.AllCrystalsEnabled,
                SpecialGroupingType.AllGil => settings.SpecialGrouping.AllGilEnabled,
                _ => false
            };
            
            // Green checkmark indicator
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.4f, 1.0f, 0.4f, 1.0f));
            ImGui.TextUnformatted("✓");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            
            if (ImGui.Checkbox($"{name}##special_{type}", ref isEnabled))
            {
                // Update the appropriate enabled flag
                switch (type)
                {
                    case SpecialGroupingType.AllCrystals:
                        settings.SpecialGrouping.AllCrystalsEnabled = isEnabled;
                        break;
                    case SpecialGroupingType.AllGil:
                        settings.SpecialGrouping.AllGilEnabled = isEnabled;
                        break;
                }
                _pendingRefresh = true;
                NotifyToolSettingsChanged();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{tooltip}\n\n[UNLOCKED] Click to {(isEnabled ? "disable" : "enable")} filters.");
            }
            
            // Draw the filters if this grouping is enabled
            if (isEnabled)
            {
                switch (type)
                {
                    case SpecialGroupingType.AllCrystals:
                        DrawCrystalFilters(settings);
                        break;
                    case SpecialGroupingType.AllGil:
                        DrawGilFilters(settings);
                        break;
                }
            }
        }
        else
        {
            // Show locked grouping with lock icon
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            ImGui.TextUnformatted("🔒");
            ImGui.SameLine();
            ImGui.TextDisabled(name);
            ImGui.PopStyleColor();
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{tooltip}\n\n[LOCKED] Add the required items to unlock this filter.");
            }
        }
    }
    
    /// <summary>
    /// Draws the gil merge option checkbox.
    /// </summary>
    private void DrawGilFilters(ItemTableSettings settings)
    {
        ImGui.Indent();
        
        var mergeGil = settings.SpecialGrouping.MergeGilCurrencies;
        if (ImGui.Checkbox("Merge into Gil##mergeGil", ref mergeGil))
        {
            settings.SpecialGrouping.MergeGilCurrencies = mergeGil;
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, Free Company Gil and Retainer Gil columns\nare hidden and their values are added to the Gil column.");
        }
        
        if (mergeGil)
        {
            ImGui.TextDisabled("FC Gil + Retainer Gil → Gil");
        }
        
        ImGui.Unindent();
    }

    /// <summary>
    /// Draws the crystal element and tier filter checkboxes.
    /// </summary>
    private void DrawCrystalFilters(ItemTableSettings settings)
    {
        ImGui.Indent();
        
        // Element filters
        ImGui.TextDisabled("Elements:");
        ImGui.SameLine();
        
        // Select All / Deselect All for elements
        if (ImGui.SmallButton("All##elements"))
        {
            foreach (CrystalElement element in Enum.GetValues<CrystalElement>())
            {
                settings.SpecialGrouping.EnabledElements.Add(element);
            }
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("None##elements"))
        {
            settings.SpecialGrouping.EnabledElements.Clear();
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
        
        // Draw element checkboxes in a row with colored text
        foreach (CrystalElement element in Enum.GetValues<CrystalElement>())
        {
            var elementEnabled = settings.SpecialGrouping.EnabledElements.Contains(element);
            var elementColor = SpecialGroupingHelper.GetElementColor(element);
            
            ImGui.PushStyleColor(ImGuiCol.Text, elementColor);
            if (ImGui.Checkbox($"{SpecialGroupingHelper.GetElementName(element)}##element", ref elementEnabled))
            {
                if (elementEnabled)
                    settings.SpecialGrouping.EnabledElements.Add(element);
                else
                    settings.SpecialGrouping.EnabledElements.Remove(element);
                _pendingRefresh = true;
                NotifyToolSettingsChanged();
            }
            ImGui.PopStyleColor();
            
            // Put elements on same line (except last)
            if (element != CrystalElement.Water)
                ImGui.SameLine();
        }
        
        ImGui.Spacing();
        
        // Tier filters
        ImGui.TextDisabled("Tiers:");
        ImGui.SameLine();
        
        // Select All / Deselect All for tiers
        if (ImGui.SmallButton("All##tiers"))
        {
            foreach (CrystalTier tier in Enum.GetValues<CrystalTier>())
            {
                settings.SpecialGrouping.EnabledTiers.Add(tier);
            }
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("None##tiers"))
        {
            settings.SpecialGrouping.EnabledTiers.Clear();
            _pendingRefresh = true;
            NotifyToolSettingsChanged();
        }
        
        // Draw tier checkboxes in a row
        foreach (CrystalTier tier in Enum.GetValues<CrystalTier>())
        {
            var tierEnabled = settings.SpecialGrouping.EnabledTiers.Contains(tier);
            if (ImGui.Checkbox($"{SpecialGroupingHelper.GetTierName(tier)}##tier", ref tierEnabled))
            {
                if (tierEnabled)
                    settings.SpecialGrouping.EnabledTiers.Add(tier);
                else
                    settings.SpecialGrouping.EnabledTiers.Remove(tier);
                _pendingRefresh = true;
                NotifyToolSettingsChanged();
            }
            
            // Put tiers on same line (except last)
            if (tier != CrystalTier.Cluster)
                ImGui.SameLine();
        }
        
        // Show count of visible crystals
        var visibleCount = settings.SpecialGrouping.EnabledElements.Count * settings.SpecialGrouping.EnabledTiers.Count;
        ImGui.TextDisabled($"Showing {visibleCount}/18 crystal types");
        
        ImGui.Unindent();
    }

    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = _instanceSettings;
        
        // Serialize columns as a list of dictionaries
        var columns = settings.Columns.Select(c => new Dictionary<string, object?>
        {
            ["Id"] = c.Id,
            ["CustomName"] = c.CustomName,
            ["IsCurrency"] = c.IsCurrency,
            ["Color"] = c.Color.HasValue ? new float[] { c.Color.Value.X, c.Color.Value.Y, c.Color.Value.Z, c.Color.Value.W } : null,
            ["Width"] = c.Width,
            ["StoreHistory"] = c.StoreHistory
        }).ToList();
        
        // Serialize merged column groups
        var mergedColumnGroups = settings.MergedColumnGroups.Select(g => new Dictionary<string, object?>
        {
            ["Name"] = g.Name,
            ["ColumnIndices"] = g.ColumnIndices.ToList(),
            ["Color"] = g.Color.HasValue ? new float[] { g.Color.Value.X, g.Color.Value.Y, g.Color.Value.Z, g.Color.Value.W } : null,
            ["Width"] = g.Width
        }).ToList();
        
        // Serialize merged row groups
        var mergedRowGroups = settings.MergedRowGroups.Select(g => new Dictionary<string, object?>
        {
            ["Name"] = g.Name,
            ["CharacterIds"] = g.CharacterIds.ToList(),
            ["Color"] = g.Color.HasValue ? new float[] { g.Color.Value.X, g.Color.Value.Y, g.Color.Value.Z, g.Color.Value.W } : null
        }).ToList();
        
        return new Dictionary<string, object?>
        {
            ["Columns"] = columns,
            ["MergedColumnGroups"] = mergedColumnGroups,
            ["MergedRowGroups"] = mergedRowGroups,
            ["ShowTotalRow"] = settings.ShowTotalRow,
            ["Sortable"] = settings.Sortable,
            ["IncludeRetainers"] = settings.IncludeRetainers,
            ["CharacterColumnWidth"] = settings.CharacterColumnWidth,
            ["CharacterColumnColor"] = settings.CharacterColumnColor.HasValue ? new float[] { settings.CharacterColumnColor.Value.X, settings.CharacterColumnColor.Value.Y, settings.CharacterColumnColor.Value.Z, settings.CharacterColumnColor.Value.W } : null,
            ["SortColumnIndex"] = settings.SortColumnIndex,
            ["SortAscending"] = settings.SortAscending,
            ["ShowActionButtons"] = settings.ShowActionButtons,
            ["UseCompactNumbers"] = settings.UseCompactNumbers,
            ["HeaderColor"] = settings.HeaderColor.HasValue ? new float[] { settings.HeaderColor.Value.X, settings.HeaderColor.Value.Y, settings.HeaderColor.Value.Z, settings.HeaderColor.Value.W } : null,
            ["EvenRowColor"] = settings.EvenRowColor.HasValue ? new float[] { settings.EvenRowColor.Value.X, settings.EvenRowColor.Value.Y, settings.EvenRowColor.Value.Z, settings.EvenRowColor.Value.W } : null,
            ["OddRowColor"] = settings.OddRowColor.HasValue ? new float[] { settings.OddRowColor.Value.X, settings.OddRowColor.Value.Y, settings.OddRowColor.Value.Z, settings.OddRowColor.Value.W } : null,
            ["UseFullNameWidth"] = settings.UseFullNameWidth,
            ["AutoSizeEqualColumns"] = settings.AutoSizeEqualColumns,
            ["HorizontalAlignment"] = (int)settings.HorizontalAlignment,
            ["VerticalAlignment"] = (int)settings.VerticalAlignment,
            ["CharacterColumnHorizontalAlignment"] = (int)settings.CharacterColumnHorizontalAlignment,
            ["CharacterColumnVerticalAlignment"] = (int)settings.CharacterColumnVerticalAlignment,
            ["HeaderHorizontalAlignment"] = (int)settings.HeaderHorizontalAlignment,
            ["HeaderVerticalAlignment"] = (int)settings.HeaderVerticalAlignment,
            ["HiddenCharacters"] = settings.HiddenCharacters.ToList(),
            ["GroupingMode"] = (int)settings.GroupingMode,
            ["HideCharacterColumnInAllMode"] = settings.HideCharacterColumnInAllMode,
            ["TextColorMode"] = (int)settings.TextColorMode,
            ["UseCharacterFilter"] = settings.UseCharacterFilter,
            ["SelectedCharacterIds"] = settings.SelectedCharacterIds.ToList(),
            // Special Grouping settings
            ["SpecialGroupingEnabled"] = settings.SpecialGrouping.Enabled,
            ["SpecialGroupingType"] = (int)settings.SpecialGrouping.ActiveGrouping,
            ["SpecialGroupingEnabledElements"] = settings.SpecialGrouping.EnabledElements.Select(e => (int)e).ToList(),
            ["SpecialGroupingEnabledTiers"] = settings.SpecialGrouping.EnabledTiers.Select(t => (int)t).ToList(),
            ["SpecialGroupingAllCrystalsEnabled"] = settings.SpecialGrouping.AllCrystalsEnabled,
            ["SpecialGroupingAllGilEnabled"] = settings.SpecialGrouping.AllGilEnabled,
            ["SpecialGroupingMergeGilCurrencies"] = settings.SpecialGrouping.MergeGilCurrencies
        };
    }
    
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        var target = _instanceSettings;
        
        // Import columns
        if (settings.TryGetValue("Columns", out var columnsObj) && columnsObj != null)
        {
            target.Columns.Clear();
            
            try
            {
                // Handle Newtonsoft.Json JArray (used by ConfigManager)
                if (columnsObj is Newtonsoft.Json.Linq.JArray jArray)
                {
                    foreach (var columnToken in jArray)
                    {
                        if (columnToken is not Newtonsoft.Json.Linq.JObject columnObj) continue;
                        
                        var column = new ItemColumnConfig
                        {
                            Id = columnObj["Id"]?.ToObject<uint>() ?? 0,
                            CustomName = columnObj["CustomName"]?.ToObject<string>(),
                            IsCurrency = columnObj["IsCurrency"]?.ToObject<bool>() ?? false,
                            Width = columnObj["Width"]?.ToObject<float>() ?? 80f,
                            StoreHistory = columnObj["StoreHistory"]?.ToObject<bool>() ?? false
                        };
                        
                        var colorToken = columnObj["Color"];
                        if (colorToken is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
                        {
                            column.Color = new System.Numerics.Vector4(
                                colorArr[0].ToObject<float>(),
                                colorArr[1].ToObject<float>(),
                                colorArr[2].ToObject<float>(),
                                colorArr[3].ToObject<float>());
                        }
                        
                        target.Columns.Add(column);
                    }
                }
                // Fallback: Handle System.Text.Json.JsonElement (in case it's used elsewhere)
                else if (columnsObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var columnJson in jsonElement.EnumerateArray())
                    {
                        var column = new ItemColumnConfig
                        {
                            Id = columnJson.TryGetProperty("Id", out var idProp) ? idProp.GetUInt32() : 0,
                            CustomName = columnJson.TryGetProperty("CustomName", out var nameProp) && nameProp.ValueKind != System.Text.Json.JsonValueKind.Null ? nameProp.GetString() : null,
                            IsCurrency = columnJson.TryGetProperty("IsCurrency", out var currProp) && currProp.GetBoolean(),
                            Width = columnJson.TryGetProperty("Width", out var widthProp) ? widthProp.GetSingle() : 80f,
                            StoreHistory = columnJson.TryGetProperty("StoreHistory", out var histProp) && histProp.GetBoolean()
                        };
                        
                        if (columnJson.TryGetProperty("Color", out var colorProp) && colorProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var colorArr = colorProp.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                            if (colorArr.Length >= 4)
                                column.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                        }
                        
                        target.Columns.Add(column);
                    }
                }
                // Handle in-memory List<Dictionary<string, object?>> (from direct ExportToolSettings)
                else if (columnsObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is not IDictionary<string, object?> columnDict) continue;
                        
                        var column = new ItemColumnConfig
                        {
                            Id = columnDict.TryGetValue("Id", out var idVal) && idVal != null ? Convert.ToUInt32(idVal) : 0,
                            CustomName = columnDict.TryGetValue("CustomName", out var nameVal) ? nameVal?.ToString() : null,
                            IsCurrency = columnDict.TryGetValue("IsCurrency", out var currVal) && currVal is bool b && b,
                            Width = columnDict.TryGetValue("Width", out var widthVal) && widthVal != null ? Convert.ToSingle(widthVal) : 80f,
                            StoreHistory = columnDict.TryGetValue("StoreHistory", out var histVal) && histVal is bool h && h
                        };
                        
                        if (columnDict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
                        {
                            column.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                        }
                        
                        target.Columns.Add(column);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[DataTableTool] Error importing columns: {ex.Message}");
            }
        }
        
        // Import scalar settings
        target.ShowTotalRow = SettingsImportHelper.GetSetting(settings, "ShowTotalRow", target.ShowTotalRow);
        target.Sortable = SettingsImportHelper.GetSetting(settings, "Sortable", target.Sortable);
        target.IncludeRetainers = SettingsImportHelper.GetSetting(settings, "IncludeRetainers", target.IncludeRetainers);
        target.CharacterColumnWidth = SettingsImportHelper.GetSetting(settings, "CharacterColumnWidth", target.CharacterColumnWidth);
        target.SortColumnIndex = SettingsImportHelper.GetSetting(settings, "SortColumnIndex", target.SortColumnIndex);
        target.SortAscending = SettingsImportHelper.GetSetting(settings, "SortAscending", target.SortAscending);
        target.ShowActionButtons = SettingsImportHelper.GetSetting(settings, "ShowActionButtons", target.ShowActionButtons);
        target.UseCompactNumbers = SettingsImportHelper.GetSetting(settings, "UseCompactNumbers", target.UseCompactNumbers);
        target.UseFullNameWidth = SettingsImportHelper.GetSetting(settings, "UseFullNameWidth", target.UseFullNameWidth);
        target.AutoSizeEqualColumns = SettingsImportHelper.GetSetting(settings, "AutoSizeEqualColumns", target.AutoSizeEqualColumns);
        target.HorizontalAlignment = (Widgets.TableHorizontalAlignment)SettingsImportHelper.GetSetting(settings, "HorizontalAlignment", (int)target.HorizontalAlignment);
        target.VerticalAlignment = (Widgets.TableVerticalAlignment)SettingsImportHelper.GetSetting(settings, "VerticalAlignment", (int)target.VerticalAlignment);
        target.CharacterColumnHorizontalAlignment = (Widgets.TableHorizontalAlignment)SettingsImportHelper.GetSetting(settings, "CharacterColumnHorizontalAlignment", (int)target.CharacterColumnHorizontalAlignment);
        target.CharacterColumnVerticalAlignment = (Widgets.TableVerticalAlignment)SettingsImportHelper.GetSetting(settings, "CharacterColumnVerticalAlignment", (int)target.CharacterColumnVerticalAlignment);
        target.HeaderHorizontalAlignment = (Widgets.TableHorizontalAlignment)SettingsImportHelper.GetSetting(settings, "HeaderHorizontalAlignment", (int)target.HeaderHorizontalAlignment);
        target.HeaderVerticalAlignment = (Widgets.TableVerticalAlignment)SettingsImportHelper.GetSetting(settings, "HeaderVerticalAlignment", (int)target.HeaderVerticalAlignment);
        
        // Import colors
        target.CharacterColumnColor = SettingsImportHelper.ImportColor(settings, "CharacterColumnColor");
        target.HeaderColor = SettingsImportHelper.ImportColor(settings, "HeaderColor");
        target.EvenRowColor = SettingsImportHelper.ImportColor(settings, "EvenRowColor");
        target.OddRowColor = SettingsImportHelper.ImportColor(settings, "OddRowColor");
        
        // Import hidden characters
        target.HiddenCharacters = SettingsImportHelper.ImportUlongHashSet(settings, "HiddenCharacters") ?? new HashSet<ulong>();
        
        // Import merged column groups
        target.MergedColumnGroups = ImportMergedColumnGroups(settings, "MergedColumnGroups") ?? new List<Widgets.MergedColumnGroup>();
        
        // Import merged row groups
        target.MergedRowGroups = ImportMergedRowGroups(settings, "MergedRowGroups") ?? new List<Widgets.MergedRowGroup>();
        
        // Import grouping mode
        target.GroupingMode = (Widgets.TableGroupingMode)SettingsImportHelper.GetSetting(settings, "GroupingMode", (int)target.GroupingMode);
        target.HideCharacterColumnInAllMode = SettingsImportHelper.GetSetting(settings, "HideCharacterColumnInAllMode", target.HideCharacterColumnInAllMode);
        
        // Import text color mode
        target.TextColorMode = (Widgets.TableTextColorMode)SettingsImportHelper.GetSetting(settings, "TextColorMode", (int)target.TextColorMode);
        
        // Import character filter settings
        target.UseCharacterFilter = SettingsImportHelper.GetSetting(settings, "UseCharacterFilter", target.UseCharacterFilter);
        var selectedIds = SettingsImportHelper.ImportUlongList(settings, "SelectedCharacterIds");
        if (selectedIds != null)
        {
            target.SelectedCharacterIds.Clear();
            target.SelectedCharacterIds.AddRange(selectedIds);
        }
        
        // Update CharacterCombo to reflect imported settings
        if (_characterCombo != null)
        {
            _characterCombo.MultiSelectEnabled = true;
            if (target.UseCharacterFilter && target.SelectedCharacterIds.Count > 0)
            {
                _characterCombo.SetSelection(target.SelectedCharacterIds);
            }
            else
            {
                _characterCombo.SelectAll();
            }
        }
        
        // Import special grouping settings
        target.SpecialGrouping.Enabled = SettingsImportHelper.GetSetting(settings, "SpecialGroupingEnabled", target.SpecialGrouping.Enabled);
        target.SpecialGrouping.ActiveGrouping = (SpecialGroupingType)SettingsImportHelper.GetSetting(settings, "SpecialGroupingType", (int)target.SpecialGrouping.ActiveGrouping);
        
        // Import enabled elements
        var enabledElements = SettingsImportHelper.ImportIntList(settings, "SpecialGroupingEnabledElements");
        if (enabledElements != null)
        {
            target.SpecialGrouping.EnabledElements.Clear();
            foreach (var e in enabledElements)
            {
                target.SpecialGrouping.EnabledElements.Add((CrystalElement)e);
            }
        }
        
        // Import enabled tiers
        var enabledTiers = SettingsImportHelper.ImportIntList(settings, "SpecialGroupingEnabledTiers");
        if (enabledTiers != null)
        {
            target.SpecialGrouping.EnabledTiers.Clear();
            foreach (var t in enabledTiers)
            {
                target.SpecialGrouping.EnabledTiers.Add((CrystalTier)t);
            }
        }
        
        // Import new per-grouping enabled flags
        target.SpecialGrouping.AllCrystalsEnabled = SettingsImportHelper.GetSetting(settings, "SpecialGroupingAllCrystalsEnabled", target.SpecialGrouping.AllCrystalsEnabled);
        target.SpecialGrouping.AllGilEnabled = SettingsImportHelper.GetSetting(settings, "SpecialGroupingAllGilEnabled", target.SpecialGrouping.AllGilEnabled);
        target.SpecialGrouping.MergeGilCurrencies = SettingsImportHelper.GetSetting(settings, "SpecialGroupingMergeGilCurrencies", target.SpecialGrouping.MergeGilCurrencies);
        
        _pendingRefresh = true;
    }
    
    private static List<Widgets.MergedColumnGroup>? ImportMergedColumnGroups(Dictionary<string, object?>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;
        
        var result = new List<Widgets.MergedColumnGroup>();
        
        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JArray jArray)
            {
                foreach (var item in jArray)
                {
                    if (item is not Newtonsoft.Json.Linq.JObject jObj) continue;
                    
                    var group = new Widgets.MergedColumnGroup
                    {
                        Name = jObj["Name"]?.ToObject<string>() ?? "Merged",
                        Width = jObj["Width"]?.ToObject<float>() ?? 80f
                    };
                    
                    // Import column indices
                    if (jObj["ColumnIndices"] is Newtonsoft.Json.Linq.JArray indicesArr)
                    {
                        group.ColumnIndices = indicesArr.Select(v => v.ToObject<int>()).ToList();
                    }
                    
                    // Import color
                    if (jObj["Color"] is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(
                            colorArr[0].ToObject<float>(),
                            colorArr[1].ToObject<float>(),
                            colorArr[2].ToObject<float>(),
                            colorArr[3].ToObject<float>());
                    }
                    
                    result.Add(group);
                }
                return result;
            }
            // Handle in-memory List<Dictionary<string, object?>> (from direct ExportToolSettings)
            else if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is not IDictionary<string, object?> dict) continue;
                    
                    var group = new Widgets.MergedColumnGroup
                    {
                        Name = dict.TryGetValue("Name", out var nameVal) ? nameVal?.ToString() ?? "Merged" : "Merged",
                        Width = dict.TryGetValue("Width", out var widthVal) && widthVal != null ? Convert.ToSingle(widthVal) : 80f
                    };
                    
                    // Import column indices
                    if (dict.TryGetValue("ColumnIndices", out var indicesVal) && indicesVal is System.Collections.IEnumerable indicesEnum)
                    {
                        group.ColumnIndices = new List<int>();
                        foreach (var idx in indicesEnum)
                        {
                            if (idx != null)
                                group.ColumnIndices.Add(Convert.ToInt32(idx));
                        }
                    }
                    
                    // Import color
                    if (dict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                    }
                    
                    result.Add(group);
                }
                return result.Count > 0 ? result : null;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTableTool] Error importing merged column groups: {ex.Message}");
        }
        
        return result.Count > 0 ? result : null;
    }
    
    private static List<Widgets.MergedRowGroup>? ImportMergedRowGroups(Dictionary<string, object?>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;
        
        var result = new List<Widgets.MergedRowGroup>();
        
        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JArray jArray)
            {
                foreach (var item in jArray)
                {
                    if (item is not Newtonsoft.Json.Linq.JObject jObj) continue;
                    
                    var group = new Widgets.MergedRowGroup
                    {
                        Name = jObj["Name"]?.ToObject<string>() ?? "Merged"
                    };
                    
                    // Import character IDs
                    if (jObj["CharacterIds"] is Newtonsoft.Json.Linq.JArray idsArr)
                    {
                        group.CharacterIds = idsArr.Select(v => v.ToObject<ulong>()).ToList();
                    }
                    
                    // Import color
                    if (jObj["Color"] is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(
                            colorArr[0].ToObject<float>(),
                            colorArr[1].ToObject<float>(),
                            colorArr[2].ToObject<float>(),
                            colorArr[3].ToObject<float>());
                    }
                    
                    result.Add(group);
                }
                return result;
            }
            // Handle in-memory List<Dictionary<string, object?>> (from direct ExportToolSettings)
            else if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is not IDictionary<string, object?> dict) continue;
                    
                    var group = new Widgets.MergedRowGroup
                    {
                        Name = dict.TryGetValue("Name", out var nameVal) ? nameVal?.ToString() ?? "Merged" : "Merged"
                    };
                    
                    // Import character IDs
                    if (dict.TryGetValue("CharacterIds", out var idsVal) && idsVal is System.Collections.IEnumerable idsEnum)
                    {
                        group.CharacterIds = new List<ulong>();
                        foreach (var id in idsEnum)
                        {
                            if (id != null)
                                group.CharacterIds.Add(Convert.ToUInt64(id));
                        }
                    }
                    
                    // Import color
                    if (dict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                    }
                    
                    result.Add(group);
                }
                return result.Count > 0 ? result : null;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTableTool] Error importing merged row groups: {ex.Message}");
        }
        
        return result.Count > 0 ? result : null;
    }
    
    public override void Dispose()
    {
        _characterCombo?.Dispose();
        base.Dispose();
    }
}
