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
    private readonly ItemPickerWidget? _itemPicker;
    
    // Cached data for graph rendering
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? _cachedSeriesData;
    private DateTime _lastRefresh = DateTime.MinValue;
    private volatile bool _cacheIsDirty = true;
    
    // Cache tracking for change detection
    private int _cachedSeriesCount;
    private int _cachedTimeRangeValue;
    private TimeRangeUnit _cachedTimeRangeUnit;
    private bool _cachedIncludeRetainers;
    private bool _cachedShowPerCharacter;
    private const double CacheValiditySeconds = 2.0;
    
    private ItemGraphSettings Settings => _configService.Config.ItemGraph;
    private KaleidoscopeDbService DbService => _samplerService.DbService;
    
    public ItemGraphTool(
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null)
    {
        _samplerService = samplerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _trackedDataRegistry = trackedDataRegistry;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        
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
        
        // Bind graph widget to settings for automatic synchronization
        _graphWidget.BindSettings(
            Settings,
            onSettingsChanged: () =>
            {
                _configService.Save();
                _cacheIsDirty = true;
            },
            settingsName: "Graph Settings",
            showLegendSettings: true);
        
        // Register graph widget for automatic settings drawing
        RegisterSettingsProvider(_graphWidget);
        
        _graphWidget.OnAutoScrollSettingsChanged += OnAutoScrollSettingsChanged;
        
        // Create item picker if we have the required services
        if (_dataManager != null && _itemDataService != null)
        {
            _itemPicker = new ItemPickerWidget(_dataManager, _itemDataService);
        }
    }
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, AutoScrollTimeUnit timeUnit, float nowPosition)
    {
        var settings = Settings;
        settings.AutoScrollEnabled = enabled;
        settings.AutoScrollTimeValue = timeValue;
        settings.AutoScrollTimeUnit = timeUnit;
        settings.AutoScrollNowPosition = nowPosition;
        _configService.Save();
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
            _cachedShowPerCharacter != settings.ShowPerCharacter)
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
            
            if (_itemPicker != null)
            {
                if (_itemPicker.Draw("##ItemToAdd", marketableOnly: false, width: 250))
                {
                    // Item selected
                    if (_itemPicker.SelectedItemId.HasValue)
                    {
                        AddSeries(_itemPicker.SelectedItemId.Value, isCurrency: false);
                        _itemPicker.ClearSelection();
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
        _configService.Save();
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
        
        // Batch fetch all item data in a single query
        var itemData = DbService.GetAllPointsBatch("Item_", startTime);
        
        var seriesList = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        
        if (settings.ShowPerCharacter)
        {
            // Per-character mode: show each character as a separate line for each tracked item/currency
            foreach (var seriesConfig in series)
            {
                var baseName = GetSeriesName(seriesConfig);
                
                if (seriesConfig.IsCurrency)
                {
                    // Get currency data per character
                    var perCharacterSeries = GetCurrencyTimeSeriesPerCharacter(seriesConfig, startTime, baseName);
                    seriesList.AddRange(perCharacterSeries);
                }
                else
                {
                    // Get item data per character from the pre-fetched batch
                    var perCharacterSeries = ExtractItemTimeSeriesPerCharacter(seriesConfig, itemData, baseName);
                    seriesList.AddRange(perCharacterSeries);
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
                    // Get item data from the pre-fetched batch
                    samples = ExtractItemTimeSeries(seriesConfig, itemData, settings.IncludeRetainers);
                }
                
                if (samples.Count > 0)
                {
                    seriesList.Add((seriesName, samples));
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
    /// </summary>
    private List<(DateTime ts, float value)> ExtractItemTimeSeries(
        ItemColumnConfig config, 
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> batchData,
        bool includeRetainers)
    {
        try
        {
            var variableName = InventoryCacheService.GetItemVariableName(config.Id);
            
            if (batchData.TryGetValue(variableName, out var points) && points.Count > 0)
            {
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
            
            // Fallback: No historical data yet, show current value as single point
            // This helps users see their current inventory even before time-series builds up
            return GetItemCurrentValue(config, includeRetainers);
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
            var byCharacter = points.GroupBy(p => p.characterId);
            
            foreach (var charGroup in byCharacter)
            {
                var characterId = charGroup.Key;
                var characterName = DbService.GetCharacterName(characterId) ?? $"Character {characterId}";
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
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> ExtractItemTimeSeriesPerCharacter(
        ItemColumnConfig config,
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> batchData,
        string baseName)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        
        try
        {
            var variableName = InventoryCacheService.GetItemVariableName(config.Id);
            
            if (!batchData.TryGetValue(variableName, out var points) || points.Count == 0)
                return result;
            
            // Group by character
            var byCharacter = points.GroupBy(p => p.characterId);
            
            foreach (var charGroup in byCharacter)
            {
                var characterId = charGroup.Key;
                var characterName = DbService.GetCharacterName(characterId) ?? $"Character {characterId}";
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
            LogService.Debug($"[ItemGraphTool] ExtractItemTimeSeriesPerCharacter error: {ex.Message}");
        }
        
        return result;
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
                
                // Color picker (small button)
                var color = seriesConfig.Color ?? new Vector4(1f, 1f, 1f, 1f);
                var hasColor = seriesConfig.Color.HasValue;
                
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
                        seriesConfig.Color = null;
                        settingsChanged = true;
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
                        seriesConfig.Color = color;
                        settingsChanged = true;
                    }
                    ImGui.EndPopup();
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(hasColor ? "Click to remove color" : "Click to set color");
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
        
        ImGui.Spacing();
        
        if (settings.Series.Count > 0 && ImGui.Button("Clear All"))
        {
            settings.Series.Clear();
            _cacheIsDirty = true;
            settingsChanged = true;
        }
        
        if (settingsChanged)
        {
            _configService.Save();
        }
    }
    
    public override void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        base.Dispose();
    }
}
