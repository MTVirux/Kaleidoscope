using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// Unified tool component that can display data as either a table or a graph.
/// Maintains all settings when switching between views.
/// </summary>
/// <remarks>
/// This is a partial class split across multiple files:
/// - DataTool.Main.cs: Core setup, fields, constructor, shared plumbing, view-mode switch
/// - DataTool.Settings.cs: Tool settings, context menus, import/export
///
/// The two views are separate component classes constructed and delegated to from here:
/// - DataToolTableView.cs: Table view cache and rendering
/// - DataToolGraphView.cs: Graph view cache, series loading, and rendering
/// </remarks>
[ToolType("DataGraph", "Data Graph", "Items/Currency", "Track items and currencies over time with graphing visualization", Variant = "Graph")]
[ToolType("DataTable", "Data Table", "Items/Currency", "Track items and currencies in a table view with characters as rows", Variant = "Table")]
public sealed partial class DataTool : ToolComponent
{
    public override string ToolName => "Data";
    
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService _configService;
    private readonly GameStateService _gameState;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    private readonly AutoRetainerService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly FavoritesService? _favoritesService;
    private readonly ITextureProvider? _textureProvider;
    private readonly ResourceObservationService? _resourceObservationService;
    
    // Widgets
    private readonly ItemTableWidget _tableWidget;
    private readonly GraphWidget _graphWidget;
    private readonly ItemComboDropdown? _itemCombo;
    private readonly CurrencyComboDropdown? _currencyCombo;
    private readonly CharacterCombo? _characterCombo;
    
    // Instance-specific settings
    private readonly DataToolSettings _instanceSettings;

    // View components (each owns its view-specific cache and draw logic)
    private readonly DataToolTableView _tableView;
    private readonly DataToolGraphView _graphView;

    // Selection/merge UI state owned by this tool instance
    private readonly ColumnManagementState _columnState = new();
    private readonly MergeManagementState _mergeRowState = new();

    // Shared cached state
    private CharacterNameFormat _cachedNameFormat;
    private CharacterSortOrder _cachedSortOrder;
    
    /// <summary>
    /// The name of the preset used to create this tool, if any.
    /// </summary>
    public string? PresetName { get; set; }
    
    private DataToolSettings Settings => _instanceSettings;
    private TimeSeriesCacheService CacheService => _currencyTrackerService.CacheService;
    private CharacterDataCacheService CharacterDataCache => _currencyTrackerService.CharacterDataCache;
    
    public DataTool(
        CurrencyTrackerService currencyTrackerService,
        ConfigurationService configService,
        GameStateService gameState,
        InventoryCacheService? inventoryCacheService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null,
        AutoRetainerService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null,
        LifestreamService? lifestreamService = null,
        INotificationManager? notificationManager = null,
        ResourceObservationService? resourceObservationService = null)
    {
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;
        _gameState = gameState;
        _inventoryCacheService = inventoryCacheService;
        _trackedDataRegistry = trackedDataRegistry;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        _favoritesService = favoritesService;
        _textureProvider = textureProvider;
        _resourceObservationService = resourceObservationService;

        
        // Initialize instance-specific settings with global defaults
        var uiColors = configService.Config.UIColors;
        _instanceSettings = new DataToolSettings
        {
            TableNumberFormat = configService.Config.DefaultTableNumberFormat.Clone(),
            GraphNumberFormat = configService.Config.DefaultGraphNumberFormat.Clone(),
            HeaderColor = uiColors.TableHeader,
            EvenRowColor = uiColors.TableRowEven,
            OddRowColor = uiColors.TableRowOdd
        };
        
        Size = new Vector2(500, 300);
        UpdateTitle();
        
        // Create the table widget
        _tableWidget = new ItemTableWidget(
            new ItemTableWidget.TableConfig
            {
                TableId = "DataToolTable",
                NoDataText = "No data yet. Add items or currencies to track.",
                TotalRowColor = uiColors.TableTotalRow
            },
            itemDataService,
            trackedDataRegistry,
            configService.Config,
            currencyTrackerService.CacheService,
            lifestreamService,
            notificationManager,
            gameState);
        
        // Bind table widget to settings
        _tableWidget.BindSettings(
            _instanceSettings,
            () => NotifyToolSettingsChanged(),
            "Table Settings");
        
        // Create the graph widget (inherit global graph style from config)
        _graphWidget = new GraphWidget(new GraphConfig
        {
            PlotId = "DataToolGraph",
            MinValue = 0f,
            MaxValue = 100_000_000f,
            NoDataText = "No historical data available.",
            Style = configService.Config.GraphStyle
        });
        
        // Bind graph widget to settings
        _graphWidget.BindSettings(
            _instanceSettings,
            () => { _graphView?.MarkDirty(); NotifyToolSettingsChanged(); },
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
                excludeCurrencies: true,
                multiSelect: true);
        }
        
