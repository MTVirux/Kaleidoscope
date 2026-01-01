using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// Unified tool component that can display data as either a table or a graph.
/// Maintains all settings when switching between views.
/// </summary>
/// <remarks>
/// This is a partial class split across multiple files:
/// - DataTool.Main.cs: Core setup, fields, constructor, entry points
/// - DataTool.TableView.cs: Table view rendering and data population
/// - DataTool.GraphView.cs: Graph view rendering and series data loading
/// - DataTool.Settings.cs: Tool settings, context menus, import/export
/// </remarks>
public partial class DataTool : ToolComponent
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
    private readonly MTGraphWidget _graphWidget;
    private readonly MTItemComboDropdown? _itemCombo;
    private readonly MTCurrencyComboDropdown? _currencyCombo;
    private readonly MTCharacterCombo? _characterCombo;
    
    // Instance-specific settings
    private readonly DataToolSettings _instanceSettings;
    
    // Table view cached data
    private PreparedItemTableData? _cachedTableData;
    private DateTime _lastTableRefresh = DateTime.MinValue;
    private volatile bool _pendingTableRefresh = true;
    
    // Graph view cached data (tuple format matching MTGraphWidget.RenderMultipleSeries)
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? _cachedSeriesData;
    private List<MTGraphSeriesGroup>? _cachedSeriesGroups;
    private DateTime _lastGraphRefresh = DateTime.MinValue;
    private volatile bool _graphCacheIsDirty = true;
    private int _cachedSeriesCount;
    private int _cachedTimeRangeValue;
    private MTTimeUnit _cachedTimeRangeUnit;
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
    private CharacterDataCacheService CharacterDataCache => _CurrencyTrackerService.CharacterDataCache;
    
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
        _graphWidget = new MTGraphWidget(new MTGraphConfig
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
            _itemCombo = new MTItemComboDropdown(
                textureProvider,
                _dataManager,
                favoritesService,
                null,
                "DataToolItemAdd",
                marketableOnly: false,
                configService: configService,
                trackedDataRegistry: trackedDataRegistry,
                excludeCurrencies: true,
                multiSelect: true);
        }
        
        // Create currency combo
        if (textureProvider != null && trackedDataRegistry != null && favoritesService != null)
        {
            _currencyCombo = new MTCurrencyComboDropdown(
                textureProvider,
                trackedDataRegistry,
                favoritesService,
                "DataToolCurrencyAdd",
                itemDataService,
                multiSelect: true);
        }
        
        // Create character combo
        if (favoritesService != null)
        {
            _characterCombo = new MTCharacterCombo(
                CurrencyTrackerService,
                favoritesService,
                configService,
                "DataToolCharFilter",
                multiSelect: true,
                autoRetainerService,
                priceTrackingService);
            _characterCombo.MultiSelectionChanged += OnCharacterSelectionChanged;
            
            // Restore selection from settings
            if (_instanceSettings.UseCharacterFilter && _instanceSettings.SelectedCharacterIds.Count > 0)
            {
                _characterCombo.SetSelection(_instanceSettings.SelectedCharacterIds);
            }
        }
        
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
        
        // Item/Currency combo dropdowns (shared for both table and graph views)
        DrawItemCurrencyCombos();
    }
    
    /// <summary>
    /// Draws the item and currency multi-select combo dropdowns.
    /// Syncs selection state between the combos and the current columns/series configuration.
    /// </summary>
    private void DrawItemCurrencyCombos()
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
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, MTTimeUnit timeUnit, float nowPosition)
    {
        _instanceSettings.AutoScrollEnabled = enabled;
        _instanceSettings.AutoScrollTimeValue = timeValue;
        _instanceSettings.AutoScrollTimeUnit = timeUnit;
        _instanceSettings.AutoScrollNowPosition = nowPosition;
        NotifyToolSettingsChanged();
        _graphCacheIsDirty = true;
    }
    
    public override void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        _characterCombo?.Dispose();
        base.Dispose();
    }
}
