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
/// Uses automatic settings binding with ImplotGraphWidget.
/// </summary>
public class InventoryValueTool : ToolComponent
{
    private readonly PriceTrackingService _priceTrackingService;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly TimeSeriesCacheService? _cacheService;
    private readonly ImplotGraphWidget _graphWidget;
    private readonly InventoryValueSettings _instanceSettings;

    // Character selection (0 = all)
    private ulong _selectedCharacterId = 0;
    private string[] _characterNames = Array.Empty<string>();
    private ulong[] _characterIds = Array.Empty<ulong>();
    private int _selectedCharacterIndex = 0;
    
    // Caching fields for optimization (local fallback when cache service unavailable)
    private DateTime _lastCacheTime = DateTime.MinValue;
    private ulong _cachedCharacterId;
    private bool _cachedShowMultipleLines;
    private bool _cachedIncludeGil;
    private int _cachedTimeRangeValue;
    private TimeUnit _cachedTimeRangeUnit;
    private CharacterNameFormat _cachedNameFormat;
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? _cachedSeriesData;
    private bool _cacheIsDirty = true;
    private const double CacheValiditySeconds = 30.0; // Check for DB changes every 30 seconds
    
    // Change detection - track DB state to avoid unnecessary reprocessing
    private long _cachedDbRecordCount;
    private long? _cachedDbMaxTimestamp;

    private InventoryValueSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    public InventoryValueTool(
        PriceTrackingService priceTrackingService,
        SamplerService samplerService,
        ConfigurationService configService)
    {
        _priceTrackingService = priceTrackingService;
        _samplerService = samplerService;
        _configService = configService;
        _cacheService = samplerService.CacheService;
        _instanceSettings = new InventoryValueSettings();

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
        
        // Bind graph widget to settings for automatic synchronization
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
        
        // Subscribe to inventory value history changes (e.g., when sale records are deleted)
        _samplerService.OnInventoryValueHistoryChanged += OnInventoryValueHistoryChanged;
        
        // Subscribe to cache updates from background thread
        if (_cacheService != null)
        {
            _cacheService.OnInventoryValueCacheInvalidated += OnCacheInvalidated;
        }
        
        RefreshCharacterList();
    }
    
    private void OnCacheInvalidated()
    {
        // Cache was invalidated - need to refresh on next draw
        _cacheIsDirty = true;
    }
    