        // Create currency combo
        if (textureProvider != null && trackedDataRegistry != null && favoritesService != null)
        {
            _currencyCombo = new CurrencyComboDropdown(
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
            _characterCombo = new CharacterCombo(
                currencyTrackerService,
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

        // Construct the view components with the shared plumbing they read. The version-counter
        // source, character-name formatting, and logging stay owned here and are supplied as
        // delegates. GetCacheVersions is a pure snapshot reader; each view keeps its own last-seen
        // snapshot, so change detection no longer relies on the sibling view's draw cadence.
        _tableView = new DataToolTableView(
            currencyTrackerService,
            configService,
            inventoryCacheService,
            autoRetainerService,
            priceTrackingService,
            _tableWidget,
            GetCacheVersions,
            LogDebug);

        _graphView = new DataToolGraphView(
            currencyTrackerService,
            configService,
            inventoryCacheService,
            autoRetainerService,
            priceTrackingService,
            itemDataService,
            trackedDataRegistry,
            _graphWidget,
            GetCacheVersions,
            GetCharacterDisplayName,
            LogDebug);
    }
    
    /// <summary>
    /// Sets the columns/series for this tool. Used by presets.
    /// </summary>
    public void SetColumns(List<ItemColumnConfig> columns)
    {
        _instanceSettings.Columns.Clear();
        _instanceSettings.Columns.AddRange(columns);
        _tableView.RequestRefresh();
        _graphView.MarkDirty();
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
        _tableView.RequestRefresh();
        _graphView.MarkDirty();
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
        _tableView.RequestRefresh();
        _graphView.MarkDirty();
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
                _tableView.RequestRefresh();
                _graphView.MarkDirty();
            }

            // Check if sort order changed
            var currentSortOrder = _configService.Config.CharacterSortOrder;
            if (_cachedSortOrder != currentSortOrder)
            {
                _cachedSortOrder = currentSortOrder;
                _tableView.RequestRefresh();
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
                _tableView.Draw(Settings);
            }
            else
            {
                _graphView.Draw(Settings);
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogDebug($"Draw error: {ex.Message}");
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
            _tableView.RequestRefresh();
            _graphView.MarkDirty();
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(toggleTooltip);
        }
        
        ImGui.SameLine();
        
        // Calculate available width for combos after the toggle button
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        
        // Determine number of combos to display
        var comboCount = 0;
        if (_characterCombo != null) comboCount++;
        if (_itemCombo != null) comboCount++;
        if (_currencyCombo != null) comboCount++;
        
        // Calculate width per combo (divide available width minus spacing)
        var comboWidth = comboCount > 0 
            ? (availableWidth - (spacing * (comboCount - 1))) / comboCount 
            : availableWidth;
        
        // Character filter combo
        if (_characterCombo != null)
        {
            ImGui.SetNextItemWidth(comboWidth);
            _characterCombo.Draw(comboWidth);
            ImGui.SameLine();
        }
        
        // Item/Currency combo dropdowns (shared for both table and graph views)
        DrawItemCurrencyCombos(comboWidth);
    }
    
    /// <summary>
    /// Draws the item and currency multi-select combo dropdowns.
    /// Syncs selection state between the combos and the current columns/series configuration.
    /// </summary>
    /// <param name="comboWidth">Width to use for each combo box</param>
    private void DrawItemCurrencyCombos(float comboWidth)
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
            
            _itemCombo.DrawMultiSelect(comboWidth);
            
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
            
            _currencyCombo.DrawMultiSelect(comboWidth);
            
            var newSelection = _currencyCombo.GetMultiSelection();
            SyncCurrencyColumns(newSelection);
            
            // Add small padding after currency combo
            ImGui.Dummy(new Vector2(0, 4f));
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
            _tableView.RequestRefresh();
            _graphView.MarkDirty();
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
            _tableView.RequestRefresh();
            _graphView.MarkDirty();
            NotifyToolSettingsChanged();
        }
    }
    
    private void AddColumn(uint id, bool isCurrency)
    {
        if (ColumnManagementWidget.AddColumn(Settings.Columns, id, isCurrency))
        {
            _tableView.RequestRefresh();
            _graphView.MarkDirty();
            NotifyToolSettingsChanged();
        }
    }
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, TimeUnit timeUnit, float nowPosition)
    {
        _instanceSettings.AutoScrollEnabled = enabled;
        _instanceSettings.AutoScrollTimeValue = timeValue;
        _instanceSettings.AutoScrollTimeUnit = timeUnit;
        _instanceSettings.AutoScrollNowPosition = nowPosition;
        NotifyToolSettingsChanged();
        _graphView.MarkDirty();
    }
    
    /// <summary>
    /// Reads the current upstream cache version counters (time-series, character data, resources DB)
    /// as an immutable O(1) snapshot. Pure — no side effects. Each view keeps its own last-seen
    /// snapshot and compares against this, so change detection no longer depends on exactly one view
    /// drawing per frame or on both views being marked dirty on every view-mode switch.
    /// </summary>
    private (long timeSeries, long character, long resources) GetCacheVersions()
        => (CacheService.Version,
            CharacterDataCache.Version,
            _resourceObservationService?.DbVersion ?? 0);

    /// <summary>
    /// Gets a display name for the provided character ID.
    /// Uses formatted name from cache service, respecting the name format setting.
    /// Shared plumbing read by both the graph view and the source-merge settings UI.
    /// </summary>
    private string GetCharacterDisplayName(ulong characterId)
    {
        // Use cache service which handles display name, game name formatting, and fallbacks
        var formattedName = CacheService.GetFormattedCharacterName(characterId);
        if (!string.IsNullOrEmpty(formattedName))
            return formattedName;

        // Try runtime lookup for currently-loaded characters (formats it)
        var runtimeName = _gameState.GetCharacterName(characterId);
        if (!string.IsNullOrEmpty(runtimeName))
            return Kaleidoscope.Libs.CharacterNameFormatter.FormatName(runtimeName, _configService.Config.CharacterNameFormat) ?? runtimeName;

        // Fallback to ID
        return $"Character {characterId}";
    }

    public override void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        _characterCombo?.Dispose();
        base.Dispose();
    }
}
