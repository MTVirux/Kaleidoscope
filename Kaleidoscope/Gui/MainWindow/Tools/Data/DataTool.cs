using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using CrystalElement = Kaleidoscope.CrystalElement;
using CrystalTier = Kaleidoscope.CrystalTier;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// Unified tool component that can display data as either a table or a graph.
/// Maintains all settings when switching between views.
/// </summary>
public class DataTool : ToolComponent
{
    public override string ToolName => "Data";
    
    private readonly CurrencyTrackerService _CurrencyTrackerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly FavoritesService? _favoritesService;
    private readonly ITextureProvider? _textureProvider;
    
    // Widgets
    private readonly ItemTableWidget _tableWidget;
    private readonly ImplotGraphWidget _graphWidget;
    private readonly ItemComboDropdown? _itemCombo;
    private readonly CurrencyComboDropdown? _currencyCombo;
    private readonly CharacterCombo? _characterCombo;
    
    // Instance-specific settings
    private readonly DataToolSettings _instanceSettings;
    
    // Table view cached data
    private PreparedItemTableData? _cachedTableData;
    private DateTime _lastTableRefresh = DateTime.MinValue;
    private volatile bool _pendingTableRefresh = true;
    
    // Graph view cached data (tuple format matching ImplotGraphWidget.RenderMultipleSeries)
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? _cachedSeriesData;
    private DateTime _lastGraphRefresh = DateTime.MinValue;
    private volatile bool _graphCacheIsDirty = true;
    private int _cachedSeriesCount;
    private int _cachedTimeRangeValue;
    private TimeUnit _cachedTimeRangeUnit;
    private bool _cachedIncludeRetainers;
    private TableGroupingMode _cachedGroupingMode;
    
    // Retainer names cache (refreshed periodically)
    private Dictionary<ulong, string>? _cachedRetainerNames;
    private DateTime _lastRetainerNamesCacheRefresh = DateTime.MinValue;
    private static readonly TimeSpan RetainerNamesCacheExpiry = TimeSpan.FromMinutes(5);
    
    // Shared cached state
    private CharacterNameFormat _cachedNameFormat;
    private CharacterSortOrder _cachedSortOrder;
    
    /// <summary>
    /// The name of the preset used to create this tool, if any.
    /// </summary>
    public string? PresetName { get; set; }
    
    private DataToolSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _CurrencyTrackerService.DbService;
    private TimeSeriesCacheService CacheService => _CurrencyTrackerService.CacheService;
    
