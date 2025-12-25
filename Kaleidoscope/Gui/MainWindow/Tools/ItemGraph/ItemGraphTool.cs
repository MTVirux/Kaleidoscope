using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.ItemGraph;

/// <summary>
/// Tool component that displays a customizable graph of items/currencies across characters over time.
/// Users can add items and currencies to track, customize series names and colors.
/// Similar to ItemTableTool but displays time-series data as a graph instead of a table.
/// </summary>
public class ItemGraphTool : ToolComponent
{
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    
    private readonly ImplotGraphWidget _graphWidget;
    private readonly ItemIconCombo? _itemCombo;
    
    // Instance-specific settings (not shared with other tool instances)
    private readonly ItemGraphSettings _instanceSettings;
    
    // Cache service for formatted character names
    private TimeSeriesCacheService _cacheService => _samplerService.CacheService;
    
    // Cached data for graph rendering (includes optional color for each series)
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? _cachedSeriesData;
    private DateTime _lastRefresh = DateTime.MinValue;
    private volatile bool _cacheIsDirty = true;
    
    // Cache tracking for change detection
    private int _cachedSeriesCount;
    private int _cachedTimeRangeValue;
    private TimeRangeUnit _cachedTimeRangeUnit;
    private bool _cachedIncludeRetainers;
    private bool _cachedShowPerCharacter;
    private CharacterNameFormat _cachedNameFormat;
    private const double CacheValiditySeconds = 2.0;
    
    private ItemGraphSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _samplerService.DbService;
    
