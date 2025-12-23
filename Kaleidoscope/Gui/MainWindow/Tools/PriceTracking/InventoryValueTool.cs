using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that tracks character inventory liquid value over time.
/// Shows a time-series graph of total inventory value (items + gil).
/// </summary>
public class InventoryValueTool : ToolComponent
{
    private static readonly string[] LegendPositionNames = { "Outside (right)", "Inside Top-Left", "Inside Top-Right", "Inside Bottom-Left", "Inside Bottom-Right" };

    private readonly PriceTrackingService _priceTrackingService;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly ImplotGraphWidget _graphWidget;

    // Character selection (0 = all)
    private ulong _selectedCharacterId = 0;
    private string[] _characterNames = Array.Empty<string>();
    private ulong[] _characterIds = Array.Empty<ulong>();
    private int _selectedCharacterIndex = 0;
    
    // Caching fields for optimization
    private DateTime _lastCacheTime = DateTime.MinValue;
    private ulong _cachedCharacterId;
    private bool _cachedShowMultipleLines;
    private bool _cachedIncludeGil;
    private int _cachedTimeRangeValue;
    private TimeRangeUnit _cachedTimeRangeUnit;
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? _cachedSeriesData;
    private bool _cacheIsDirty = true;
    private const double CacheValiditySeconds = 2.0; // Refresh cache every 2 seconds max
    
    // Change detection - track DB state to avoid unnecessary reprocessing
    private long _cachedDbRecordCount;
    private long? _cachedDbMaxTimestamp;