    public DataTool(
        CurrencyTrackerService CurrencyTrackerService,
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
        _CurrencyTrackerService = CurrencyTrackerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _trackedDataRegistry = trackedDataRegistry;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        _favoritesService = favoritesService;
        _textureProvider = textureProvider;
        
        // Initialize instance-specific settings
        _instanceSettings = new DataToolSettings();
        
        Size = new Vector2(500, 300);
        UpdateTitle();
        
        // Create the table widget
        _tableWidget = new ItemTableWidget(
            new ItemTableWidget.TableConfig
            {
                TableId = "DataToolTable",
                NoDataText = "No data yet. Add items or currencies to track."
            },
            itemDataService,
            trackedDataRegistry,
            configService.Config,
            CurrencyTrackerService.CacheService);
        
        // Bind table widget to settings
        _tableWidget.BindSettings(
            _instanceSettings,
            () => NotifyToolSettingsChanged(),
            "Table Settings");
        
        // Create the graph widget
        _graphWidget = new ImplotGraphWidget(new ImplotGraphWidget.GraphConfig
        {
            PlotId = "DataToolGraph",
            MinValue = 0f,
            MaxValue = 100_000_000f,
            NoDataText = "No historical data available."
        });
        
        // Bind graph widget to settings
        _graphWidget.BindSettings(
            _instanceSettings,
            () => { _graphCacheIsDirty = true; NotifyToolSettingsChanged(); },
            "Graph Settings");
        
        // Subscribe to auto-scroll settings changes from controls drawer
        _graphWidget.OnAutoScrollSettingsChanged += OnAutoScrollSettingsChanged;
        
        // Create item combo
        if (_dataManager != null && _itemDataService != null && textureProvider != null && favoritesService != null)
        {
            _itemCombo = new ItemComboDropdown(
                textureProvider,
                _dataManager,
                favoritesService,
                null,
                "DataToolItemAdd",
                marketableOnly: false,
                configService: configService,
                trackedDataRegistry: trackedDataRegistry,
                excludeCurrencies: true);
        }
        
        // Create currency combo
        if (textureProvider != null && trackedDataRegistry != null && favoritesService != null)
        {
            _currencyCombo = new CurrencyComboDropdown(
                textureProvider,
                trackedDataRegistry,
                favoritesService,
                "DataToolCurrencyAdd",
                itemDataService);
        }
        
        // Create character combo
        if (favoritesService != null)
        {
            _characterCombo = new CharacterCombo(
                CurrencyTrackerService,
                favoritesService,
                configService,
                "DataToolCharFilter",
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
        
        // Register widgets as settings providers
        RegisterSettingsProvider(_tableWidget);
        RegisterSettingsProvider(_graphWidget);
    }
    
    /// <summary>
    /// Sets the columns/series for this tool. Used by presets.
    /// </summary>
    public void SetColumns(List<ItemColumnConfig> columns)
    {
        _instanceSettings.Columns.Clear();
        _instanceSettings.Columns.AddRange(columns);
        _pendingTableRefresh = true;
        _graphCacheIsDirty = true;
    }
    
    /// <summary>
    /// Gets the current columns/series being tracked.
    /// </summary>
    public IReadOnlyList<ItemColumnConfig> GetColumns() => _instanceSettings.Columns;
    
    /// <summary>
    /// Configures settings. Used by presets.
    /// </summary>
    public void ConfigureSettings(Action<DataToolSettings> configure)
    {
        configure(_instanceSettings);
        _pendingTableRefresh = true;
        _graphCacheIsDirty = true;
    }
    
    private void UpdateTitle()
    {
        var viewSuffix = Settings.ViewMode == DataToolViewMode.Table ? "Table" : "Graph";
        Title = string.IsNullOrWhiteSpace(PresetName) 
            ? $"Data {viewSuffix}" 
            : $"Data {viewSuffix} - {PresetName}";
    }
    
    private void OnCharacterSelectionChanged(IReadOnlySet<ulong> selectedIds)
    {
        Settings.SelectedCharacterIds.Clear();
        Settings.SelectedCharacterIds.AddRange(selectedIds);
        Settings.UseCharacterFilter = selectedIds.Count > 0;
        _pendingTableRefresh = true;
        _graphCacheIsDirty = true;
        NotifyToolSettingsChanged();
    }
    
    public override void RenderToolContent()
    {
        try
        {
            // Check if name format changed
            var currentFormat = _configService.Config.CharacterNameFormat;
            if (_cachedNameFormat != currentFormat)
            {
                _cachedNameFormat = currentFormat;
                _pendingTableRefresh = true;
                _graphCacheIsDirty = true;
            }
            
            // Check if sort order changed
            var currentSortOrder = _configService.Config.CharacterSortOrder;
            if (_cachedSortOrder != currentSortOrder)
            {
                _cachedSortOrder = currentSortOrder;
                _pendingTableRefresh = true;
            }
            
            // Draw action buttons
            if (Settings.ShowActionButtons)
            {
                DrawActionButtons();
                ImGui.Separator();
            }
            
            // Draw based on view mode
            if (Settings.ViewMode == DataToolViewMode.Table)
            {
                DrawTableView();
            }
            else
            {
                DrawGraphView();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[DataTool] Draw error: {ex.Message}");
        }
    }
    
    private void DrawActionButtons()
    {
        // View toggle button
        var isGraphView = Settings.ViewMode == DataToolViewMode.Graph;
        var toggleLabel = isGraphView ? "📊" : "📈";
        
        // Check for items without history tracking (only items need this, currencies are always tracked)
        var itemsWithoutHistory = Settings.Columns
            .Where(c => !c.IsCurrency && !_configService.Config.ItemsWithHistoricalTracking.Contains(c.Id))
            .ToList();
        var hasHistoryWarning = itemsWithoutHistory.Count > 0;
        
        // Build tooltip
        var toggleTooltip = isGraphView ? "Switch to Table View" : "Switch to Graph View";
        if (hasHistoryWarning && !isGraphView)
        {
            toggleTooltip += $"\n\n⚠ Warning: {itemsWithoutHistory.Count} item(s) do not have historical tracking enabled.";
            toggleTooltip += "\n\nThese items will not display time-series data in graph view.\nEnable historical tracking in Settings for each item.";
        }
        
        if (ImGuiHelpers.ButtonAutoWidth(toggleLabel, 8f))
        {
            Settings.ViewMode = isGraphView ? DataToolViewMode.Table : DataToolViewMode.Graph;
            UpdateTitle();
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(toggleTooltip);
        }
        
        ImGui.SameLine();
        
        // Character filter combo
        if (_characterCombo != null)
        {
            ImGui.SetNextItemWidth(160);
            _characterCombo.Draw(160);
            ImGui.SameLine();
        }
        
        // Item/Currency adding (different behavior for table vs graph)
        if (Settings.ViewMode == DataToolViewMode.Table)
        {
            DrawTableActionButtons();
        }
        else
        {
            DrawGraphActionButtons();
        }
    }
    
    private void DrawTableActionButtons()
    {
        // Multi-select item dropdown
        if (_itemCombo != null)
        {
            var currentItemIds = Settings.Columns
                .Where(c => !c.IsCurrency)
                .Select(c => c.Id)
                .ToHashSet();
            
            var comboSelection = _itemCombo.GetMultiSelection();
            if (!currentItemIds.SetEquals(comboSelection))
            {
                _itemCombo.SetMultiSelection(currentItemIds);
            }
            
            _itemCombo.DrawMultiSelect(160);
            
            var newSelection = _itemCombo.GetMultiSelection();
            SyncItemColumns(newSelection);
            
            ImGui.SameLine();
        }
        
        // Multi-select currency dropdown
        if (_currencyCombo != null)
        {
            var currentCurrencyTypes = Settings.Columns
                .Where(c => c.IsCurrency)
                .Select(c => (TrackedDataType)c.Id)
                .ToHashSet();
            
            var comboSelection = _currencyCombo.GetMultiSelection();
            if (!currentCurrencyTypes.SetEquals(comboSelection))
            {
                _currencyCombo.SetMultiSelection(currentCurrencyTypes);
            }
            
            _currencyCombo.DrawMultiSelect(160);
            
            var newSelection = _currencyCombo.GetMultiSelection();
            SyncCurrencyColumns(newSelection);
        }
    }
    
    private void DrawGraphActionButtons()
    {
        // Multi-select item dropdown
        if (_itemCombo != null)
        {
            var currentItemIds = Settings.Columns
                .Where(c => !c.IsCurrency)
                .Select(c => c.Id)
                .ToHashSet();
            
            var comboSelection = _itemCombo.GetMultiSelection();
            if (!currentItemIds.SetEquals(comboSelection))
            {
                _itemCombo.SetMultiSelection(currentItemIds);
            }
            
            _itemCombo.DrawMultiSelect(160);
            
            var newSelection = _itemCombo.GetMultiSelection();
            SyncItemColumns(newSelection);
            
            ImGui.SameLine();
        }
        
        // Multi-select currency dropdown
        if (_currencyCombo != null)
        {
            var currentCurrencyTypes = Settings.Columns
                .Where(c => c.IsCurrency)
                .Select(c => (TrackedDataType)c.Id)
                .ToHashSet();
            
            var comboSelection = _currencyCombo.GetMultiSelection();
            if (!currentCurrencyTypes.SetEquals(comboSelection))
            {
                _currencyCombo.SetMultiSelection(currentCurrencyTypes);
            }
            
            _currencyCombo.DrawMultiSelect(160);
            
            var newSelection = _currencyCombo.GetMultiSelection();
            SyncCurrencyColumns(newSelection);
            
            ImGui.SameLine();
        }
        
        ImGui.TextDisabled($"({Settings.Columns.Count} series)");
    }
    
    private void SyncItemColumns(IReadOnlySet<uint> selectedItemIds)
    {
        var changed = false;
        
        foreach (var itemId in selectedItemIds)
        {
            if (!Settings.Columns.Any(c => !c.IsCurrency && c.Id == itemId))
            {
                Settings.Columns.Add(new ItemColumnConfig { Id = itemId, IsCurrency = false });
                changed = true;
            }
        }
        
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
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
    }
    
    private void SyncCurrencyColumns(IReadOnlySet<TrackedDataType> selectedTypes)
    {
        var changed = false;
        
        foreach (var type in selectedTypes)
        {
            var typeId = (uint)type;
            if (!Settings.Columns.Any(c => c.IsCurrency && c.Id == typeId))
            {
                Settings.Columns.Add(new ItemColumnConfig { Id = typeId, IsCurrency = true });
                changed = true;
            }
        }
        
        var toRemove = Settings.Columns
            .Where(c => c.IsCurrency && !selectedTypes.Contains((TrackedDataType)c.Id))
            .ToList();
        
        foreach (var col in toRemove)
        {
            Settings.Columns.Remove(col);
            changed = true;
        }
        
        if (changed)
        {
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
    }
    
    private void AddColumn(uint id, bool isCurrency)
    {
        if (ColumnManagementWidget.AddColumn(Settings.Columns, id, isCurrency))
        {
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
    }
    
    #region Table View
    
    private void DrawTableView()
    {
        using (ProfilerService.BeginStaticChildScope("TableView"))
        {
            // Auto-refresh every 0.5s
            var shouldAutoRefresh = (DateTime.UtcNow - _lastTableRefresh).TotalSeconds > 0.5;
            
            if (_pendingTableRefresh || shouldAutoRefresh)
            {
                using (ProfilerService.BeginStaticChildScope("RefreshTableData"))
                {
                    RefreshTableData();
                }
            }
            
            using (ProfilerService.BeginStaticChildScope("DrawTable"))
            {
                _tableWidget.Draw(_cachedTableData, Settings);
            }
        }
    }
    
    private void RefreshTableData()
    {
        try
        {
            var settings = Settings;
            var allColumns = settings.Columns;
            
            // Apply special grouping filter to get visible columns
            List<ItemColumnConfig> columns;
            using (ProfilerService.BeginStaticChildScope("ApplyGroupingFilter"))
            {
                columns = SpecialGroupingHelper.ApplySpecialGroupingFilter(allColumns, settings.SpecialGrouping).ToList();
            }
            
            if (columns.Count == 0)
            {
                _cachedTableData = new PreparedItemTableData
                {
                    Rows = Array.Empty<ItemTableCharacterRow>(),
                    Columns = columns
                };
                _lastTableRefresh = DateTime.UtcNow;
                _pendingTableRefresh = false;
                return;
            }
            
            // Get all character names with disambiguation
            IReadOnlyDictionary<ulong, string?> characterNames;
            IReadOnlyDictionary<ulong, string> disambiguatedNames;
            using (ProfilerService.BeginStaticChildScope("GetCharacterNames"))
            {
                characterNames = DbService.GetAllCharacterNamesDict();
                disambiguatedNames = CacheService.GetDisambiguatedNames(characterNames.Keys);
            }
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
                var charWorldName = characterWorlds.TryGetValue(charId, out var w) ? w : string.Empty;
                var dcName = !string.IsNullOrEmpty(charWorldName) ? worldData?.GetDataCenterForWorld(charWorldName)?.Name ?? string.Empty : string.Empty;
                var regionName = !string.IsNullOrEmpty(charWorldName) ? worldData?.GetRegionForWorld(charWorldName) ?? string.Empty : string.Empty;
                
                rows[charId] = new ItemTableCharacterRow
                {
                    CharacterId = charId,
                    Name = displayName,
                    WorldName = charWorldName,
                    DataCenterName = dcName,
                    RegionName = regionName,
                    ItemCounts = new Dictionary<uint, long>()
                };
            }
            
            // Populate data for each column
            using (ProfilerService.BeginStaticChildScope("PopulateColumns"))
            {
                foreach (var column in columns)
                {
                    if (column.IsCurrency)
                    {
                        PopulateCurrencyData(column, rows);
                    }
                    else
                    {
                        PopulateItemData(column, rows, settings.IncludeRetainers, settings.ShowRetainerBreakdown);
                    }
                }
            }
            
            // Apply gil merging if enabled
            if (settings.SpecialGrouping.AllGilEnabled && settings.SpecialGrouping.MergeGilCurrencies)
            {
                ApplyGilMerging(rows);
            }
            
            // Sort rows
            List<ItemTableCharacterRow> sortedRows;
            using (ProfilerService.BeginStaticChildScope("SortRows"))
            {
                sortedRows = CharacterSortHelper.SortByCharacter(
                    rows.Values,
                    _configService,
                    _autoRetainerService,
                    r => r.CharacterId,
                    r => r.Name).ToList();
            }
            
            _cachedTableData = new PreparedItemTableData
            {
                Rows = sortedRows,
                Columns = columns
            };
            
            _lastTableRefresh = DateTime.UtcNow;
            _pendingTableRefresh = false;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTool] RefreshTableData error: {ex.Message}");
        }
    }
    
    private void PopulateCurrencyData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        using (ProfilerService.BeginStaticChildScope("PopulateCurrency"))
        {
            try
            {
                var dataType = (TrackedDataType)column.Id;
                var variableName = dataType.ToString();
                
                using (ProfilerService.BeginStaticChildScope("DbGetPoints"))
                {
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
            }
            catch (Exception ex)
            {
                LogService.Debug($"[DataTool] PopulateCurrencyData error: {ex.Message}");
            }
        }
    }
    
    private void PopulateItemData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows, bool includeRetainers)
    {
        PopulateItemData(column, rows, includeRetainers, false);
    }
    
    private void PopulateItemData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows, bool includeRetainers, bool showRetainerBreakdown)
    {
        using (ProfilerService.BeginStaticChildScope("PopulateItem"))
        {
            try
            {
                if (_inventoryCacheService == null) return;
                
                var allInventories = _inventoryCacheService.GetAllInventories();
                
                foreach (var cache in allInventories)
                {
                    if (!rows.TryGetValue(cache.CharacterId, out var row))
                        continue;
                    
                    var count = cache.Items
                        .Where(i => i.ItemId == column.Id)
                        .Sum(i => (long)i.Quantity);
                    
                    // Initialize ItemCounts if needed
                    if (!row.ItemCounts.ContainsKey(column.Id))
                        row.ItemCounts[column.Id] = 0;
                    
                    if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Player)
                    {
                        // Always add player inventory to total
                        row.ItemCounts[column.Id] += count;
                        
                        // If showing breakdown, also track player-only counts
                        if (showRetainerBreakdown)
                        {
                            row.PlayerItemCounts ??= new Dictionary<uint, long>();
                            if (!row.PlayerItemCounts.ContainsKey(column.Id))
                                row.PlayerItemCounts[column.Id] = 0;
                            row.PlayerItemCounts[column.Id] += count;
                        }
                    }
                    else if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Retainer)
                    {
                        // Add retainer inventory to total if includeRetainers is enabled
                        if (includeRetainers)
                        {
                            row.ItemCounts[column.Id] += count;
                        }
                        
                        // If showing breakdown, track per-retainer counts
                        if (showRetainerBreakdown && count > 0)
                        {
                            var retainerKey = (cache.RetainerId, cache.Name ?? $"Retainer {cache.RetainerId}");
                            row.RetainerBreakdown ??= new Dictionary<(ulong, string), Dictionary<uint, long>>();
                            
                            if (!row.RetainerBreakdown.TryGetValue(retainerKey, out var retainerCounts))
                            {
                                retainerCounts = new Dictionary<uint, long>();
                                row.RetainerBreakdown[retainerKey] = retainerCounts;
                            }
                            
                            if (!retainerCounts.ContainsKey(column.Id))
                                retainerCounts[column.Id] = 0;
                            retainerCounts[column.Id] += count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[DataTool] PopulateItemData error: {ex.Message}");
            }
        }
    }
    
    private void ApplyGilMerging(Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        var gilId = (uint)TrackedDataType.Gil;
        var fcGilId = (uint)TrackedDataType.FreeCompanyGil;
        var retainerGilId = (uint)TrackedDataType.RetainerGil;
        
        foreach (var row in rows.Values)
        {
            long totalGil = 0;
            
            if (row.ItemCounts.TryGetValue(gilId, out var gil))
                totalGil += gil;
            if (row.ItemCounts.TryGetValue(fcGilId, out var fcGil))
                totalGil += fcGil;
            if (row.ItemCounts.TryGetValue(retainerGilId, out var retainerGil))
                totalGil += retainerGil;
            
            row.ItemCounts[gilId] = totalGil;
        }
    }
    
    #endregion
    
    #region Graph View
    
    private void DrawGraphView()
    {
        using (ProfilerService.BeginStaticChildScope("GraphView"))
        {
            _graphWidget.SyncFromBoundSettings();
            
            if (NeedsGraphCacheRefresh())
            {
                using (ProfilerService.BeginStaticChildScope("RefreshGraphData"))
                {
                    RefreshGraphData();
                }
            }
            
            if (_cachedSeriesData != null && _cachedSeriesData.Count > 0)
            {
                using (ProfilerService.BeginStaticChildScope("RenderGraph"))
                {
                    _graphWidget.RenderMultipleSeries(_cachedSeriesData);
                }
            }
            else
            {
                if (Settings.Columns.Count == 0)
                {
                    ImGui.TextDisabled("No items or currencies configured. Add some to start tracking.");
                }
                else
                {
                    ImGui.TextDisabled("No historical data available.");
                }
            }
        }
    }
    
    private bool _cachedShowRetainerBreakdownInGraph;
    
    private bool NeedsGraphCacheRefresh()
    {
        if (_graphCacheIsDirty) return true;
        
        var settings = Settings;
        if (_cachedSeriesCount != settings.Columns.Count) return true;
        if (_cachedTimeRangeValue != settings.TimeRangeValue) return true;
        if (_cachedTimeRangeUnit != settings.TimeRangeUnit) return true;
        if (_cachedIncludeRetainers != settings.IncludeRetainers) return true;
        if (_cachedShowRetainerBreakdownInGraph != settings.ShowRetainerBreakdownInGraph) return true;
        if (_cachedGroupingMode != settings.GroupingMode) return true;
        if (_cachedNameFormat != _configService.Config.CharacterNameFormat) return true;
        
        return (DateTime.UtcNow - _lastGraphRefresh).TotalSeconds > 5.0;
    }
    
    private void RefreshGraphData()
    {
        var settings = Settings;
        
        _lastGraphRefresh = DateTime.UtcNow;
        _cachedSeriesCount = settings.Columns.Count;
        _cachedTimeRangeValue = settings.TimeRangeValue;
        _cachedTimeRangeUnit = settings.TimeRangeUnit;
        _cachedIncludeRetainers = settings.IncludeRetainers;
        _cachedShowRetainerBreakdownInGraph = settings.ShowRetainerBreakdownInGraph;
        _cachedNameFormat = _configService.Config.CharacterNameFormat;
        _cachedGroupingMode = settings.GroupingMode;
        _graphCacheIsDirty = false;
        
        // Build set of indices that are part of merged groups with ShowInGraph enabled
        var mergedIndicesWithGraph = new HashSet<int>();
        foreach (var group in settings.MergedColumnGroups.Where(g => g.ShowInGraph))
        {
            foreach (var idx in group.ColumnIndices)
            {
                mergedIndicesWithGraph.Add(idx);
            }
        }
        
        // Apply special grouping filter and visibility filter
        // Skip individual columns that are part of a merged group that has ShowInGraph enabled
        List<ItemColumnConfig> series;
        using (ProfilerService.BeginStaticChildScope("ApplyGroupingFilter"))
        {
            var filteredColumns = SpecialGroupingHelper.ApplySpecialGroupingFilter(settings.Columns, settings.SpecialGrouping);
            series = filteredColumns
                .Where((c, idx) => c.ShowInGraph && !mergedIndicesWithGraph.Contains(idx))
                .ToList();
        }
        
        var timeRange = GetTimeRange();
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : (DateTime?)null;
        
        // Get character filter
        HashSet<ulong>? allowedCharacters = null;
        if (settings.UseCharacterFilter && settings.SelectedCharacterIds.Count > 0)
        {
            allowedCharacters = settings.SelectedCharacterIds.ToHashSet();
        }
        
        var seriesList = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        
        // Calculate total item/currency count (excluding merged groups)
        // When there are multiple items, we show item names in legend; when single item, show character/grouping breakdown
        var totalItemCount = series.Count + settings.MergedColumnGroups.Count(g => g.ShowInGraph);
        var isSingleItem = totalItemCount == 1;
        
        using (ProfilerService.BeginStaticChildScope("LoadAllSeries"))
        {
            // Load individual (non-merged) series
            foreach (var seriesConfig in series)
            {
                var seriesData = LoadSeriesData(seriesConfig, settings, startTime, allowedCharacters, isSingleItem);
                if (seriesData != null)
                {
                    seriesList.AddRange(seriesData);
                }
            }
            
            // Load merged group series
            foreach (var group in settings.MergedColumnGroups.Where(g => g.ShowInGraph))
            {
                var mergedSeriesData = LoadMergedSeriesData(group, settings, startTime, allowedCharacters, isSingleItem);
                if (mergedSeriesData != null)
                {
                    seriesList.AddRange(mergedSeriesData);
                }
            }
        }
        
        _cachedSeriesData = seriesList.Count > 0 ? seriesList : null;
    }
    
    private TimeSpan? GetTimeRange()
    {
        var settings = Settings;
        return TimeRangeSelectorWidget.GetTimeSpan(settings.TimeRangeValue, settings.TimeRangeUnit);
    }
    
    /// <summary>
    /// Gets a display name for the provided character ID.
    /// Uses formatted name from cache service, respecting the name format setting.
    /// </summary>
    private string GetCharacterDisplayName(ulong characterId)
    {
        // Use cache service which handles display name, game name formatting, and fallbacks
        var formattedName = CacheService.GetFormattedCharacterName(characterId);
        if (!string.IsNullOrEmpty(formattedName))
            return formattedName;

        // Try runtime lookup for currently-loaded characters (formats it)
        var runtimeName = GameStateService.GetCharacterName(characterId);
        if (!string.IsNullOrEmpty(runtimeName))
            return TimeSeriesCacheService.FormatName(runtimeName, _configService.Config.CharacterNameFormat) ?? runtimeName;

        // Fallback to ID
        return $"Character {characterId}";
    }
    
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? LoadSeriesData(
        ItemColumnConfig seriesConfig, 
        DataToolSettings settings, 
        DateTime? startTime,
        HashSet<ulong>? allowedCharacters,
        bool isSingleItem = true)
    {
        try
        {
            var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
            string variableName;
            string? perRetainerVariablePrefix = null;
            
            if (seriesConfig.IsCurrency)
            {
                variableName = ((TrackedDataType)seriesConfig.Id).ToString();
            }
            else
            {
                variableName = $"Item_{seriesConfig.Id}";
                // If showing retainer breakdown in graph, we'll fetch per-retainer data
                if (settings.IncludeRetainers && settings.ShowRetainerBreakdownInGraph)
                {
                    // Per-retainer data uses pattern: ItemRetainerX_{retainerId}_{itemId}
                    // We need to search for all matching the item ID at the end
                    perRetainerVariablePrefix = $"ItemRetainerX_";
                }
            }
            
            IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> points;
            Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>? perRetainerPointsDict = null;
            using (ProfilerService.BeginStaticChildScope("DbGetPoints"))
            {
                var allPoints = DbService.GetAllPointsBatch(variableName, startTime);
            
                if (!allPoints.TryGetValue(variableName, out var pts) || pts.Count == 0)
                {
                    // Try to get from pending samples cache (for real-time display before DB flush)
                    if (_inventoryCacheService != null && variableName.StartsWith("Item_"))
                    {
                        var pendingPlayerSamples = _inventoryCacheService.GetPendingSamples("Item_", $"_{seriesConfig.Id}");
                        if (pendingPlayerSamples.TryGetValue(variableName, out var pendingPts) && pendingPts.Count > 0)
                        {
                            pts = pendingPts;
                        }
                    }
                    
                    if (pts == null || pts.Count == 0)
                        return null;
                }
                
                // Merge in pending (cached but not yet flushed) player samples for real-time display
                if (_inventoryCacheService != null && variableName.StartsWith("Item_"))
                {
                    var pendingPlayerSamples = _inventoryCacheService.GetPendingSamples("Item_", $"_{seriesConfig.Id}");
                    if (pendingPlayerSamples.TryGetValue(variableName, out var pendingPts) && pendingPts.Count > 0)
                    {
                        var mutablePoints = pts.ToList();
                        mutablePoints.AddRange(pendingPts);
                        pts = mutablePoints;
                    }
                }
                
                points = pts;
                
                // If IncludeRetainers is enabled but ShowRetainerBreakdownInGraph is disabled,
                // we need to add retainer totals to the main series (not show them separately)
                if (settings.IncludeRetainers && !settings.ShowRetainerBreakdownInGraph && !seriesConfig.IsCurrency)
                {
                    var retainerVariableName = $"ItemRetainer_{seriesConfig.Id}";
                    var retainerPoints = DbService.GetAllPointsBatch(retainerVariableName, startTime);
                    
                    if (retainerPoints.TryGetValue(retainerVariableName, out var retainerPts) && retainerPts.Count > 0)
                    {
                        // Merge in pending retainer samples for real-time display
                        if (_inventoryCacheService != null)
                        {
                            var pendingRetainerSamples = _inventoryCacheService.GetPendingSamples("ItemRetainer_", $"_{seriesConfig.Id}");
                            if (pendingRetainerSamples.TryGetValue(retainerVariableName, out var pendingPts) && pendingPts.Count > 0)
                            {
                                var mutableRetainerPoints = retainerPts.ToList();
                                mutableRetainerPoints.AddRange(pendingPts);
                                retainerPts = mutableRetainerPoints;
                            }
                        }
                        
                        // Merge player and retainer data using forward-fill logic.
                        // This ensures that at any timestamp, we combine the latest known player value
                        // with the latest known retainer value, even if they weren't sampled at the same time.
                        // This handles the case where player inventory value stays constant (no new samples)
                        // while retainer values change (new samples created).
                        
                        // Group points by character ID first
                        var playerByChar = points
                            .GroupBy(p => p.characterId)
                            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.timestamp).ToList());
                        
                        var retainerByChar = retainerPts
                            .GroupBy(p => p.characterId)
                            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.timestamp).ToList());
                        
                        // Get all unique character IDs
                        var allCharIds = playerByChar.Keys.Union(retainerByChar.Keys).ToList();
                        
                        var mergedPoints = new List<(ulong characterId, DateTime timestamp, long value)>();
                        
                        foreach (var charId in allCharIds)
                        {
                            var playerPts = playerByChar.GetValueOrDefault(charId) ?? new List<(ulong, DateTime, long)>();
                            var retPts = retainerByChar.GetValueOrDefault(charId) ?? new List<(ulong, DateTime, long)>();
                            
                            // Collect all unique timestamps (rounded to minute)
                            var allTimestamps = playerPts
                                .Select(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                                    p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                                .Union(retPts.Select(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                                    p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc)))
                                .OrderBy(t => t)
                                .Distinct()
                                .ToList();
                            
                            // Build lookup for player and retainer values by rounded timestamp
                            var playerLookup = playerPts
                                .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                                    p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                                .ToDictionary(g => g.Key, g => g.Sum(p => p.value));
                            
                            var retainerLookup = retPts
                                .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                                    p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                                .ToDictionary(g => g.Key, g => g.Sum(p => p.value));
                            
                            // Forward-fill: carry forward the last known value for each series
                            long lastPlayerValue = 0;
                            long lastRetainerValue = 0;
                            
                            foreach (var ts in allTimestamps)
                            {
                                // Update with new value if available, otherwise keep last known
                                if (playerLookup.TryGetValue(ts, out var pVal))
                                    lastPlayerValue = pVal;
                                if (retainerLookup.TryGetValue(ts, out var rVal))
                                    lastRetainerValue = rVal;
                                
                                mergedPoints.Add((charId, ts, lastPlayerValue + lastRetainerValue));
                            }
                        }
                        
                        points = mergedPoints.OrderBy(p => p.timestamp).ToList();
                    }
                }
                
                // Also fetch per-retainer data if breakdown is enabled
                if (perRetainerVariablePrefix != null)
                {
                    // Use optimized query with both prefix and suffix matching
                    var itemIdSuffix = $"_{seriesConfig.Id}";
                    perRetainerPointsDict = DbService.GetPointsBatchWithSuffix(perRetainerVariablePrefix, itemIdSuffix, startTime);
                    
                    // Merge in pending (cached but not yet flushed) retainer samples for real-time display
                    if (_inventoryCacheService != null)
                    {
                        var pendingSamples = _inventoryCacheService.GetPendingSamples(perRetainerVariablePrefix, itemIdSuffix);
                        foreach (var (varName, pendingPoints) in pendingSamples)
                        {
                            if (!perRetainerPointsDict.TryGetValue(varName, out var existingList))
                            {
                                existingList = new List<(ulong, DateTime, long)>();
                                perRetainerPointsDict[varName] = existingList;
                            }
                            existingList.AddRange(pendingPoints);
                        }
                    }
                    
                    // If no per-retainer data found, fall back to the old total retainer data
                    if (perRetainerPointsDict.Count == 0)
                    {
                        var fallbackVariableName = $"ItemRetainer_{seriesConfig.Id}";
                        var fallbackPoints = DbService.GetAllPointsBatch(fallbackVariableName, startTime);
                        if (fallbackPoints.TryGetValue(fallbackVariableName, out var fallbackPts) && fallbackPts.Count > 0)
                        {
                            // Use the old total data with a generic "Retainers" label
                            perRetainerPointsDict[fallbackVariableName] = fallbackPts;
                        }
                    }
                }
                
                // Apply character filter
                if (allowedCharacters != null)
                {
                    points = points.Where(p => allowedCharacters.Contains(p.characterId)).ToList();
                    if (perRetainerPointsDict != null)
                    {
                        perRetainerPointsDict = perRetainerPointsDict
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.Where(p => allowedCharacters.Contains(p.characterId)).ToList());
                    }
                }
                