    public ItemGraphTool(
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null)
    {
        _samplerService = samplerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _trackedDataRegistry = trackedDataRegistry;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        
        // Initialize instance-specific settings with defaults
        _instanceSettings = new ItemGraphSettings();
        
        Title = "Item Graph";
        Size = new Vector2(500, 300);
        
        // Initialize graph widget
        _graphWidget = new ImplotGraphWidget(new ImplotGraphWidget.GraphConfig
        {
            PlotId = "item_graph_plot",
            NoDataText = "No data yet. Add items or currencies to track.",
            ShowValueLabel = true,
            ShowXAxisTimestamps = true,
            ShowCrosshair = true,
            ShowGridLines = true,
            ShowCurrentPriceLine = true
        });
        
        // Bind graph widget to instance-specific settings (not global config)
        _graphWidget.BindSettings(
            _instanceSettings,
            onSettingsChanged: () =>
            {
                NotifyToolSettingsChanged();
                _cacheIsDirty = true;
            },
            settingsName: "Graph Settings",
            showLegendSettings: true);
        
        // Register graph widget for automatic settings drawing
        RegisterSettingsProvider(_graphWidget);
        
        _graphWidget.OnAutoScrollSettingsChanged += OnAutoScrollSettingsChanged;
        
        // Create item combo if we have the required services
        if (_dataManager != null && _itemDataService != null && textureProvider != null && favoritesService != null)
        {
            _itemCombo = new ItemIconCombo(
                textureProvider,
                _dataManager,
                favoritesService,
                null, // No price tracking service - include all items
                "ItemGraphAdd",
                marketableOnly: false);
        }
    }
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, AutoScrollTimeUnit timeUnit, float nowPosition)
    {
        var settings = Settings;
        settings.AutoScrollEnabled = enabled;
        settings.AutoScrollTimeValue = timeValue;
        settings.AutoScrollTimeUnit = timeUnit;
        settings.AutoScrollNowPosition = nowPosition;
        NotifyToolSettingsChanged();
        // Note: Don't set _cacheIsDirty here - auto-scroll settings don't require data refresh
        // The graph widget handles auto-scroll updates internally without needing new data
    }
    
    /// <summary>
    /// Checks if the cache needs to be refreshed.
    /// </summary>
    private bool NeedsCacheRefresh()
    {
        if (_cacheIsDirty) return true;
        
        var settings = Settings;
        
        // Check if settings changed
        if (_cachedSeriesCount != settings.Series.Count ||
            _cachedTimeRangeValue != settings.TimeRangeValue ||
            _cachedTimeRangeUnit != settings.TimeRangeUnit ||
            _cachedIncludeRetainers != settings.IncludeRetainers ||
            _cachedShowPerCharacter != settings.ShowPerCharacter ||
            _cachedNameFormat != _configService.Config.CharacterNameFormat)
        {
            return true;
        }
        
        // When auto-scroll is enabled, don't refresh based on time
        // The graph widget handles the time movement internally
        // Only refresh when data actually changes (via _cacheIsDirty)
        if (settings.AutoScrollEnabled)
        {
            return false;
        }
        
        // Check if cache is stale (time-based) - only when not auto-scrolling
        var elapsed = (DateTime.UtcNow - _lastRefresh).TotalSeconds;
        return elapsed >= CacheValiditySeconds;
    }
    
    public override void DrawContent()
    {
        try
        {
            var settings = Settings;
            
            // Draw action buttons (if enabled)
            if (settings.ShowActionButtons)
            {
                DrawActionButtons();
                ImGui.Separator();
            }
            
            // Graph
            using (ProfilerService.BeginStaticChildScope("DrawGraph"))
            {
                DrawGraph();
            }
            
            // Draw popups for adding items/currencies
            DrawAddItemPopup();
            DrawAddCurrencyPopup();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[ItemGraphTool] Draw error: {ex.Message}");
        }
    }
    
    private void DrawActionButtons()
    {
        if (ImGui.Button("+ Add Item"))
        {
            ImGui.OpenPopup("AddItemPopup");
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("+ Add Currency"))
        {
            ImGui.OpenPopup("AddCurrencyPopup");
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Refresh"))
        {
            _cacheIsDirty = true;
        }
        
        // Show series count
        var seriesCount = Settings.Series.Count;
        ImGui.SameLine();
        ImGui.TextDisabled($"({seriesCount} series)");
    }
    
    private void DrawAddItemPopup()
    {
        if (ImGui.BeginPopup("AddItemPopup"))
        {
            ImGui.TextUnformatted("Add Item Series");
            ImGui.Separator();
            
            if (_itemCombo != null)
            {
                if (_itemCombo.Draw(_itemCombo.SelectedItem?.Name ?? "Select item...", _itemCombo.SelectedItemId, 250, 300))
                {
                    // Item selected
                    if (_itemCombo.SelectedItemId > 0)
                    {
                        AddSeries(_itemCombo.SelectedItemId, isCurrency: false);
                        _itemCombo.ClearSelection();
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("Item picker not available.");
            }
            
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
        }
    }
    
    private void DrawAddCurrencyPopup()
    {
        if (ImGui.BeginPopup("AddCurrencyPopup"))
        {
            ImGui.TextUnformatted("Add Currency Series");
            ImGui.Separator();
            
            if (_trackedDataRegistry != null)
            {
                // Group currencies by category
                var categories = _trackedDataRegistry.Definitions.Values
                    .GroupBy(d => d.Category)
                    .OrderBy(g => g.Key);
                
                foreach (var category in categories)
                {
                    if (ImGui.TreeNode($"{category.Key}##cat"))
                    {
                        foreach (var def in category.OrderBy(d => d.DisplayName))
                        {
                            // Check if already added
                            var alreadyAdded = Settings.Series.Any(c => c.IsCurrency && c.Id == (uint)def.Type);
                            
                            if (alreadyAdded)
                            {
                                ImGui.TextDisabled($"✓ {def.DisplayName}");
                            }
                            else if (ImGui.Selectable(def.DisplayName))
                            {
                                AddSeries((uint)def.Type, isCurrency: true);
                                ImGui.CloseCurrentPopup();
                            }
                        }
                        ImGui.TreePop();
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("Currency registry not available.");
            }
            
            ImGui.Spacing();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
        }
    }
    
    private void AddSeries(uint id, bool isCurrency)
    {
        // Check if already exists
        if (Settings.Series.Any(c => c.Id == id && c.IsCurrency == isCurrency))
            return;
        
        Settings.Series.Add(new ItemColumnConfig
        {
            Id = id,
            IsCurrency = isCurrency
        });
        
        _cacheIsDirty = true;
        NotifyToolSettingsChanged();
    }
    
    private void DrawGraph()
    {
        // Sync graph widget from bound settings (in case settings changed externally)
        _graphWidget.SyncFromBoundSettings();

        // Refresh cache if needed
        if (NeedsCacheRefresh())
        {
            using (ProfilerService.BeginStaticChildScope("RefreshCachedData"))
            {
                RefreshCachedData(Settings);
            }
        }
        
        // Draw from cache
        if (_cachedSeriesData != null && _cachedSeriesData.Count > 0)
        {
            _graphWidget.DrawMultipleSeries(_cachedSeriesData);
        }
        else
        {
            if (Settings.Series.Count == 0)
            {
                ImGui.TextDisabled("No items or currencies configured. Add some to start tracking.");
            }
            else
            {
                ImGui.TextDisabled("No data available yet.");
            }
        }
    }
    
    /// <summary>
    /// Refreshes the cached data from the database.
    /// Uses batched queries for better performance when tracking multiple items.
    /// </summary>
    private void RefreshCachedData(ItemGraphSettings settings)
    {
        // Update cache tracking
        _lastRefresh = DateTime.UtcNow;
        _cachedSeriesCount = settings.Series.Count;
        _cachedTimeRangeValue = settings.TimeRangeValue;
        _cachedTimeRangeUnit = settings.TimeRangeUnit;
        _cachedIncludeRetainers = settings.IncludeRetainers;
        _cachedShowPerCharacter = settings.ShowPerCharacter;
        _cachedNameFormat = _configService.Config.CharacterNameFormat;
        _cacheIsDirty = false;
        
        var series = settings.Series;
        if (series.Count == 0)
        {
            _cachedSeriesData = null;
            return;
        }
        
        // Get time range
        var timeRange = GetTimeRange();
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : (DateTime?)null;
        
        // Batch fetch all player item data
        var itemData = DbService.GetAllPointsBatch("Item_", startTime);
        
        // Also fetch retainer data if Include Retainers is enabled
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>? retainerData = null;
        if (settings.IncludeRetainers)
        {
            retainerData = DbService.GetAllPointsBatch("ItemRetainer_", startTime);
        }
        
        var seriesList = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        
        if (settings.ShowPerCharacter)
        {
            // Per-character mode: show each character as a separate line for each tracked item/currency
            foreach (var seriesConfig in series)
            {
                var baseName = GetSeriesName(seriesConfig);
                var baseColor = seriesConfig.Color;
                
                if (seriesConfig.IsCurrency)
                {
                    // Get currency data per character
                    var perCharacterSeries = GetCurrencyTimeSeriesPerCharacter(seriesConfig, startTime, baseName);
                    // Apply the same color to all character sub-series for this item/currency
                    foreach (var (name, samples) in perCharacterSeries)
                    {
                        seriesList.Add((name, samples, baseColor));
                    }
                }
                else
                {
                    // Get item data per character from the pre-fetched batch
                    var perCharacterSeries = ExtractItemTimeSeriesPerCharacter(seriesConfig, itemData, retainerData, baseName);
                    // Apply the same color to all character sub-series for this item/currency
                    foreach (var (name, samples) in perCharacterSeries)
                    {
                        seriesList.Add((name, samples, baseColor));
                    }
                }
            }
        }
        else
        {
            // Aggregated mode: sum across all characters
            foreach (var seriesConfig in series)
            {
                var seriesName = GetSeriesName(seriesConfig);
                List<(DateTime ts, float value)> samples;
                
                if (seriesConfig.IsCurrency)
                {
                    // Get currency data from sampler database (time series)
                    samples = GetCurrencyTimeSeries(seriesConfig, startTime);
                }
                else
                {
                    // Get item data from the pre-fetched batch, optionally including retainer data
                    samples = ExtractItemTimeSeries(seriesConfig, itemData, retainerData);
                }
                
                if (samples.Count > 0)
                {
                    seriesList.Add((seriesName, samples, seriesConfig.Color));
                }
            }
        }
        
        _cachedSeriesData = seriesList.Count > 0 ? seriesList : null;
    }
    
    /// <summary>
    /// Gets the display name for a series configuration.
    /// </summary>
    private string GetSeriesName(ItemColumnConfig config)
    {
        if (!string.IsNullOrEmpty(config.CustomName))
            return config.CustomName;
        
        if (config.IsCurrency && _trackedDataRegistry != null)
        {
            var dataType = (TrackedDataType)config.Id;
            if (_trackedDataRegistry.Definitions.TryGetValue(dataType, out var def))
                return def.ShortName;
        }
        
        if (!config.IsCurrency && _itemDataService != null)
        {
            return _itemDataService.GetItemName(config.Id) ?? $"Item {config.Id}";
        }
        
        return config.IsCurrency ? $"Currency {config.Id}" : $"Item {config.Id}";
    }
    
    /// <summary>
    /// Gets time-series data for a currency type (aggregated across all characters).
    /// </summary>
    private List<(DateTime ts, float value)> GetCurrencyTimeSeries(ItemColumnConfig config, DateTime? startTime)
    {
        try
        {
            var dataType = (TrackedDataType)config.Id;
            var variableName = dataType.ToString();
            
            // Get all points for this variable (across all characters)
            var allPoints = DbService.GetAllPointsBatch(variableName, startTime);
            
            if (!allPoints.TryGetValue(variableName, out var points) || points.Count == 0)
                return new List<(DateTime ts, float value)>();
            
            // Aggregate across characters by timestamp
            // Group by timestamp (rounded to minute for aggregation) and sum values
            var aggregated = points
                .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day, 
                    p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                .OrderBy(p => p.ts)
                .ToList();
            
            return aggregated;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemGraphTool] GetCurrencyTimeSeries error: {ex.Message}");
            return new List<(DateTime ts, float value)>();
        }
    }
    
    /// <summary>
    /// Extracts time-series data for an item from the pre-fetched batch data.
    /// Uses historical data from the series/points tables, with fallback to current inventory cache.
    /// Optionally includes retainer inventory data if retainerBatchData is provided.
    /// </summary>
    private List<(DateTime ts, float value)> ExtractItemTimeSeries(
        ItemColumnConfig config, 
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> playerBatchData,
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>? retainerBatchData)
    {
        try
        {
            var playerVariableName = InventoryCacheService.GetItemVariableName(config.Id);
            var retainerVariableName = $"ItemRetainer_{config.Id}";
            
            // Collect all player points
            var allPoints = new List<(DateTime timestamp, long value)>();
            
            if (playerBatchData.TryGetValue(playerVariableName, out var playerPoints) && playerPoints.Count > 0)
            {
                // Group player data by timestamp and sum across characters
                var playerByTime = playerPoints
                    .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day, 
                        p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                    .Select(g => (ts: g.Key, value: g.Sum(p => p.value)));
                
                foreach (var (ts, value) in playerByTime)
                {
                    allPoints.Add((ts, value));
                }
            }
            
            // Add retainer points if provided
            if (retainerBatchData != null && 
                retainerBatchData.TryGetValue(retainerVariableName, out var retainerPoints) && 
                retainerPoints.Count > 0)
            {
                // Group retainer data by timestamp and sum across characters
                var retainerByTime = retainerPoints
                    .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day, 
                        p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                    .Select(g => (ts: g.Key, value: g.Sum(p => p.value)));
                
                foreach (var (ts, value) in retainerByTime)
                {
                    allPoints.Add((ts, value));
                }
            }
            
            if (allPoints.Count > 0)
            {
                // Aggregate by timestamp (player + retainer at same timestamp get summed)
                var aggregated = allPoints
                    .GroupBy(p => p.timestamp)
                    .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                    .OrderBy(p => p.ts)
                    .ToList();
                
                return aggregated;
            }
            
            // Fallback: No historical data yet, show current value as single point
            return GetItemCurrentValue(config, retainerBatchData != null);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemGraphTool] ExtractItemTimeSeries error: {ex.Message}");
            return new List<(DateTime ts, float value)>();
        }
    }
    
    /// <summary>
    /// Gets current item value as a single-point time series.
    /// Used as fallback when no historical data exists yet.
    /// </summary>
    private List<(DateTime ts, float value)> GetItemCurrentValue(ItemColumnConfig config, bool includeRetainers)
    {
        try
        {
            if (_inventoryCacheService == null) 
                return new List<(DateTime ts, float value)>();
            
            var allInventories = _inventoryCacheService.GetAllInventories();
            long totalCount = 0;
            
            foreach (var cache in allInventories)
            {
                // Skip retainers if not included
                if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                    continue;
                totalCount += cache.Items
                    .Where(i => i.ItemId == config.Id)
                    .Sum(i => (long)i.Quantity);
            }
            
            if (totalCount > 0)
            {
                // Return current time with current count
                return new List<(DateTime ts, float value)> { (DateTime.UtcNow, totalCount) };
            }
            
            return new List<(DateTime ts, float value)>();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemGraphTool] GetItemCurrentValue error: {ex.Message}");
            return new List<(DateTime ts, float value)>();
        }
    }
    
    /// <summary>
    /// Gets time-series data for a currency type, with separate series for each character.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> GetCurrencyTimeSeriesPerCharacter(
        ItemColumnConfig config, 
        DateTime? startTime,
        string baseName)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        
        try
        {
            var dataType = (TrackedDataType)config.Id;
            var variableName = dataType.ToString();
            
            // Get all points for this variable (across all characters)
            var allPoints = DbService.GetAllPointsBatch(variableName, startTime);
            
            if (!allPoints.TryGetValue(variableName, out var points) || points.Count == 0)
                return result;
            
            // Group by character
            var byCharacter = points.GroupBy(p => p.characterId).ToList();
            
            // Get disambiguated names for all characters
            var characterIds = byCharacter.Select(g => g.Key);
            var disambiguatedNames = _cacheService.GetDisambiguatedNames(characterIds);
            
            foreach (var charGroup in byCharacter)
            {
                var characterId = charGroup.Key;
                var characterName = disambiguatedNames.TryGetValue(characterId, out var name) 
                    ? name : GetCharacterDisplayName(characterId);
                var seriesName = $"{baseName} ({characterName})";
                
                var samples = charGroup
                    .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                        p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                    .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                    .OrderBy(p => p.ts)
                    .ToList();
                
                if (samples.Count > 0)
                {
                    result.Add((seriesName, samples));
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemGraphTool] GetCurrencyTimeSeriesPerCharacter error: {ex.Message}");
        }
        
        return result;
    }
    
    /// <summary>
    /// Extracts time-series data for an item from the pre-fetched batch data, with separate series for each character.
    /// Optionally combines player and retainer inventory data when retainerBatchData is provided.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> ExtractItemTimeSeriesPerCharacter(
        ItemColumnConfig config,
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> batchData,
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>? retainerBatchData,
        string baseName)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        
        try
        {
            var variableName = InventoryCacheService.GetItemVariableName(config.Id);
            var retainerVariableName = InventoryCacheService.GetRetainerItemVariableName(config.Id);
            
            // Get player data
            batchData.TryGetValue(variableName, out var playerPoints);
            
            // Get retainer data if provided
            List<(ulong characterId, DateTime timestamp, long value)>? retainerPoints = null;
            retainerBatchData?.TryGetValue(retainerVariableName, out retainerPoints);
            
            // Combine all points with a source flag
            var allPoints = new List<(ulong characterId, DateTime timestamp, long value, bool isRetainer)>();
            
            if (playerPoints != null)
            {
                foreach (var p in playerPoints)
                    allPoints.Add((p.characterId, p.timestamp, p.value, false));
            }
            
            if (retainerPoints != null)
            {
                foreach (var p in retainerPoints)
                    allPoints.Add((p.characterId, p.timestamp, p.value, true));
            }
            
            if (allPoints.Count == 0)
                return result;
            
            // Group by character
            var byCharacter = allPoints.GroupBy(p => p.characterId).ToList();
            
            // Get disambiguated names for all characters
            var characterIds = byCharacter.Select(g => g.Key);
            var disambiguatedNames = _cacheService.GetDisambiguatedNames(characterIds);
            
            foreach (var charGroup in byCharacter)
            {
                var characterId = charGroup.Key;
                var characterName = disambiguatedNames.TryGetValue(characterId, out var name) 
                    ? name : GetCharacterDisplayName(characterId);
                var seriesName = $"{baseName} ({characterName})";
                
                // Group by timestamp (minute resolution), summing player and retainer values
                var samples = charGroup
                    .GroupBy(p => new DateTime(p.timestamp.Year, p.timestamp.Month, p.timestamp.Day,
                        p.timestamp.Hour, p.timestamp.Minute, 0, DateTimeKind.Utc))
                    .Select(g => (ts: g.Key, value: (float)g.Sum(p => p.value)))
                    .OrderBy(p => p.ts)
                    .ToList();
                
                if (samples.Count > 0)
                {
                    result.Add((seriesName, samples));
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemGraphTool] ExtractItemTimeSeriesPerCharacter error: {ex.Message}");
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets a display name for the provided character ID.
    /// Uses formatted name from cache service, respecting the name format setting.
    /// </summary>
    private string GetCharacterDisplayName(ulong characterId)
    {
        // Use cache service which handles display name, game name formatting, and fallbacks
        var formattedName = _cacheService.GetFormattedCharacterName(characterId);
        if (!string.IsNullOrEmpty(formattedName))
            return formattedName;

        // Try runtime lookup for currently-loaded characters (formats it)
        var runtimeName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(characterId);
        if (!string.IsNullOrEmpty(runtimeName))
            return TimeSeriesCacheService.FormatName(runtimeName, _configService.Config.CharacterNameFormat) ?? runtimeName;

        // Fallback to ID
        return $"Character {characterId}";
    }
    
    private TimeSpan? GetTimeRange()
    {
        var settings = Settings;
        return TimeRangeSelectorWidget.GetTimeSpan(settings.TimeRangeValue, settings.TimeRangeUnit);
    }
    
    protected override bool HasToolSettings => true;
    
    protected override void DrawToolSettings()
    {
        var settings = Settings;
        var settingsChanged = false;
        
        // Display Options Section
        ImGui.TextUnformatted("Display Options");
        ImGui.Separator();
        
        // Show action buttons
        var showActionButtons = settings.ShowActionButtons;
        if (ImGui.Checkbox("Show Action Buttons", ref showActionButtons))
        {
            settings.ShowActionButtons = showActionButtons;
            settingsChanged = true;
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
            settingsChanged = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Display large numbers in compact form (e.g., 10M instead of 10,000,000)");
        }
        
        // Include retainers
        var includeRetainers = settings.IncludeRetainers;
        if (ImGui.Checkbox("Include Retainers", ref includeRetainers))
        {
            settings.IncludeRetainers = includeRetainers;
            settingsChanged = true;
            _cacheIsDirty = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Include retainer inventory in item counts");
        }
        
        // Show per character
        var showPerCharacter = settings.ShowPerCharacter;
        if (ImGui.Checkbox("Show Per Character", ref showPerCharacter))
        {
            settings.ShowPerCharacter = showPerCharacter;
            settingsChanged = true;
            _cacheIsDirty = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show separate lines for each character instead of aggregating totals");
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        ImGui.TextUnformatted("Series Management");
        ImGui.Separator();
        
        if (settings.Series.Count == 0)
        {
            ImGui.TextDisabled("No series configured. Add items or currencies above.");
        }
        else
        {
            // Track which series to delete or swap (can't modify list during iteration)
            int deleteIndex = -1;
            int swapUpIndex = -1;
            int swapDownIndex = -1;
            
            for (int i = 0; i < settings.Series.Count; i++)
            {
                var seriesConfig = settings.Series[i];
                var defaultName = GetSeriesName(new ItemColumnConfig { Id = seriesConfig.Id, IsCurrency = seriesConfig.IsCurrency });
                
                ImGui.PushID(i);
                
                // Color picker using ColorEdit4 (same style as tool background settings)
                var color = seriesConfig.Color ?? new Vector4(0.5f, 0.5f, 0.5f, 1f);
                if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
                {
                    seriesConfig.Color = color;
                    _cacheIsDirty = true;
                    settingsChanged = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Series color");
                }
                
                ImGui.SameLine();
                
                // Custom name input
                var customName = seriesConfig.CustomName ?? string.Empty;
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputTextWithHint("##name", defaultName, ref customName, 64))
                {
                    seriesConfig.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName;
                    settingsChanged = true;
                }
                
                ImGui.SameLine();
                
                // Type label
                ImGui.TextDisabled(seriesConfig.IsCurrency ? "[C]" : "[I]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(seriesConfig.IsCurrency ? "Currency (historical data)" : "Item (historical data)");
                }
                
                ImGui.SameLine();
                
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
                ImGui.BeginDisabled(i == settings.Series.Count - 1);
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
                    ImGui.SetTooltip("Remove series");
                }
                
                ImGui.PopID();
            }
            
            // Process reordering and deletion after iteration
            if (swapUpIndex > 0)
            {
                var temp = settings.Series[swapUpIndex - 1];
                settings.Series[swapUpIndex - 1] = settings.Series[swapUpIndex];
                settings.Series[swapUpIndex] = temp;
                settingsChanged = true;
            }
            else if (swapDownIndex >= 0 && swapDownIndex < settings.Series.Count - 1)
            {
                var temp = settings.Series[swapDownIndex + 1];
                settings.Series[swapDownIndex + 1] = settings.Series[swapDownIndex];
                settings.Series[swapDownIndex] = temp;
                settingsChanged = true;
            }
            else if (deleteIndex >= 0)
            {
                settings.Series.RemoveAt(deleteIndex);
                _cacheIsDirty = true;
                settingsChanged = true;
            }
        }
        
        if (settingsChanged)
        {
            NotifyToolSettingsChanged();
        }
    }
    
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = _instanceSettings;
        
        // Serialize series as a list of dictionaries
        var series = settings.Series.Select(s => new Dictionary<string, object?>
        {
            ["Id"] = s.Id,
            ["CustomName"] = s.CustomName,
            ["IsCurrency"] = s.IsCurrency,
            ["Color"] = s.Color.HasValue ? new float[] { s.Color.Value.X, s.Color.Value.Y, s.Color.Value.Z, s.Color.Value.W } : null,
            ["Width"] = s.Width
        }).ToList();
        
        return new Dictionary<string, object?>
        {
            ["Series"] = series,
            ["IncludeRetainers"] = settings.IncludeRetainers,
            ["ShowPerCharacter"] = settings.ShowPerCharacter,
            ["ShowActionButtons"] = settings.ShowActionButtons,
            ["UseCompactNumbers"] = settings.UseCompactNumbers,
            
            // IGraphWidgetSettings properties
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
        
        // Import series
        if (settings.TryGetValue("Series", out var seriesObj) && seriesObj != null)
        {
            target.Series.Clear();
            
            try
            {
                // Handle Newtonsoft.Json JArray (used by ConfigManager)
                if (seriesObj is Newtonsoft.Json.Linq.JArray jArray)
                {
                    foreach (var seriesToken in jArray)
                    {
                        if (seriesToken is not Newtonsoft.Json.Linq.JObject seriesJsonObj) continue;
                        
                        var item = new ItemColumnConfig
                        {
                            Id = seriesJsonObj["Id"]?.ToObject<uint>() ?? 0,
                            CustomName = seriesJsonObj["CustomName"]?.ToObject<string>(),
                            IsCurrency = seriesJsonObj["IsCurrency"]?.ToObject<bool>() ?? false,
                            Width = seriesJsonObj["Width"]?.ToObject<float>() ?? 80f
                        };
                        
                        var colorToken = seriesJsonObj["Color"];
                        if (colorToken is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
                        {
                            item.Color = new Vector4(
                                colorArr[0].ToObject<float>(),
                                colorArr[1].ToObject<float>(),
                                colorArr[2].ToObject<float>(),
                                colorArr[3].ToObject<float>());
                        }
                        
                        target.Series.Add(item);
                    }
                }
                // Fallback: Handle System.Text.Json.JsonElement (in case it's used elsewhere)
                else if (seriesObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var seriesJson in jsonElement.EnumerateArray())
                    {
                        var item = new ItemColumnConfig
                        {
                            Id = seriesJson.TryGetProperty("Id", out var idProp) ? idProp.GetUInt32() : 0,
                            CustomName = seriesJson.TryGetProperty("CustomName", out var nameProp) && nameProp.ValueKind != System.Text.Json.JsonValueKind.Null ? nameProp.GetString() : null,
                            IsCurrency = seriesJson.TryGetProperty("IsCurrency", out var currProp) && currProp.GetBoolean(),
                            Width = seriesJson.TryGetProperty("Width", out var widthProp) ? widthProp.GetSingle() : 80f
                        };
                        
                        if (seriesJson.TryGetProperty("Color", out var colorProp) && colorProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var colorArr = colorProp.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                            if (colorArr.Length >= 4)
                                item.Color = new Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                        }
                        
                        target.Series.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[ItemGraphTool] Error importing series: {ex.Message}");
            }
        }
        
        // Import scalar settings
        target.IncludeRetainers = GetSetting(settings, "IncludeRetainers", target.IncludeRetainers);
        target.ShowPerCharacter = GetSetting(settings, "ShowPerCharacter", target.ShowPerCharacter);
        target.ShowActionButtons = GetSetting(settings, "ShowActionButtons", target.ShowActionButtons);
        target.UseCompactNumbers = GetSetting(settings, "UseCompactNumbers", target.UseCompactNumbers);
        
        // IGraphWidgetSettings properties
        target.LegendWidth = GetSetting(settings, "LegendWidth", target.LegendWidth);
        target.LegendHeightPercent = GetSetting(settings, "LegendHeightPercent", target.LegendHeightPercent);
        target.ShowLegend = GetSetting(settings, "ShowLegend", target.ShowLegend);
        target.LegendPosition = (Widgets.LegendPosition)GetSetting(settings, "LegendPosition", (int)target.LegendPosition);
        target.GraphType = (GraphType)GetSetting(settings, "GraphType", (int)target.GraphType);
        target.ShowXAxisTimestamps = GetSetting(settings, "ShowXAxisTimestamps", target.ShowXAxisTimestamps);
        target.ShowCrosshair = GetSetting(settings, "ShowCrosshair", target.ShowCrosshair);
        target.ShowGridLines = GetSetting(settings, "ShowGridLines", target.ShowGridLines);
        target.ShowCurrentPriceLine = GetSetting(settings, "ShowCurrentPriceLine", target.ShowCurrentPriceLine);
        target.ShowValueLabel = GetSetting(settings, "ShowValueLabel", target.ShowValueLabel);
        target.ValueLabelOffsetX = GetSetting(settings, "ValueLabelOffsetX", target.ValueLabelOffsetX);
        target.ValueLabelOffsetY = GetSetting(settings, "ValueLabelOffsetY", target.ValueLabelOffsetY);
        target.AutoScrollEnabled = GetSetting(settings, "AutoScrollEnabled", target.AutoScrollEnabled);
        target.AutoScrollTimeValue = GetSetting(settings, "AutoScrollTimeValue", target.AutoScrollTimeValue);
        target.AutoScrollTimeUnit = (AutoScrollTimeUnit)GetSetting(settings, "AutoScrollTimeUnit", (int)target.AutoScrollTimeUnit);
        target.AutoScrollNowPosition = GetSetting(settings, "AutoScrollNowPosition", target.AutoScrollNowPosition);
        target.ShowControlsDrawer = GetSetting(settings, "ShowControlsDrawer", target.ShowControlsDrawer);
        target.TimeRangeValue = GetSetting(settings, "TimeRangeValue", target.TimeRangeValue);
        target.TimeRangeUnit = (TimeRangeUnit)GetSetting(settings, "TimeRangeUnit", (int)target.TimeRangeUnit);
        
        _cacheIsDirty = true;
    }
    
    public override void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        base.Dispose();
    }
}