    private InventoryValueSettings Settings => _configService.Config.InventoryValue;
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    public InventoryValueTool(
        PriceTrackingService priceTrackingService,
        SamplerService samplerService,
        ConfigurationService configService)
    {
        _priceTrackingService = priceTrackingService;
        _samplerService = samplerService;
        _configService = configService;

        Title = "Inventory Value";
        Size = new Vector2(400, 300);

        // Initialize graph widget
        _graphWidget = new ImplotGraphWidget(new ImplotGraphWidget.GraphConfig
        {
            PlotId = "inventory_value_plot",
            NoDataText = "No value history data",
            ShowValueLabel = true,
            ShowXAxisTimestamps = true,
            ShowCrosshair = true,
            ShowGridLines = true,
            ShowCurrentPriceLine = true
        });
        
        _graphWidget.OnAutoScrollSettingsChanged += OnAutoScrollSettingsChanged;
        
        RefreshCharacterList();
    }
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, AutoScrollTimeUnit timeUnit, float nowPosition)
    {
        var settings = Settings;
        settings.AutoScrollEnabled = enabled;
        settings.AutoScrollTimeValue = timeValue;
        settings.AutoScrollTimeUnit = timeUnit;
        settings.AutoScrollNowPosition = nowPosition;
        _configService.Save();
        _cacheIsDirty = true;
    }
    
    /// <summary>
    /// Checks if the cache needs to be refreshed.
    /// </summary>
    private bool NeedsCacheRefresh()
    {
        if (_cacheIsDirty) return true;
        
        var settings = Settings;
        
        // Check if settings changed
        if (_cachedCharacterId != _selectedCharacterId ||
            _cachedShowMultipleLines != settings.ShowMultipleLines ||
            _cachedIncludeGil != settings.IncludeGil ||
            _cachedTimeRangeValue != settings.TimeRangeValue ||
            _cachedTimeRangeUnit != settings.TimeRangeUnit)
        {
            return true;
        }
        
        // Check if cache is stale (time-based)
        var elapsed = (DateTime.UtcNow - _lastCacheTime).TotalSeconds;
        if (elapsed < CacheValiditySeconds)
            return false; // Cache is still fresh time-wise
        
        // Time-based cache expired - check if DB actually changed to avoid reprocessing
        var characterIdForStats = _selectedCharacterId == 0 ? (ulong?)null : _selectedCharacterId;
        var (recordCount, maxTimestamp) = DbService.GetInventoryValueHistoryStats(characterIdForStats);
        
        if (recordCount == _cachedDbRecordCount && maxTimestamp == _cachedDbMaxTimestamp)
        {
            // DB hasn't changed - just update last cache time and skip full refresh
            _lastCacheTime = DateTime.UtcNow;
            return false;
        }
        
        return true;
    }

    /// <summary>
    /// Gets a display name for the provided character ID.
    /// Checks database first, then runtime lookup, then falls back to ID.
    /// </summary>
    private string GetCharacterDisplayName(ulong characterId)
    {
        // Try database first (most reliable for historical data)
        var storedName = DbService.GetCharacterName(characterId);
        if (!string.IsNullOrEmpty(storedName))
            return storedName;

        // Try runtime lookup for currently-loaded characters
        var runtimeName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(characterId);
        if (!string.IsNullOrEmpty(runtimeName))
            return runtimeName;

        // Fallback to ID
        return $"Character {characterId}";
    }

    private void RefreshCharacterList()
    {
        try
        {
            var chars = DbService.GetAllCharacterNames()
                .Select(c => (c.characterId, c.name))
                .DistinctBy(c => c.characterId)
                .OrderBy(c => c.name)
                .ToList();

            // Include "All Characters" option
            _characterNames = new string[chars.Count + 1];
            _characterIds = new ulong[chars.Count + 1];

            _characterNames[0] = "All Characters";
            _characterIds[0] = 0;

            for (int i = 0; i < chars.Count; i++)
            {
                _characterNames[i + 1] = chars[i].name ?? $"Character {chars[i].characterId}";
                _characterIds[i + 1] = chars[i].characterId;
            }

            // Update selected index
            var idx = Array.IndexOf(_characterIds, _selectedCharacterId);
            _selectedCharacterIndex = idx >= 0 ? idx : 0;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[InventoryValueTool] Error refreshing characters: {ex.Message}");
        }
    }

    public override void DrawContent()
    {
        try
        {
            // Character selector
            DrawCharacterSelector();

            // Graph
            using (ProfilerService.BeginStaticChildScope("DrawGraph"))
            {
                DrawGraph();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[InventoryValueTool] Draw error: {ex.Message}");
        }
    }

    private void DrawCharacterSelector()
    {
        if (_characterNames.Length == 0)
        {
            ImGui.TextDisabled("No character data available");
            return;
        }

        if (ImGui.Combo("##CharSelector", ref _selectedCharacterIndex, _characterNames, _characterNames.Length))
        {
            _selectedCharacterId = _characterIds[_selectedCharacterIndex];
            _cacheIsDirty = true;
        }
    }

    private void DrawGraph()
    {
        var settings = Settings;

        // Update graph widget display options from settings
        _graphWidget.UpdateDisplayOptions(
            showValueLabel: true,
            legendWidth: settings.LegendWidth,
            showLegend: settings.ShowLegend,
            graphType: settings.GraphType,
            showXAxisTimestamps: true,
            showCrosshair: true,
            showGridLines: true,
            showCurrentPriceLine: true,
            legendPosition: settings.LegendPosition,
            legendHeightPercent: settings.LegendHeightPercent,
            autoScrollEnabled: settings.AutoScrollEnabled,
            autoScrollTimeValue: settings.AutoScrollTimeValue,
            autoScrollTimeUnit: settings.AutoScrollTimeUnit,
            autoScrollNowPosition: settings.AutoScrollNowPosition,
            showControlsDrawer: settings.ShowControlsDrawer);

        // Refresh cache if needed
        if (NeedsCacheRefresh())
        {
            using (ProfilerService.BeginStaticChildScope("RefreshCachedData"))
            {
                RefreshCachedData(settings);
            }
        }
        
        // Draw from cache
        if (_cachedSeriesData != null && _cachedSeriesData.Count > 0)
        {
            _graphWidget.DrawMultipleSeries(_cachedSeriesData);
        }
        else
        {
            ImGui.TextDisabled("No value history data");
        }
    }
    
    /// <summary>
    /// Refreshes the cached data from the database.
    /// </summary>
    private void RefreshCachedData(InventoryValueSettings settings)
    {
        // Update cache tracking
        _lastCacheTime = DateTime.UtcNow;
        _cachedCharacterId = _selectedCharacterId;
        _cachedShowMultipleLines = settings.ShowMultipleLines;
        _cachedIncludeGil = settings.IncludeGil;
        _cachedTimeRangeValue = settings.TimeRangeValue;
        _cachedTimeRangeUnit = settings.TimeRangeUnit;
        _cacheIsDirty = false;
        
        // Update DB stats cache for change detection
        var characterIdForStats = _selectedCharacterId == 0 ? (ulong?)null : _selectedCharacterId;
        (_cachedDbRecordCount, _cachedDbMaxTimestamp) = DbService.GetInventoryValueHistoryStats(characterIdForStats);
        
        // Get time range
        var timeRange = GetTimeRange();
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : (DateTime?)null;

        if (_selectedCharacterId == 0 && settings.ShowMultipleLines)
        {
            // Multi-character mode - show each character as a separate line
            var allData = DbService.GetAllInventoryValueHistory(startTime);
            
            // Group data by character using direct dictionary iteration to avoid LINQ overhead
            var perCharacterData = new Dictionary<ulong, List<(DateTime ts, float value)>>();
            foreach (var entry in allData)
            {
                if (!perCharacterData.TryGetValue(entry.CharacterId, out var list))
                {
                    list = new List<(DateTime ts, float value)>();
                    perCharacterData[entry.CharacterId] = list;
                }
                
                var value = settings.IncludeGil ? entry.TotalValue : entry.ItemValue;
                list.Add((entry.Timestamp, value));
            }
            
            // Convert to the format expected by ImplotGraphWidget - preallocate list capacity
            var seriesList = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>(perCharacterData.Count);
            foreach (var kvp in perCharacterData)
            {
                seriesList.Add((GetCharacterDisplayName(kvp.Key), kvp.Value));
            }
            _cachedSeriesData = seriesList;
        }
        else
        {
            // Single line mode (either single character or all aggregated)
            List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> data;
            
            if (_selectedCharacterId == 0)
            {
                // All characters combined - use SQL aggregation instead of LINQ GroupBy
                data = DbService.GetAggregatedInventoryValueHistory(startTime);
            }
            else
            {
                // Single character - pass startTime to DB query directly
                data = DbService.GetInventoryValueHistory(_selectedCharacterId, startTime);
            }
            
            // Convert to timestamped series format for the widget - preallocate capacity
            var samples = new List<(DateTime ts, float value)>(data.Count);
            foreach (var d in data)
            {
                samples.Add((d.Timestamp, settings.IncludeGil ? d.TotalValue : d.ItemValue));
            }
            
            if (samples.Count > 0)
            {
                var seriesName = _selectedCharacterId == 0 ? "Total Value" : GetCharacterDisplayName(_selectedCharacterId);
                _cachedSeriesData = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>(1)
                {
                    (seriesName, samples)
                };
            }
            else
            {
                _cachedSeriesData = null;
            }
        }
    }

    private TimeSpan? GetTimeRange()
    {
        var settings = Settings;
        return TimeRangeSelectorWidget.GetTimeSpan(settings.TimeRangeValue, settings.TimeRangeUnit);
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            var settings = Settings;
            var settingsChanged = false;

            ImGui.TextUnformatted("Inventory Value Settings");
            ImGui.Separator();

            var includeRetainers = settings.IncludeRetainers;
            if (ImGui.Checkbox("Include retainer inventories", ref includeRetainers))
            {
                settings.IncludeRetainers = includeRetainers;
                settingsChanged = true;
            }
            ShowSettingTooltip("Include items from retainer inventories in the value calculation.", "On");

            var includeGil = settings.IncludeGil;
            if (ImGui.Checkbox("Include gil", ref includeGil))
            {
                settings.IncludeGil = includeGil;
                settingsChanged = true;
            }
            ShowSettingTooltip("Include character and retainer gil in the total value.", "On");

            var showMultipleLines = settings.ShowMultipleLines;
            if (ImGui.Checkbox("Show multiple lines (per character)", ref showMultipleLines))
            {
                settings.ShowMultipleLines = showMultipleLines;
                settingsChanged = true;
            }
            ShowSettingTooltip("When viewing 'All Characters', show a separate line for each character.", "On");

            if (showMultipleLines)
            {
                var showLegend = settings.ShowLegend;
                if (ImGui.Checkbox("Show legend", ref showLegend))
                {
                    settings.ShowLegend = showLegend;
                    settingsChanged = true;
                }
                ShowSettingTooltip("Show a legend panel on the right side of the graph.", "On");

                if (showLegend)
                {
                    var legendPosition = (int)settings.LegendPosition;
                    if (ImGui.Combo("Legend position", ref legendPosition, LegendPositionNames, LegendPositionNames.Length))
                    {
                        settings.LegendPosition = (LegendPosition)legendPosition;
                        settingsChanged = true;
                    }
                    ShowSettingTooltip("Where to display the legend: outside the graph or inside at a corner.", "Outside (right)");

                    if (settings.LegendPosition == LegendPosition.Outside)
                    {
                        var legendWidth = settings.LegendWidth;
                        if (ImGui.SliderFloat("Legend width", ref legendWidth, 60f, 250f, "%.0f px"))
                        {
                            settings.LegendWidth = legendWidth;
                            settingsChanged = true;
                        }
                        ShowSettingTooltip("Width of the scrollable legend panel.", "140");
                    }
                    else
                    {
                        var legendHeight = settings.LegendHeightPercent;
                        if (ImGui.SliderFloat("Legend height", ref legendHeight, 10f, 80f, "%.0f %%"))
                        {
                            settings.LegendHeightPercent = legendHeight;
                            settingsChanged = true;
                        }
                        ShowSettingTooltip("Maximum height of the inside legend as a percentage of the graph height.", "25%");
                    }
                }
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Graph Settings");
            ImGui.Separator();

            var graphType = settings.GraphType;
            if (GraphTypeSelectorWidget.Draw("Graph type", ref graphType))
            {
                settings.GraphType = graphType;
                settingsChanged = true;
            }
            ShowSettingTooltip("Visual style for the graph.", "Area");

            ImGui.Spacing();
            ImGui.TextUnformatted("Time Range");
            ImGui.Separator();

            var timeRangeValue = settings.TimeRangeValue;
            var timeRangeUnit = settings.TimeRangeUnit;
            if (TimeRangeSelectorWidget.DrawVertical(ref timeRangeValue, ref timeRangeUnit))
            {
                settings.TimeRangeValue = timeRangeValue;
                settings.TimeRangeUnit = timeRangeUnit;
                settingsChanged = true;
            }
            
            if (settingsChanged)
            {
                _cacheIsDirty = true;
                _configService.Save();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[InventoryValueTool] Settings error: {ex.Message}");
        }
    }

    public override void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
    }
}