                if (points.Count == 0)
                    return null;
            }
            
            var defaultName = GetSeriesDisplayName(seriesConfig);
            var color = GetEffectiveSeriesColor(seriesConfig, settings, result.Count);
            
            // Use GroupingMode for graph series grouping
            var groupingMode = settings.GroupingMode;
            
            if (groupingMode == TableGroupingMode.Character)
            {
                // Separate series per character
                var byCharacter = points.GroupBy(p => p.characterId);
                var charIndex = 0;
                
                foreach (var charGroup in byCharacter)
                {
                    var charName = GetCharacterDisplayName(charGroup.Key);
                    // When there's only one item/currency, show just the grouping name in the legend
                    var seriesName = isSingleItem ? charName : $"{defaultName} ({charName})";
                    
                    // Determine color: prefer character color in PreferredCharacterColors mode,
                    // otherwise use item color or fallback
                    Vector4 seriesColor;
                    if (settings.TextColorMode == TableTextColorMode.PreferredCharacterColors)
                    {
                        seriesColor = GetPreferredCharacterColor(charGroup.Key) ?? GetDefaultSeriesColor(charIndex);
                    }
                    else
                    {
                        seriesColor = color;
                    }
                    
                    var samples = charGroup
                        .OrderBy(p => p.timestamp)
                        .Select(p => (ts: p.timestamp, value: (float)p.value))
                        .ToList();
                    
                    if (samples.Count > 0)
                    {
                        result.Add((seriesName, samples, seriesColor));
                    }
                    charIndex++;
                }
            }
            else if (groupingMode == TableGroupingMode.All)
            {
                // Aggregate all characters by timestamp (round to minute)
                var aggregated = points
                    .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day, 
                        p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                    .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                    .OrderBy(p => p.ts)
                    .ToList();
                
                if (aggregated.Count > 0)
                {
                    result.Add((seriesConfig.CustomName ?? defaultName, aggregated, color));
                }
            }
            else
            {
                // Group by World, DataCenter, or Region
                var groupedSeries = GroupPointsByLocation(points, groupingMode, defaultName, seriesConfig, settings, isSingleItem);
                result.AddRange(groupedSeries);
            }
            
            // Add per-retainer series if breakdown is enabled and we have retainer data
            if (perRetainerPointsDict != null && perRetainerPointsDict.Count > 0)
            {
                var retainerSeriesResult = BuildPerRetainerSeries(perRetainerPointsDict, seriesConfig.Id, defaultName, settings, groupingMode, seriesConfig);
                result.AddRange(retainerSeriesResult);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTool] LoadSeriesData error: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Loads and combines series data for a merged column group.
    /// When isSingleItem is true, respects the grouping mode to create per-character/world/etc. series.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? LoadMergedSeriesData(
        MergedColumnGroup group,
        DataToolSettings settings,
        DateTime? startTime,
        HashSet<ulong>? allowedCharacters,
        bool isSingleItem = true)
    {
        try
        {
            // Get the member columns
            var memberColumns = group.ColumnIndices
                .Where(idx => idx >= 0 && idx < settings.Columns.Count)
                .Select(idx => settings.Columns[idx])
                .ToList();
            
            if (memberColumns.Count == 0)
                return null;
            
            // Collect all points from all member columns (now with character ID)
            var allPoints = new List<(ulong characterId, DateTime ts, long value)>();
            
            foreach (var column in memberColumns)
            {
                string variableName;
                if (column.IsCurrency)
                {
                    variableName = ((TrackedDataType)column.Id).ToString();
                }
                else
                {
                    variableName = $"Item_{column.Id}";
                }
                
                var pointsDict = DbService.GetAllPointsBatch(variableName, startTime);
                if (pointsDict.TryGetValue(variableName, out var pts) && pts.Count > 0)
                {
                    // Filter by allowed characters if specified
                    var filteredPoints = allowedCharacters != null
                        ? pts.Where(p => allowedCharacters.Contains(p.characterId))
                        : pts;
                    
                    // Add points with character ID
                    foreach (var p in filteredPoints)
                    {
                        allPoints.Add((p.characterId, p.timestamp, p.value));
                    }
                }
                
                // Also include retainer data if IncludeRetainers is enabled
                if (settings.IncludeRetainers && !column.IsCurrency)
                {
                    var retainerVariableName = $"ItemRetainer_{column.Id}";
                    var retainerPointsDict = DbService.GetAllPointsBatch(retainerVariableName, startTime);
                    if (retainerPointsDict.TryGetValue(retainerVariableName, out var retainerPts) && retainerPts.Count > 0)
                    {
                        var filteredRetainerPoints = allowedCharacters != null
                            ? retainerPts.Where(p => allowedCharacters.Contains(p.characterId))
                            : retainerPts;
                        
                        foreach (var p in filteredRetainerPoints)
                        {
                            allPoints.Add((p.characterId, p.timestamp, p.value));
                        }
                    }
                }
            }
            
            if (allPoints.Count == 0)
                return null;
            
            // Use the merged group's color if set, otherwise use first member's color
            var baseColor = group.Color;
            if (!baseColor.HasValue && memberColumns.Count > 0 && memberColumns[0].Color.HasValue)
            {
                baseColor = memberColumns[0].Color;
            }
            
            var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
            
            // Use GroupingMode for series grouping
            var groupingMode = settings.GroupingMode;
            
            if (groupingMode == TableGroupingMode.Character)
            {
                // Separate series per character
                var byCharacter = allPoints.GroupBy(p => p.characterId);
                var charIndex = 0;
                
                foreach (var charGroup in byCharacter)
                {
                    var charName = GetCharacterDisplayName(charGroup.Key);
                    // When there's only one merged group, show just the grouping name in the legend
                    var seriesName = isSingleItem ? charName : $"{group.Name} ({charName})";
                    
                    // Determine color
                    Vector4 seriesColor;
                    if (settings.TextColorMode == TableTextColorMode.PreferredCharacterColors)
                    {
                        seriesColor = GetPreferredCharacterColor(charGroup.Key) ?? GetDefaultSeriesColor(charIndex);
                    }
                    else
                    {
                        seriesColor = baseColor ?? GetDefaultSeriesColor(charIndex);
                    }
                    
                    // Group by timestamp within this character and sum values
                    var samples = charGroup
                        .GroupBy(p => new DateTime(p.ts.Year, p.ts.Month, p.ts.Day, p.ts.Hour, p.ts.Minute, 0, DateTimeKind.Utc))
                        .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                        .OrderBy(p => p.ts)
                        .ToList();
                    
                    if (samples.Count > 0)
                    {
                        result.Add((seriesName, samples, seriesColor));
                    }
                    charIndex++;
                }
            }
            else if (groupingMode == TableGroupingMode.All)
            {
                // Aggregate all into a single series
                var groupedPoints = allPoints
                    .GroupBy(p => new DateTime(p.ts.Year, p.ts.Month, p.ts.Day, p.ts.Hour, p.ts.Minute, 0, DateTimeKind.Utc))
                    .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                    .OrderBy(p => p.ts)
                    .ToList();
                
                if (groupedPoints.Count > 0)
                {
                    result.Add((group.Name, groupedPoints, baseColor));
                }
            }
            else
            {
                // Group by World, DataCenter, or Region
                var worldData = _priceTrackingService?.WorldData;
                var characterWorlds = GetCharacterWorldsMap();
                
                // Build character -> group name mapping
                var characterGroups = new Dictionary<ulong, string>();
                foreach (var (charId, worldName) in characterWorlds)
                {
                    string groupName = groupingMode switch
                    {
                        TableGroupingMode.World => worldName,
                        TableGroupingMode.DataCenter => worldData?.GetDataCenterForWorld(worldName)?.Name ?? "Unknown DC",
                        TableGroupingMode.Region => worldData?.GetRegionForWorld(worldName) ?? "Unknown Region",
                        _ => "Unknown"
                    };
                    characterGroups[charId] = groupName;
                }
                
                // Group points by their location group
                var byGroup = allPoints
                    .GroupBy(p => characterGroups.TryGetValue(p.characterId, out var g) ? g : "Unknown")
                    .OrderBy(g => g.Key);
                
                var groupIndex = 0;
                foreach (var locationGroup in byGroup)
                {
                    var locationName = locationGroup.Key;
                    // When there's only one merged group, show just the grouping name in the legend
                    var seriesName = isSingleItem ? locationName : $"{group.Name} ({locationName})";
                    
                    // Aggregate points by timestamp within the group
                    var aggregated = locationGroup
                        .GroupBy(p => new DateTime(p.ts.Year, p.ts.Month, p.ts.Day, p.ts.Hour, p.ts.Minute, 0, DateTimeKind.Utc))
                        .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                        .OrderBy(p => p.ts)
                        .ToList();
                    
                    if (aggregated.Count > 0)
                    {
                        var seriesColor = baseColor ?? GetDefaultSeriesColor(groupIndex);
                        result.Add((seriesName, aggregated, seriesColor));
                    }
                    groupIndex++;
                }
            }
            
            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTool] LoadMergedSeriesData error: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Groups time series points by World, DataCenter, or Region.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> GroupPointsByLocation(
        IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> points,
        TableGroupingMode groupingMode,
        string defaultName,
        ItemColumnConfig seriesConfig,
        DataToolSettings settings,
        bool isSingleItem = true)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        
        // Get world data and character info from AutoRetainer
        var worldData = _priceTrackingService?.WorldData;
        var characterWorlds = GetCharacterWorldsMap();
        
        // Build character -> group name mapping
        var characterGroups = new Dictionary<ulong, string>();
        foreach (var (charId, worldName) in characterWorlds)
        {
            string groupName = groupingMode switch
            {
                TableGroupingMode.World => worldName,
                TableGroupingMode.DataCenter => worldData?.GetDataCenterForWorld(worldName)?.Name ?? "Unknown DC",
                TableGroupingMode.Region => worldData?.GetRegionForWorld(worldName) ?? "Unknown Region",
                _ => "Unknown"
            };
            characterGroups[charId] = groupName;
        }
        
        // Group points by their location group
        var byGroup = points
            .GroupBy(p => characterGroups.TryGetValue(p.characterId, out var g) ? g : "Unknown")
            .OrderBy(g => g.Key);
        
        var groupIndex = 0;
        var color = GetEffectiveSeriesColor(seriesConfig, settings, 0);
        
        foreach (var group in byGroup)
        {
            var groupName = group.Key;
            // When there's only one item/currency, show just the grouping name in the legend
            var seriesName = isSingleItem ? groupName : $"{defaultName} ({groupName})";
            
            // Aggregate points by timestamp within the group
            var aggregated = group
                .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                    p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                .OrderBy(p => p.ts)
                .ToList();
            
            if (aggregated.Count > 0)
            {
                // Use different colors per group if not using item colors
                var seriesColor = settings.TextColorMode == TableTextColorMode.PreferredItemColors 
                    ? color 
                    : GetDefaultSeriesColor(groupIndex);
                result.Add((seriesName, aggregated, seriesColor));
            }
            groupIndex++;
        }
        
        return result;
    }
    
    /// <summary>
    /// Builds separate series for each individual retainer's inventory data.
    /// Each retainer gets its own series with distinct name and color.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> BuildPerRetainerSeries(
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> perRetainerPointsDict,
        uint itemId,
        string defaultName,
        DataToolSettings settings,
        TableGroupingMode groupingMode,
        ItemColumnConfig seriesConfig)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        
        // Build a lookup of retainer ID -> retainer name from inventory cache
        var retainerNames = GetRetainerNamesMap();
        
        // Use a color palette for retainers
        var baseColor = GetEffectiveSeriesColor(seriesConfig, settings, 0);
        var retainerIndex = 0;
        
        foreach (var (variableName, points) in perRetainerPointsDict)
        {
            if (points.Count == 0) continue;
            
            string retainerName;
            
            // Check if this is the old format (ItemRetainer_{itemId}) or new format (ItemRetainerX_{retainerId}_{itemId})
            if (variableName.StartsWith("ItemRetainerX_"))
            {
                // Parse retainer ID from variable name: ItemRetainerX_{retainerId}_{itemId}
                // Format: ItemRetainerX_12345678_1234
                var parts = variableName.Split('_');
                if (parts.Length < 3) continue;
                
                if (!ulong.TryParse(parts[1], out var retainerId)) continue;
                
                // Get retainer name
                retainerName = retainerNames.TryGetValue(retainerId, out var name) ? name : $"Retainer {retainerId}";
            }
            else
            {
                // Old format: ItemRetainer_{itemId} - show as combined "Retainers"
                retainerName = "Retainers";
            }
            
            // Generate a unique color for this retainer
            var retainerColor = GetRetainerSeriesColor(baseColor, retainerIndex);
            
            if (groupingMode == TableGroupingMode.Character)
            {
                // Separate series per character's retainer
                var byCharacter = points.GroupBy(p => p.characterId);
                
                foreach (var charGroup in byCharacter)
                {
                    var charName = GetCharacterDisplayName(charGroup.Key);
                    var seriesName = $"{defaultName} ({charName} - {retainerName})";
                    
                    Vector4 seriesColor;
                    if (settings.TextColorMode == TableTextColorMode.PreferredCharacterColors)
                    {
                        var charColor = GetPreferredCharacterColor(charGroup.Key) ?? GetDefaultSeriesColor(retainerIndex);
                        seriesColor = GetRetainerSeriesColor(charColor, retainerIndex);
                    }
                    else
                    {
                        seriesColor = retainerColor;
                    }
                    
                    var samples = charGroup
                        .OrderBy(p => p.timestamp)
                        .Select(p => (ts: p.timestamp, value: (float)p.value))
                        .ToList();
                    
                    if (samples.Count > 0)
                    {
                        result.Add((seriesName, samples, seriesColor));
                    }
                }
            }
            else if (groupingMode == TableGroupingMode.All)
            {
                // Aggregate this retainer's data across all characters by timestamp
                var aggregated = points
                    .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                        p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                    .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                    .OrderBy(p => p.ts)
                    .ToList();
                
                if (aggregated.Count > 0)
                {
                    var seriesName = $"{seriesConfig.CustomName ?? defaultName} ({retainerName})";
                    result.Add((seriesName, aggregated, retainerColor));
                }
            }
            else
            {
                // Group by World, DataCenter, or Region
                var worldData = _priceTrackingService?.WorldData;
                var characterWorlds = GetCharacterWorldsMap();
                
                var characterGroups = new Dictionary<ulong, string>();
                foreach (var (charId, worldName) in characterWorlds)
                {
                    string groupName = groupingMode switch
                    {
                        TableGroupingMode.World => worldName,
                        TableGroupingMode.DataCenter => worldData?.GetDataCenterForWorld(worldName)?.Name ?? "Unknown DC",
                        TableGroupingMode.Region => worldData?.GetRegionForWorld(worldName) ?? "Unknown Region",
                        _ => "Unknown"
                    };
                    characterGroups[charId] = groupName;
                }
                
                var byGroup = points
                    .GroupBy(p => characterGroups.TryGetValue(p.characterId, out var g) ? g : "Unknown")
                    .OrderBy(g => g.Key);
                
                foreach (var group in byGroup)
                {
                    var groupName = group.Key;
                    var seriesName = $"{defaultName} ({groupName} - {retainerName})";
                    
                    var aggregated = group
                        .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                            p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                        .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                        .OrderBy(p => p.ts)
                        .ToList();
                    
                    if (aggregated.Count > 0)
                    {
                        var seriesColor = settings.TextColorMode == TableTextColorMode.PreferredItemColors
                            ? retainerColor
                            : GetRetainerSeriesColor(GetDefaultSeriesColor(0), retainerIndex);
                        result.Add((seriesName, aggregated, seriesColor));
                    }
                }
            }
            
            retainerIndex++;
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets a mapping of retainer ID to retainer name from inventory cache.
    /// Uses a cached result that is refreshed periodically.
    /// </summary>
    private Dictionary<ulong, string> GetRetainerNamesMap()
    {
        // Return cached result if still valid
        if (_cachedRetainerNames != null && 
            (DateTime.UtcNow - _lastRetainerNamesCacheRefresh) < RetainerNamesCacheExpiry)
        {
            return _cachedRetainerNames;
        }
        
        var retainerNames = new Dictionary<ulong, string>();
        try
        {
            // Get all inventory caches to find retainer names
            var allCaches = DbService.GetAllInventoryCachesAllCharacters();
            foreach (var cache in allCaches)
            {
                if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Retainer && 
                    cache.RetainerId != 0 && 
                    !string.IsNullOrEmpty(cache.Name))
                {
                    retainerNames[cache.RetainerId] = cache.Name;
                }
            }
            
            // Cache the result
            _cachedRetainerNames = retainerNames;
            _lastRetainerNamesCacheRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTool] GetRetainerNamesMap error: {ex.Message}");
        }
        return retainerNames;
    }
    
    /// <summary>
    /// Generates a distinct color for a retainer series based on a base color.
    /// Uses hue rotation to create visually distinct colors.
    /// </summary>
    private static Vector4 GetRetainerSeriesColor(Vector4 baseColor, int retainerIndex)
    {
        // Create color variations by rotating hue and adjusting saturation
        var hueShift = (retainerIndex * 0.15f) % 1.0f;
        
        // Simple hue rotation approximation
        var r = baseColor.X;
        var g = baseColor.Y;
        var b = baseColor.Z;
        
        // Rotate colors based on index
        var rotation = retainerIndex % 6;
        return rotation switch
        {
            0 => new Vector4(r, g * 0.7f + 0.3f, b * 0.5f, baseColor.W),
            1 => new Vector4(r * 0.7f, g, b * 0.7f + 0.3f, baseColor.W),
            2 => new Vector4(r * 0.5f, g * 0.7f + 0.3f, b, baseColor.W),
            3 => new Vector4(r * 0.7f + 0.3f, g * 0.5f, b * 0.7f, baseColor.W),
            4 => new Vector4(r * 0.6f, g * 0.8f, b * 0.6f + 0.4f, baseColor.W),
            5 => new Vector4(r * 0.8f + 0.2f, g * 0.6f + 0.2f, b * 0.5f, baseColor.W),
            _ => baseColor
        };
    }
    
    /// <summary>
    /// Gets a mapping of character ID to world name from AutoRetainer.
    /// </summary>
    private Dictionary<ulong, string> GetCharacterWorldsMap()
    {
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
        return characterWorlds;
    }
    
    private string GetSeriesDisplayName(ItemColumnConfig config)
    {
        if (!string.IsNullOrEmpty(config.CustomName))
            return config.CustomName;
        
        if (config.IsCurrency)
        {
            var dataType = (TrackedDataType)config.Id;
            var def = _trackedDataRegistry?.GetDefinition(dataType);
            return def?.DisplayName ?? dataType.ToString();
        }
        
        return _itemDataService?.GetItemName(config.Id) ?? $"Item #{config.Id}";
    }
    
    /// <summary>
    /// Gets the effective color for a series based on TextColorMode setting.
    /// </summary>
    private Vector4 GetEffectiveSeriesColor(ItemColumnConfig config, DataToolSettings settings, int seriesIndex)
    {
        // First check if the column has a custom color set
        if (config.Color.HasValue)
            return config.Color.Value;
        
        // Check TextColorMode for preferred colors
        if (settings.TextColorMode == TableTextColorMode.PreferredItemColors)
        {
            var preferredColor = GetPreferredItemColor(config);
            if (preferredColor.HasValue)
                return preferredColor.Value;
        }
        
        // Fallback to default color rotation
        return GetDefaultSeriesColor(seriesIndex);
    }
    
    /// <summary>
    /// Gets the preferred color for an item/currency from configuration.
    /// </summary>
    private Vector4? GetPreferredItemColor(ItemColumnConfig config)
    {
        var configData = _configService.Config;
        
        if (config.IsCurrency)
        {
            // Check ItemColors (TrackedDataType -> uint)
            var dataType = (TrackedDataType)config.Id;
            if (configData.ItemColors.TryGetValue(dataType, out var colorUint))
                return ColorUtils.UintToVector4(colorUint);
        }
        else
        {
            // Check GameItemColors (item ID -> uint)
            if (configData.GameItemColors.TryGetValue(config.Id, out var colorUint))
                return ColorUtils.UintToVector4(colorUint);
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the preferred color for a character from the cache service.
    /// </summary>
    private Vector4? GetPreferredCharacterColor(ulong characterId)
    {
        var charColor = CacheService.GetCharacterTimeSeriesColor(characterId);
        if (charColor.HasValue)
            return ColorUtils.UintToVector4(charColor.Value);
        return null;
    }
    
    private static Vector4 GetDefaultSeriesColor(int index)
    {
        var colors = new[]
        {
            new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
            new Vector4(0.2f, 0.6f, 1.0f, 1.0f),
            new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
            new Vector4(0.8f, 0.2f, 0.8f, 1.0f),
            new Vector4(1.0f, 1.0f, 0.2f, 1.0f),
            new Vector4(0.2f, 1.0f, 1.0f, 1.0f),
        };
        return colors[index % colors.Length];
    }
    
    #endregion
    
    #region Tool Settings
    
    protected override bool HasToolSettings => true;
    
    protected override void DrawToolSettings()
    {
        var settings = Settings;
        
        // View Mode Section
        ImGui.TextUnformatted("View Mode");
        ImGui.Separator();
        
        var viewMode = (int)settings.ViewMode;
        if (ImGui.Combo("View", ref viewMode, "Table\0Graph\0"))
        {
            settings.ViewMode = (DataToolViewMode)viewMode;
            UpdateTitle();
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Display Options (shared between both modes)
        ImGui.TextUnformatted("Display Options");
        ImGui.Separator();
        
        // Grouping mode
        var groupingMode = (int)settings.GroupingMode;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Group By", ref groupingMode, "Character\0World\0Data Center\0Region\0All\0"))
        {
            settings.GroupingMode = (TableGroupingMode)groupingMode;
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        
        // Include retainers
        var includeRetainers = settings.IncludeRetainers;
        if (ImGui.Checkbox("Include Retainers", ref includeRetainers))
        {
            settings.IncludeRetainers = includeRetainers;
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        
        // Show retainer breakdown options (available when IncludeRetainers is enabled)
        if (settings.IncludeRetainers)
        {
            ImGui.Indent(16f);
            
            // Table mode retainer breakdown
            var showRetainerBreakdownTable = settings.ShowRetainerBreakdown;
            if (ImGui.Checkbox("Retainer Breakdown (Table)", ref showRetainerBreakdownTable))
            {
                settings.ShowRetainerBreakdown = showRetainerBreakdownTable;
                _pendingTableRefresh = true;
                NotifyToolSettingsChanged();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Show expandable rows for each character to view per-retainer item counts.");
            }
            
            // Graph mode retainer breakdown
            var showRetainerBreakdownGraph = settings.ShowRetainerBreakdownInGraph;
            if (ImGui.Checkbox("Retainer Breakdown (Graph)", ref showRetainerBreakdownGraph))
            {
                settings.ShowRetainerBreakdownInGraph = showRetainerBreakdownGraph;
                _graphCacheIsDirty = true;
                NotifyToolSettingsChanged();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Show separate series for each retainer's inventory in graph view.\n\n" +
                      "Note: Historical tracking must be enabled for each item,\n" +
                      "and you must open each retainer's inventory at least once to collect data.");
            }
            
            // Show warning if any items don't have historical tracking (only relevant for graph mode)
            if (showRetainerBreakdownGraph)
            {
                var itemsWithoutTracking = settings.Columns.Count(c => !c.IsCurrency && !_configService.Config.ItemsWithHistoricalTracking.Contains(c.Id));
                if (itemsWithoutTracking > 0)
                {
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"⚠ {itemsWithoutTracking} item(s) without historical tracking");
                }
            }
            
            ImGui.Unindent(16f);
        }
        
        // Show action buttons
        var showActionButtons = settings.ShowActionButtons;
        if (ImGui.Checkbox("Show Action Buttons", ref showActionButtons))
        {
            settings.ShowActionButtons = showActionButtons;
            NotifyToolSettingsChanged();
        }
        
        // Compact numbers
        var useCompactNumbers = settings.UseCompactNumbers;
        if (ImGui.Checkbox("Compact Numbers", ref useCompactNumbers))
        {
            settings.UseCompactNumbers = useCompactNumbers;
            NotifyToolSettingsChanged();
        }
        
        // Hide zero rows
        var hideZeroRows = settings.HideZeroRows;
        if (ImGui.Checkbox("Hide Zero Rows", ref hideZeroRows))
        {
            settings.HideZeroRows = hideZeroRows;
            _pendingTableRefresh = true;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hide rows where all column values are zero.");
        }
        
        // Color Mode (applies to both table and graph)
        var textColorMode = (int)settings.TextColorMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Color Mode", ref textColorMode, "Don't use\0Use preferred item colors\0Use preferred character colors\0"))
        {
            settings.TextColorMode = (TableTextColorMode)textColorMode;
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Column/Series Management with integrated merge functionality
        ColumnManagementWidget.Draw(
            settings.Columns,
            settings.MergedColumnGroups,
            column => GetSeriesDisplayName(column),
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _pendingTableRefresh = true; _graphCacheIsDirty = true; },
            sectionTitle: "Item / Currency Management",
            emptyMessage: "No items or currencies configured.",
            itemLabel: "Item",
            currencyLabel: "Currency",
            widgetId: $"datatool_{GetHashCode()}",
            isItemHistoricalTrackingEnabled: (itemId) => _configService.Config.ItemsWithHistoricalTracking.Contains(itemId),
            onItemHistoricalTrackingToggled: (itemId, enabled) =>
            {
                if (enabled)
                {
                    _configService.Config.ItemsWithHistoricalTracking.Add(itemId);
                }
                else
                {
                    _configService.Config.ItemsWithHistoricalTracking.Remove(itemId);
                }
                _configService.Save();
                _pendingTableRefresh = true;
                _graphCacheIsDirty = true;
            },
            isCurrencyHistoricalTrackingEnabled: (currencyId) => _configService.Config.EnabledTrackedDataTypes.Contains((TrackedDataType)currencyId),
            onCurrencyHistoricalTrackingToggled: (currencyId, enabled) =>
            {
                var dataType = (TrackedDataType)currencyId;
                if (enabled)
                {
                    _configService.Config.EnabledTrackedDataTypes.Add(dataType);
                }
                else
                {
                    _configService.Config.EnabledTrackedDataTypes.Remove(dataType);
                }
                _configService.Save();
                _pendingTableRefresh = true;
                _graphCacheIsDirty = true;
            });
        
        // Source Merging
        // Compute available row identifiers based on grouping mode
        var currentGroupingMode = settings.GroupingMode;
        var availableCharIds = _cachedTableData?.Rows?.Select(r => r.CharacterId).Distinct().ToList() 
                               ?? new List<ulong>();
        
        // For non-Character modes, compute available group keys
        List<string>? availableGroupKeys = null;
        if (currentGroupingMode != TableGroupingMode.Character && _cachedTableData?.Rows != null)
        {
            availableGroupKeys = currentGroupingMode switch
            {
                TableGroupingMode.World => _cachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.WorldName) ? "Unknown World" : r.WorldName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.DataCenter => _cachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.DataCenterName) ? "Unknown DC" : r.DataCenterName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.Region => _cachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.RegionName) ? "Unknown Region" : r.RegionName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.All => new List<string> { "All Characters" },
                _ => null
            };
        }
        
        MergeManagementWidget.DrawMergedRows(
            settings.MergedRowGroups,
            groupingMode: currentGroupingMode,
            getCharacterName: GetCharacterDisplayName,
            availableCharacterIds: availableCharIds,
            availableGroupKeys: availableGroupKeys,
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _pendingTableRefresh = true; _graphCacheIsDirty = true; },
            widgetId: $"datatool_rows_{GetHashCode()}");
        
        // Special Grouping
        SpecialGroupingWidget.Draw(
            settings.SpecialGrouping,
            settings.Columns,
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _pendingTableRefresh = true; _graphCacheIsDirty = true; },
            onAddColumn: (id, isCurrency) => AddColumn(id, isCurrency));
    }
    
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = _instanceSettings;
        
        // Use centralized column export
        var columns = ColumnManagementWidget.ExportColumns(settings.Columns);
        
        // Serialize merged column groups
        var mergedColumnGroups = settings.MergedColumnGroups.Select(g => new Dictionary<string, object?>
        {
            ["Name"] = g.Name,
            ["ColumnIndices"] = g.ColumnIndices.ToList(),
            ["Color"] = g.Color.HasValue ? new float[] { g.Color.Value.X, g.Color.Value.Y, g.Color.Value.Z, g.Color.Value.W } : null,
            ["Width"] = g.Width,
            ["ShowInTable"] = g.ShowInTable,
            ["ShowInGraph"] = g.ShowInGraph
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
            // View mode
            ["ViewMode"] = (int)settings.ViewMode,
            
            // Shared settings
            ["Columns"] = columns,
            ["IncludeRetainers"] = settings.IncludeRetainers,
            ["ShowActionButtons"] = settings.ShowActionButtons,
            ["UseCompactNumbers"] = settings.UseCompactNumbers,
            ["UseCharacterFilter"] = settings.UseCharacterFilter,
            ["SelectedCharacterIds"] = settings.SelectedCharacterIds.ToList(),
            ["GroupingMode"] = (int)settings.GroupingMode,
            ["SpecialGrouping"] = SpecialGroupingWidget.ExportSettings(settings.SpecialGrouping),
            
            // Table-specific
            ["MergedColumnGroups"] = mergedColumnGroups,
            ["MergedRowGroups"] = mergedRowGroups,
            ["ShowTotalRow"] = settings.ShowTotalRow,
            ["Sortable"] = settings.Sortable,
            ["CharacterColumnWidth"] = settings.CharacterColumnWidth,
            ["CharacterColumnColor"] = settings.CharacterColumnColor.HasValue ? new float[] { settings.CharacterColumnColor.Value.X, settings.CharacterColumnColor.Value.Y, settings.CharacterColumnColor.Value.Z, settings.CharacterColumnColor.Value.W } : null,
            ["SortColumnIndex"] = settings.SortColumnIndex,
            ["SortAscending"] = settings.SortAscending,
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
            ["HideCharacterColumnInAllMode"] = settings.HideCharacterColumnInAllMode,
            ["TextColorMode"] = (int)settings.TextColorMode,
            ["ShowRetainerBreakdown"] = settings.ShowRetainerBreakdown,
            ["ShowRetainerBreakdownInGraph"] = settings.ShowRetainerBreakdownInGraph,
            
            // Graph-specific
            ["LegendWidth"] = settings.LegendWidth,
            ["LegendHeightPercent"] = settings.LegendHeightPercent,
            ["ShowLegend"] = settings.ShowLegend,
            ["LegendPosition"] = (int)settings.LegendPosition,
            ["GraphType"] = (int)settings.GraphType,
            ["ShowXAxisTimestamps"] = settings.ShowXAxisTimestamps,
            ["ShowCrosshair"] = settings.ShowCrosshair,
            ["ShowGridLines"] = settings.ShowGridLines,
            ["ShowCurrentPriceLine"] = settings.ShowCurrentPriceLine,
            ["ShowValueLabel"] = settings.ShowValueLabel,
            ["ValueLabelOffsetX"] = settings.ValueLabelOffsetX,
            ["ValueLabelOffsetY"] = settings.ValueLabelOffsetY,
            ["AutoScrollEnabled"] = settings.AutoScrollEnabled,
            ["AutoScrollTimeValue"] = settings.AutoScrollTimeValue,
            ["AutoScrollTimeUnit"] = (int)settings.AutoScrollTimeUnit,
            ["AutoScrollNowPosition"] = settings.AutoScrollNowPosition,
            ["ShowControlsDrawer"] = settings.ShowControlsDrawer,
            ["TimeRangeValue"] = settings.TimeRangeValue,
            ["TimeRangeUnit"] = (int)settings.TimeRangeUnit
        };
    }
    
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        var target = _instanceSettings;
        
        // View mode
        target.ViewMode = (DataToolViewMode)SettingsImportHelper.GetSetting(settings, "ViewMode", (int)target.ViewMode);
        
        // Columns
        if (settings.TryGetValue("Columns", out var columnsObj) && columnsObj != null)
        {
            target.Columns.Clear();
            target.Columns.AddRange(ColumnManagementWidget.ImportColumns(columnsObj));
        }
        
        // Shared settings
        target.IncludeRetainers = SettingsImportHelper.GetSetting(settings, "IncludeRetainers", target.IncludeRetainers);
        target.ShowActionButtons = SettingsImportHelper.GetSetting(settings, "ShowActionButtons", target.ShowActionButtons);
        target.UseCompactNumbers = SettingsImportHelper.GetSetting(settings, "UseCompactNumbers", target.UseCompactNumbers);
        target.UseCharacterFilter = SettingsImportHelper.GetSetting(settings, "UseCharacterFilter", target.UseCharacterFilter);
        
        var selectedIds = SettingsImportHelper.ImportUlongList(settings, "SelectedCharacterIds");
        if (selectedIds != null)
        {
            target.SelectedCharacterIds.Clear();
            target.SelectedCharacterIds.AddRange(selectedIds);
        }
        
        target.GroupingMode = (TableGroupingMode)SettingsImportHelper.GetSetting(settings, "GroupingMode", (int)target.GroupingMode);
        
        // Special grouping
        if (settings.TryGetValue("SpecialGrouping", out var specialGroupingObj))
        {
            var specialGroupingDict = SettingsImportHelper.ConvertToDictionary(specialGroupingObj);
            SpecialGroupingWidget.ImportSettings(target.SpecialGrouping, specialGroupingDict);
        }
        
        // Table-specific
        target.ShowTotalRow = SettingsImportHelper.GetSetting(settings, "ShowTotalRow", target.ShowTotalRow);
        target.Sortable = SettingsImportHelper.GetSetting(settings, "Sortable", target.Sortable);
        target.CharacterColumnWidth = SettingsImportHelper.GetSetting(settings, "CharacterColumnWidth", target.CharacterColumnWidth);
        target.SortColumnIndex = SettingsImportHelper.GetSetting(settings, "SortColumnIndex", target.SortColumnIndex);
        target.SortAscending = SettingsImportHelper.GetSetting(settings, "SortAscending", target.SortAscending);
        target.UseFullNameWidth = SettingsImportHelper.GetSetting(settings, "UseFullNameWidth", target.UseFullNameWidth);
        target.AutoSizeEqualColumns = SettingsImportHelper.GetSetting(settings, "AutoSizeEqualColumns", target.AutoSizeEqualColumns);
        target.HorizontalAlignment = (TableHorizontalAlignment)SettingsImportHelper.GetSetting(settings, "HorizontalAlignment", (int)target.HorizontalAlignment);
        target.VerticalAlignment = (TableVerticalAlignment)SettingsImportHelper.GetSetting(settings, "VerticalAlignment", (int)target.VerticalAlignment);
        target.CharacterColumnHorizontalAlignment = (TableHorizontalAlignment)SettingsImportHelper.GetSetting(settings, "CharacterColumnHorizontalAlignment", (int)target.CharacterColumnHorizontalAlignment);
        target.CharacterColumnVerticalAlignment = (TableVerticalAlignment)SettingsImportHelper.GetSetting(settings, "CharacterColumnVerticalAlignment", (int)target.CharacterColumnVerticalAlignment);
        target.HeaderHorizontalAlignment = (TableHorizontalAlignment)SettingsImportHelper.GetSetting(settings, "HeaderHorizontalAlignment", (int)target.HeaderHorizontalAlignment);
        target.HeaderVerticalAlignment = (TableVerticalAlignment)SettingsImportHelper.GetSetting(settings, "HeaderVerticalAlignment", (int)target.HeaderVerticalAlignment);
        target.HideCharacterColumnInAllMode = SettingsImportHelper.GetSetting(settings, "HideCharacterColumnInAllMode", target.HideCharacterColumnInAllMode);
        target.TextColorMode = (TableTextColorMode)SettingsImportHelper.GetSetting(settings, "TextColorMode", (int)target.TextColorMode);
        target.ShowRetainerBreakdown = SettingsImportHelper.GetSetting(settings, "ShowRetainerBreakdown", target.ShowRetainerBreakdown);
        
        // Import merged column groups
        if (settings.TryGetValue("MergedColumnGroups", out var mergedColGroupsObj) && mergedColGroupsObj != null)
        {
            target.MergedColumnGroups.Clear();
            var groups = ImportMergedColumnGroups(mergedColGroupsObj);
            target.MergedColumnGroups.AddRange(groups);
        }
        
        // Import merged row groups
        if (settings.TryGetValue("MergedRowGroups", out var mergedRowGroupsObj) && mergedRowGroupsObj != null)
        {
            target.MergedRowGroups.Clear();
            var groups = ImportMergedRowGroups(mergedRowGroupsObj);
            target.MergedRowGroups.AddRange(groups);
        }
        
        // Colors
        target.CharacterColumnColor = SettingsImportHelper.ImportColor(settings, "CharacterColumnColor");
        target.HeaderColor = SettingsImportHelper.ImportColor(settings, "HeaderColor");
        target.EvenRowColor = SettingsImportHelper.ImportColor(settings, "EvenRowColor");
        target.OddRowColor = SettingsImportHelper.ImportColor(settings, "OddRowColor");
        
        // Hidden characters
        target.HiddenCharacters = SettingsImportHelper.ImportUlongHashSet(settings, "HiddenCharacters") ?? new HashSet<ulong>();
        
        // Graph-specific
        target.LegendWidth = SettingsImportHelper.GetSetting(settings, "LegendWidth", target.LegendWidth);
        target.LegendHeightPercent = SettingsImportHelper.GetSetting(settings, "LegendHeightPercent", target.LegendHeightPercent);
        target.ShowLegend = SettingsImportHelper.GetSetting(settings, "ShowLegend", target.ShowLegend);
        target.LegendPosition = (LegendPosition)SettingsImportHelper.GetSetting(settings, "LegendPosition", (int)target.LegendPosition);
        target.GraphType = (GraphType)SettingsImportHelper.GetSetting(settings, "GraphType", (int)target.GraphType);
        target.ShowXAxisTimestamps = SettingsImportHelper.GetSetting(settings, "ShowXAxisTimestamps", target.ShowXAxisTimestamps);
        target.ShowCrosshair = SettingsImportHelper.GetSetting(settings, "ShowCrosshair", target.ShowCrosshair);
        target.ShowGridLines = SettingsImportHelper.GetSetting(settings, "ShowGridLines", target.ShowGridLines);
        target.ShowCurrentPriceLine = SettingsImportHelper.GetSetting(settings, "ShowCurrentPriceLine", target.ShowCurrentPriceLine);
        target.ShowValueLabel = SettingsImportHelper.GetSetting(settings, "ShowValueLabel", target.ShowValueLabel);
        target.ValueLabelOffsetX = SettingsImportHelper.GetSetting(settings, "ValueLabelOffsetX", target.ValueLabelOffsetX);
        target.ValueLabelOffsetY = SettingsImportHelper.GetSetting(settings, "ValueLabelOffsetY", target.ValueLabelOffsetY);
        target.AutoScrollEnabled = SettingsImportHelper.GetSetting(settings, "AutoScrollEnabled", target.AutoScrollEnabled);
        target.AutoScrollTimeValue = SettingsImportHelper.GetSetting(settings, "AutoScrollTimeValue", target.AutoScrollTimeValue);
        target.AutoScrollTimeUnit = (TimeUnit)SettingsImportHelper.GetSetting(settings, "AutoScrollTimeUnit", (int)target.AutoScrollTimeUnit);
        target.AutoScrollNowPosition = SettingsImportHelper.GetSetting(settings, "AutoScrollNowPosition", target.AutoScrollNowPosition);
        target.ShowControlsDrawer = SettingsImportHelper.GetSetting(settings, "ShowControlsDrawer", target.ShowControlsDrawer);
        target.TimeRangeValue = SettingsImportHelper.GetSetting(settings, "TimeRangeValue", target.TimeRangeValue);
        target.TimeRangeUnit = (TimeUnit)SettingsImportHelper.GetSetting(settings, "TimeRangeUnit", (int)target.TimeRangeUnit);
        
        // Update character combo
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
        
        UpdateTitle();
        _pendingTableRefresh = true;
        _graphCacheIsDirty = true;
    }
    
    #endregion
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, TimeUnit timeUnit, float nowPosition)
    {
        _instanceSettings.AutoScrollEnabled = enabled;
        _instanceSettings.AutoScrollTimeValue = timeValue;
        _instanceSettings.AutoScrollTimeUnit = timeUnit;
        _instanceSettings.AutoScrollNowPosition = nowPosition;
        NotifyToolSettingsChanged();
        _graphCacheIsDirty = true;
    }
    
    /// <summary>
    /// Imports merged column groups from various serialized formats.
    /// </summary>
    private static List<MergedColumnGroup> ImportMergedColumnGroups(object? obj)
    {
        var result = new List<MergedColumnGroup>();
        if (obj == null) return result;
        
        try
        {
            System.Collections.IEnumerable? enumerable = null;
            
            if (obj is Newtonsoft.Json.Linq.JArray jArray)
                enumerable = jArray;
            else if (obj is System.Collections.IEnumerable e)
                enumerable = e;
            
            if (enumerable == null) return result;
            
            foreach (var item in enumerable)
            {
                var dict = SettingsImportHelper.ConvertToDictionary(item);
                if (dict == null) continue;
                
                var group = new MergedColumnGroup
                {
                    Name = SettingsImportHelper.GetSetting(dict, "Name", "Merged"),
                    Width = SettingsImportHelper.GetSetting(dict, "Width", 80f),
                    Color = SettingsImportHelper.ImportColor(dict, "Color"),
                    ShowInTable = SettingsImportHelper.GetSetting(dict, "ShowInTable", true),
                    ShowInGraph = SettingsImportHelper.GetSetting(dict, "ShowInGraph", true)
                };
                
                var indices = SettingsImportHelper.ImportIntList(dict, "ColumnIndices");
                if (indices != null)
                    group.ColumnIndices = indices;
                
                result.Add(group);
            }
        }
        catch
        {
            // Graceful fallback
        }
        
        return result;
    }
    
    /// <summary>
    /// Imports merged row groups from various serialized formats.
    /// </summary>
    private static List<MergedRowGroup> ImportMergedRowGroups(object? obj)
    {
        var result = new List<MergedRowGroup>();
        if (obj == null) return result;
        
        try
        {
            System.Collections.IEnumerable? enumerable = null;
            
            if (obj is Newtonsoft.Json.Linq.JArray jArray)
                enumerable = jArray;
            else if (obj is System.Collections.IEnumerable e)
                enumerable = e;
            
            if (enumerable == null) return result;
            
            foreach (var item in enumerable)
            {
                var dict = SettingsImportHelper.ConvertToDictionary(item);
                if (dict == null) continue;
                
                var group = new MergedRowGroup
                {
                    Name = SettingsImportHelper.GetSetting(dict, "Name", "Merged"),
                    Color = SettingsImportHelper.ImportColor(dict, "Color")
                };
                
                var charIds = SettingsImportHelper.ImportUlongList(dict, "CharacterIds");
                if (charIds != null)
                    group.CharacterIds = charIds;
                
                result.Add(group);
            }
        }
        catch
        {
            // Graceful fallback
        }
        
        return result;
    }
    
    public override void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        _characterCombo?.Dispose();
        base.Dispose();
    }
}