    private void OnInventoryValueHistoryChanged()
    {
        _cacheIsDirty = true;
        // Also invalidate the cache service's inventory value cache
        _cacheService?.InvalidateInventoryValueCache();
    }
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, TimeUnit timeUnit, float nowPosition)
    {
        var settings = Settings;
        settings.AutoScrollEnabled = enabled;
        settings.AutoScrollTimeValue = timeValue;
        settings.AutoScrollTimeUnit = timeUnit;
        settings.AutoScrollNowPosition = nowPosition;
        NotifyToolSettingsChanged();
        _cacheIsDirty = true;
    }
    
    /// <summary>
    /// Checks if the cache needs to be refreshed.
    /// </summary>
    private bool NeedsCacheRefresh()
    {
        // Dirty flag is set when data changes via event - this is the primary refresh trigger
        if (_cacheIsDirty) return true;
        
        // No cached data yet - need initial load
        if (_cachedSeriesData == null) return true;
        
        var settings = Settings;
        
        // Check if settings changed
        if (_cachedCharacterId != _selectedCharacterId ||
            _cachedShowMultipleLines != settings.ShowMultipleLines ||
            _cachedIncludeGil != settings.IncludeGil ||
            _cachedTimeRangeValue != settings.TimeRangeValue ||
            _cachedTimeRangeUnit != settings.TimeRangeUnit ||
            _cachedNameFormat != _configService.Config.CharacterNameFormat)
        {
            return true;
        }
        
        // Check if cache is stale (time-based) - only do stats check periodically
        var elapsed = (DateTime.UtcNow - _lastCacheTime).TotalSeconds;
        if (elapsed < CacheValiditySeconds)
            return false; // Cache is still fresh time-wise
        
        // Time-based cache expired - check cached stats (NO DB query - uses in-memory cache only)
        long recordCount;
        long? maxTimestamp;
        using (ProfilerService.BeginStaticChildScope("CachedStatsCheck"))
        {
            // Use cache-only stats - this NEVER hits DB
            (recordCount, maxTimestamp) = _cacheService?.GetInventoryValueStatsFromCache() ?? (0, null);
        }
        
        if (recordCount == _cachedDbRecordCount && maxTimestamp == _cachedDbMaxTimestamp)
        {
            // Stats haven't changed - just update last cache time and skip full refresh
            _lastCacheTime = DateTime.UtcNow;
            return false;
        }
        
        return true;
    }

    /// <summary>
    /// Gets a display name for the provided character ID.
    /// Uses formatted name from cache service, respecting the name format setting.
    /// </summary>
    private string GetCharacterDisplayName(ulong characterId)
    {
        // Use cache service which handles display name, game name formatting, and fallbacks
        var formattedName = _cacheService?.GetFormattedCharacterName(characterId);
        if (!string.IsNullOrEmpty(formattedName))
            return formattedName;

        // Try runtime lookup for currently-loaded characters (formats it)
        var runtimeName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(characterId);
        if (!string.IsNullOrEmpty(runtimeName))
            return TimeSeriesCacheService.FormatName(runtimeName, _configService.Config.CharacterNameFormat) ?? runtimeName;

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
        // Sync graph widget from bound settings (in case settings changed externally)
        _graphWidget.SyncFromBoundSettings();

        // Refresh cache if needed
        bool needsRefresh;
        using (ProfilerService.BeginStaticChildScope("NeedsCacheRefresh"))
        {
            needsRefresh = NeedsCacheRefresh();
        }
        
        if (needsRefresh)
        {
            using (ProfilerService.BeginStaticChildScope("RefreshCachedData"))
            {
                RefreshCachedData(Settings);
            }
        }
        
        // Draw from cache
        if (_cachedSeriesData != null && _cachedSeriesData.Count > 0)
        {
            using (ProfilerService.BeginStaticChildScope("DrawMultipleSeries"))
            {
                _graphWidget.DrawMultipleSeries(_cachedSeriesData);
            }
        }
        else
        {
            ImGui.TextDisabled("No value history data");
        }
    }
    
    /// <summary>
    /// Refreshes the cached data from the in-memory cache.
    /// This method NEVER hits the database - all DB access is done by background thread.
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
        _cachedNameFormat = _configService.Config.CharacterNameFormat;
        _cacheIsDirty = false;
        
        // Update stats cache for change detection (NO DB query - uses in-memory cache only)
        using (ProfilerService.BeginStaticChildScope("GetCacheStats"))
        {
            (_cachedDbRecordCount, _cachedDbMaxTimestamp) = _cacheService?.GetInventoryValueStatsFromCache() ?? (0, null);
        }
        
        // Get time range
        var timeRange = GetTimeRange();
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : (DateTime?)null;

        // Get data from in-memory cache only - NEVER hit DB on main thread
        List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)>? allData = null;
        
        using (ProfilerService.BeginStaticChildScope("GetFromCache"))
        {
            allData = _cacheService?.GetInventoryValueHistoryFromCache();
        }
        
        // If cache is empty, we can't display anything - background thread will populate it
        if (allData == null)
        {
            _cachedSeriesData = null;
            return;
        }

        if (_selectedCharacterId == 0 && settings.ShowMultipleLines)
        {
            // Multi-character mode - show each character as a separate line
            using (ProfilerService.BeginStaticChildScope("ProcessData"))
            {
                // Group data by character using direct dictionary iteration to avoid LINQ overhead
                var perCharacterData = new Dictionary<ulong, List<(DateTime ts, float value)>>();
                foreach (var entry in allData)
                {
                    // Apply time filter (cache stores all data)
                    if (startTime.HasValue && entry.Timestamp < startTime.Value)
                        continue;
                        
                    if (!perCharacterData.TryGetValue(entry.CharacterId, out var list))
                    {
                        list = new List<(DateTime ts, float value)>();
                        perCharacterData[entry.CharacterId] = list;
                    }
                    
                    var value = settings.IncludeGil ? entry.TotalValue : entry.ItemValue;
                    list.Add((entry.Timestamp, value));
                }
                
                // Get disambiguated names for all characters in the data
                var disambiguatedNames = _cacheService?.GetDisambiguatedNames(perCharacterData.Keys) 
                    ?? perCharacterData.Keys.ToDictionary(k => k, k => GetCharacterDisplayName(k));
                
                // Convert to the format expected by ImplotGraphWidget - preallocate list capacity
                var seriesList = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>(perCharacterData.Count);
                foreach (var kvp in perCharacterData)
                {
                    seriesList.Add((disambiguatedNames[kvp.Key], kvp.Value));
                }
                _cachedSeriesData = seriesList;
            }
        }
        else
        {
            // Single line mode (either single character or all aggregated)
            List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> data;
            
            if (_selectedCharacterId == 0)
            {
                // All characters combined - aggregate in memory from cache
                data = AggregateFromCache(allData, startTime);
            }
            else
            {
                // Single character - filter from cache
                data = FilterSingleCharacterFromCache(allData, _selectedCharacterId, startTime);
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
    
    /// <summary>
    /// Filters cached data for a single character.
    /// </summary>
    private static List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> FilterSingleCharacterFromCache(
        List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> cachedData,
        ulong characterId,
        DateTime? startTime)
    {
        var result = new List<(DateTime, long, long, long)>();
        
        foreach (var entry in cachedData)
        {
            if (entry.CharacterId != characterId)
                continue;
            if (startTime.HasValue && entry.Timestamp < startTime.Value)
                continue;
                
            result.Add((entry.Timestamp, entry.TotalValue, entry.GilValue, entry.ItemValue));
        }
        
        // Sort by timestamp
        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }

    /// <summary>
    /// Aggregates cached inventory value data by timestamp.
    /// </summary>
    private static List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> AggregateFromCache(
        List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> cachedData,
        DateTime? startTime)
    {
        // Group by timestamp and sum values
        var grouped = new SortedDictionary<DateTime, (long total, long gil, long item)>();
        
        foreach (var entry in cachedData)
        {
            if (startTime.HasValue && entry.Timestamp < startTime.Value)
                continue;
                
            if (grouped.TryGetValue(entry.Timestamp, out var existing))
            {
                grouped[entry.Timestamp] = (
                    existing.total + entry.TotalValue,
                    existing.gil + entry.GilValue,
                    existing.item + entry.ItemValue);
            }
            else
            {
                grouped[entry.Timestamp] = (entry.TotalValue, entry.GilValue, entry.ItemValue);
            }
        }
        
        var result = new List<(DateTime, long, long, long)>(grouped.Count);
        foreach (var kvp in grouped)
        {
            result.Add((kvp.Key, kvp.Value.total, kvp.Value.gil, kvp.Value.item));
        }
        return result;
    }

    private TimeSpan? GetTimeRange()
    {
        var settings = Settings;
        return TimeRangeSelectorWidget.GetTimeSpan(settings.TimeRangeValue, settings.TimeRangeUnit);
    }

    /// <summary>
    /// Indicates this tool has its own settings in addition to component settings.
    /// </summary>
    protected override bool HasToolSettings => true;

    /// <summary>
    /// Draws tool-specific settings. Graph settings are automatically drawn via the registered graph widget.
    /// </summary>
    protected override void DrawToolSettings()
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
            
            if (settingsChanged)
            {
                _cacheIsDirty = true;
                NotifyToolSettingsChanged();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[InventoryValueTool] Settings error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public override Dictionary<string, object> ExportToolSettings()
    {
        return new Dictionary<string, object>
        {
            ["ShowMultipleLines"] = _instanceSettings.ShowMultipleLines,
            ["IncludeRetainers"] = _instanceSettings.IncludeRetainers,
            ["IncludeGil"] = _instanceSettings.IncludeGil,
            ["TimeRangeValue"] = _instanceSettings.TimeRangeValue,
            ["TimeRangeUnit"] = (int)_instanceSettings.TimeRangeUnit,
            ["ShowLegend"] = _instanceSettings.ShowLegend,
            ["LegendWidth"] = _instanceSettings.LegendWidth,
            ["LegendPosition"] = (int)_instanceSettings.LegendPosition,
            ["LegendHeightPercent"] = _instanceSettings.LegendHeightPercent,
            ["GraphType"] = (int)_instanceSettings.GraphType,
            ["ShowXAxisTimestamps"] = _instanceSettings.ShowXAxisTimestamps,
            ["ShowCrosshair"] = _instanceSettings.ShowCrosshair,
            ["ShowGridLines"] = _instanceSettings.ShowGridLines,
            ["ShowCurrentPriceLine"] = _instanceSettings.ShowCurrentPriceLine,
            ["ShowValueLabel"] = _instanceSettings.ShowValueLabel,
            ["ValueLabelOffsetX"] = _instanceSettings.ValueLabelOffsetX,
            ["ValueLabelOffsetY"] = _instanceSettings.ValueLabelOffsetY,
            ["AutoScrollEnabled"] = _instanceSettings.AutoScrollEnabled,
            ["AutoScrollTimeValue"] = _instanceSettings.AutoScrollTimeValue,
            ["AutoScrollTimeUnit"] = (int)_instanceSettings.AutoScrollTimeUnit,
            ["AutoScrollNowPosition"] = _instanceSettings.AutoScrollNowPosition,
            ["ShowControlsDrawer"] = _instanceSettings.ShowControlsDrawer,
        };
    }

    /// <inheritdoc />
    public override void ImportToolSettings(Dictionary<string, object> settings)
    {
        if (settings.TryGetValue("ShowMultipleLines", out var showMultipleLines))
            _instanceSettings.ShowMultipleLines = Convert.ToBoolean(showMultipleLines);
        if (settings.TryGetValue("IncludeRetainers", out var includeRetainers))
            _instanceSettings.IncludeRetainers = Convert.ToBoolean(includeRetainers);
        if (settings.TryGetValue("IncludeGil", out var includeGil))
            _instanceSettings.IncludeGil = Convert.ToBoolean(includeGil);
        if (settings.TryGetValue("TimeRangeValue", out var timeRangeValue))
            _instanceSettings.TimeRangeValue = Convert.ToInt32(timeRangeValue);
        if (settings.TryGetValue("TimeRangeUnit", out var timeRangeUnit))
            _instanceSettings.TimeRangeUnit = (TimeUnit)Convert.ToInt32(timeRangeUnit);
        if (settings.TryGetValue("ShowLegend", out var showLegend))
            _instanceSettings.ShowLegend = Convert.ToBoolean(showLegend);
        if (settings.TryGetValue("LegendWidth", out var legendWidth))
            _instanceSettings.LegendWidth = Convert.ToSingle(legendWidth);
        if (settings.TryGetValue("LegendPosition", out var legendPosition))
            _instanceSettings.LegendPosition = (LegendPosition)Convert.ToInt32(legendPosition);
        if (settings.TryGetValue("LegendHeightPercent", out var legendHeightPercent))
            _instanceSettings.LegendHeightPercent = Convert.ToSingle(legendHeightPercent);
        if (settings.TryGetValue("GraphType", out var graphType))
            _instanceSettings.GraphType = (GraphType)Convert.ToInt32(graphType);
        if (settings.TryGetValue("ShowXAxisTimestamps", out var showXAxisTimestamps))
            _instanceSettings.ShowXAxisTimestamps = Convert.ToBoolean(showXAxisTimestamps);
        if (settings.TryGetValue("ShowCrosshair", out var showCrosshair))
            _instanceSettings.ShowCrosshair = Convert.ToBoolean(showCrosshair);
        if (settings.TryGetValue("ShowGridLines", out var showGridLines))
            _instanceSettings.ShowGridLines = Convert.ToBoolean(showGridLines);
        if (settings.TryGetValue("ShowCurrentPriceLine", out var showCurrentPriceLine))
            _instanceSettings.ShowCurrentPriceLine = Convert.ToBoolean(showCurrentPriceLine);
        if (settings.TryGetValue("ShowValueLabel", out var showValueLabel))
            _instanceSettings.ShowValueLabel = Convert.ToBoolean(showValueLabel);
        if (settings.TryGetValue("ValueLabelOffsetX", out var valueLabelOffsetX))
            _instanceSettings.ValueLabelOffsetX = Convert.ToSingle(valueLabelOffsetX);
        if (settings.TryGetValue("ValueLabelOffsetY", out var valueLabelOffsetY))
            _instanceSettings.ValueLabelOffsetY = Convert.ToSingle(valueLabelOffsetY);
        if (settings.TryGetValue("AutoScrollEnabled", out var autoScrollEnabled))
            _instanceSettings.AutoScrollEnabled = Convert.ToBoolean(autoScrollEnabled);
        if (settings.TryGetValue("AutoScrollTimeValue", out var autoScrollTimeValue))
            _instanceSettings.AutoScrollTimeValue = Convert.ToInt32(autoScrollTimeValue);
        if (settings.TryGetValue("AutoScrollTimeUnit", out var autoScrollTimeUnit))
            _instanceSettings.AutoScrollTimeUnit = (TimeUnit)Convert.ToInt32(autoScrollTimeUnit);
        if (settings.TryGetValue("AutoScrollNowPosition", out var autoScrollNowPosition))
            _instanceSettings.AutoScrollNowPosition = Convert.ToSingle(autoScrollNowPosition);
        if (settings.TryGetValue("ShowControlsDrawer", out var showControlsDrawer))
            _instanceSettings.ShowControlsDrawer = Convert.ToBoolean(showControlsDrawer);

        _cacheIsDirty = true;
    }

    public override void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        _samplerService.OnInventoryValueHistoryChanged -= OnInventoryValueHistoryChanged;
        if (_cacheService != null)
        {
            _cacheService.OnInventoryValueCacheInvalidated -= OnCacheInvalidated;
        }
    }
}
