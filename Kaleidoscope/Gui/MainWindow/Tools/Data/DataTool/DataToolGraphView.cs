using System.Numerics;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Universalis;
using Kaleidoscope.Gui.Widgets.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// Owns the graph view's cached series data and rendering, including series loading, grouping,
/// and color logic. Constructed by and delegated to from <see cref="DataTool"/>, which retains
/// the shared settings, data-source plumbing, and view-mode switch.
/// </summary>
internal sealed class DataToolGraphView
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly AutoRetainerService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly ItemDataService? _itemDataService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly GraphWidget _graphWidget;
    private readonly Func<bool> _hasCacheVersionChanged;
    private readonly Func<ulong, string> _getCharacterDisplayName;
    private readonly Action<string> _logDebug;

    // Graph view cached data (tuple format matching GraphWidget.RenderMultipleSeries)
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>? _cachedSeriesData;
    private List<GraphSeriesGroup>? _cachedSeriesGroups;
    private volatile bool _cacheIsDirty = true;
    private int _cachedSeriesCount;
    private int _cachedTimeRangeValue;
    private TimeUnit _cachedTimeRangeUnit;
    private bool _cachedIncludeRetainers;
    private TableGroupingMode _cachedGroupingMode;
    private bool _cachedShowRetainerBreakdownInGraph;

    // Retainer names cache (refreshed periodically)
    private Dictionary<ulong, string>? _cachedRetainerNames;
    private DateTime _lastRetainerNamesCacheRefresh = DateTime.MinValue;
    private static readonly TimeSpan RetainerNamesCacheExpiry = TimeSpan.FromMinutes(5);

    private TimeSeriesCacheService CacheService => _currencyTrackerService.CacheService;

    public DataToolGraphView(
        CurrencyTrackerService currencyTrackerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService,
        AutoRetainerService? autoRetainerService,
        PriceTrackingService? priceTrackingService,
        ItemDataService? itemDataService,
        TrackedDataRegistry? trackedDataRegistry,
        GraphWidget graphWidget,
        Func<bool> hasCacheVersionChanged,
        Func<ulong, string> getCharacterDisplayName,
        Action<string> logDebug)
    {
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        _itemDataService = itemDataService;
        _trackedDataRegistry = trackedDataRegistry;
        _graphWidget = graphWidget;
        _hasCacheVersionChanged = hasCacheVersionChanged;
        _getCharacterDisplayName = getCharacterDisplayName;
        _logDebug = logDebug;
    }

    /// <summary>Marks the cached series data as stale so it is rebuilt on the next draw.</summary>
    public void MarkDirty() => _cacheIsDirty = true;

    public void Draw(DataToolSettings settings)
    {
        using (ProfilerService.BeginStaticChildScope("GraphView"))
        {
            _graphWidget.SyncFromBoundSettings();

            if (NeedsGraphCacheRefresh(settings))
            {
                using (ProfilerService.BeginStaticChildScope("RefreshGraphData"))
                {
                    RefreshGraphData(settings);
                }

                _graphWidget.Groups = _cachedSeriesGroups;
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
                if (settings.Columns.Count == 0)
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

    private bool NeedsGraphCacheRefresh(DataToolSettings settings)
    {
        if (_cacheIsDirty) return true;

        if (_cachedSeriesCount != settings.Columns.Count) return true;
        if (_cachedTimeRangeValue != settings.TimeRangeValue) return true;
        if (_cachedTimeRangeUnit != settings.TimeRangeUnit) return true;
        if (_cachedIncludeRetainers != settings.IncludeRetainers) return true;
        if (_cachedShowRetainerBreakdownInGraph != settings.ShowRetainerBreakdownInGraph) return true;
        if (_cachedGroupingMode != settings.GroupingMode) return true;

        // Use version counters for data change detection instead of blind 5s polling
        return _hasCacheVersionChanged();
    }

    private void RefreshGraphData(DataToolSettings settings)
    {
        _cachedSeriesCount = settings.Columns.Count;
        _cachedTimeRangeValue = settings.TimeRangeValue;
        _cachedTimeRangeUnit = settings.TimeRangeUnit;
        _cachedIncludeRetainers = settings.IncludeRetainers;
        _cachedShowRetainerBreakdownInGraph = settings.ShowRetainerBreakdownInGraph;
        _cachedGroupingMode = settings.GroupingMode;
        _cacheIsDirty = false;

        var mergedIndicesWithGraph = new HashSet<int>();
        foreach (var group in settings.MergedColumnGroups.Where(g => g.ShowInGraph))
        {
            foreach (var idx in group.ColumnIndices)
            {
                mergedIndicesWithGraph.Add(idx);
            }
        }

        List<ItemColumnConfig> series;
        using (ProfilerService.BeginStaticChildScope("ApplyGroupingFilter"))
        {
            var filteredColumns = SpecialGroupingHelper.ApplySpecialGroupingFilter(settings.Columns, settings.SpecialGrouping);
            series = filteredColumns
                .Where((c, idx) => c.ShowInGraph && !mergedIndicesWithGraph.Contains(idx))
                .ToList();
        }

        var timeRange = GetTimeRange(settings);
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : (DateTime?)null;

        HashSet<ulong>? allowedCharacters = null;
        if (settings.UseCharacterFilter && settings.SelectedCharacterIds.Count > 0)
        {
            allowedCharacters = settings.SelectedCharacterIds.ToHashSet();
        }

        var seriesList = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();

        var seriesByItem = new Dictionary<string, List<string>>();
        var itemColors = new Dictionary<string, Vector4>();

        var totalItemCount = series.Count + settings.MergedColumnGroups.Count(g => g.ShowInGraph);
        var isSingleItem = totalItemCount == 1;

        using (ProfilerService.BeginStaticChildScope("LoadAllSeries"))
        {
            var itemIndex = 0;
            foreach (var seriesConfig in series)
            {
                var itemName = GetSeriesDisplayName(seriesConfig);
                var seriesData = LoadSeriesData(seriesConfig, settings, startTime, allowedCharacters, isSingleItem);
                if (seriesData != null)
                {
                    if (!isSingleItem)
                    {
                        if (!seriesByItem.ContainsKey(itemName))
                        {
                            seriesByItem[itemName] = new List<string>();
                            var color = GetEffectiveSeriesColor(seriesConfig, settings, itemIndex);
                            itemColors[itemName] = color;
                        }
                        foreach (var s in seriesData)
                        {
                            seriesByItem[itemName].Add(s.name);
                        }
                    }
                    seriesList.AddRange(seriesData);
                }
                itemIndex++;
            }

            foreach (var group in settings.MergedColumnGroups.Where(g => g.ShowInGraph))
            {
                var groupName = group.Name;
                var mergedSeriesData = LoadMergedSeriesData(group, settings, startTime, allowedCharacters, isSingleItem);
                if (mergedSeriesData != null)
                {
                    if (!isSingleItem)
                    {
                        if (!seriesByItem.ContainsKey(groupName))
                        {
                            seriesByItem[groupName] = new List<string>();
                            if (group.Color.HasValue)
                            {
                                itemColors[groupName] = group.Color.Value;
                            }
                            else
                            {
                                itemColors[groupName] = GetDefaultSeriesColor(itemIndex);
                            }
                        }
                        foreach (var s in mergedSeriesData)
                        {
                            seriesByItem[groupName].Add(s.name);
                        }
                    }
                    seriesList.AddRange(mergedSeriesData);
                }
                itemIndex++;
            }
        }

        _cachedSeriesData = seriesList.Count > 0 ? seriesList : null;

        // Build groups for the legend (only when there are multiple items with multiple series each)
        if (!isSingleItem && seriesByItem.Count > 1)
        {
            var groups = new List<GraphSeriesGroup>();
            foreach (var (itemName, seriesNames) in seriesByItem)
            {
                // Only create a group if the item has multiple series
                if (seriesNames.Count > 1)
                {
                    var color = itemColors.TryGetValue(itemName, out var c)
                        ? new Vector3(c.X, c.Y, c.Z)
                        : new Vector3(0.6f, 0.6f, 0.6f);

                    groups.Add(new GraphSeriesGroup
                    {
                        Name = itemName,
                        Color = color,
                        SeriesNames = seriesNames
                    });
                }
            }
            _cachedSeriesGroups = groups.Count > 0 ? groups : null;
        }
        else
        {
            _cachedSeriesGroups = null;
        }
    }

    private TimeSpan? GetTimeRange(DataToolSettings settings)
    {
        return TimeRangeSelectorWidget.GetTimeSpan(settings.TimeRangeValue, settings.TimeRangeUnit);
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
            var (variableName, pendingPrefix, pendingSuffix, perRetainerVariablePrefix) =
                ResolveSeriesVariableNames(seriesConfig, settings);

            IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> points;
            Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>? perRetainerPointsDict = null;
            using (ProfilerService.BeginStaticChildScope("CacheGetPoints"))
            {
                var loaded = LoadAndMergePoints(variableName, pendingPrefix, pendingSuffix, startTime);
                if (loaded == null)
                    return null;
                points = loaded;

                // If IncludeRetainers is enabled but ShowRetainerBreakdownInGraph is disabled,
                // add retainer totals to the main series (not separate)
                if (settings.IncludeRetainers && !settings.ShowRetainerBreakdownInGraph && !seriesConfig.IsCurrency)
                {
                    var retainerVariableName = $"ItemRetainer_{seriesConfig.Id}";
                    var retainerPts = LoadAndMergePoints(retainerVariableName, "ItemRetainer_", pendingSuffix, startTime);
                    if (retainerPts != null && retainerPts.Count > 0)
                        points = MergePlayerAndRetainerData(points, retainerPts.ToList());
                }

                // Fetch per-retainer data if breakdown is enabled
                if (perRetainerVariablePrefix != null)
                {
                    var itemIdSuffix = $"_{seriesConfig.Id}";
                    perRetainerPointsDict = LoadPerRetainerPoints(perRetainerVariablePrefix, itemIdSuffix, seriesConfig.Id, startTime);
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
            var color = GetEffectiveSeriesColor(seriesConfig, settings, 0);
            var groupingMode = settings.GroupingMode;

            var result = BuildGroupedSeries(points, groupingMode, seriesConfig.CustomName ?? defaultName, color, settings, isSingleItem);

            if (perRetainerPointsDict != null && perRetainerPointsDict.Count > 0)
            {
                var retainerSeriesResult = BuildPerRetainerSeries(perRetainerPointsDict, seriesConfig.Id, defaultName, settings, groupingMode, seriesConfig);
                result.AddRange(retainerSeriesResult);
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logDebug($"LoadSeriesData error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves the cache variable name and pending-sample prefix/suffix for a series column,
    /// plus the per-retainer variable prefix when the retainer breakdown is enabled.
    /// </summary>
    private static (string variableName, string? pendingPrefix, string? pendingSuffix, string? perRetainerVariablePrefix)
        ResolveSeriesVariableNames(ItemColumnConfig seriesConfig, DataToolSettings settings)
    {
        if (seriesConfig.IsCurrency)
            return (((TrackedDataType)seriesConfig.Id).ToString(), null, null, null);

        var perRetainerVariablePrefix = settings.IncludeRetainers && settings.ShowRetainerBreakdownInGraph
            ? "ItemRetainerX_"
            : null;
        return ($"Item_{seriesConfig.Id}", "Item_", $"_{seriesConfig.Id}", perRetainerVariablePrefix);
    }

    /// <summary>
    /// Normalizes a DateTime to UTC kind without rounding.
    /// </summary>
    private static DateTime NormalizeTimestamp(DateTime dt)
        => DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    /// <summary>
    /// Aggregates multiple data sources using forward-fill logic.
    /// At each timestamp, the sum includes the last known value from each source,
    /// ensuring proper aggregation even when sources are sampled at different times.
    /// </summary>
    /// <param name="points">Points tagged with a source identifier (e.g., column index or variable name).</param>
    /// <returns>Aggregated samples with forward-filled sums at each unique timestamp.</returns>
    private static List<(DateTime ts, float value)> AggregateWithForwardFill(
        IEnumerable<(string source, DateTime ts, long value)> points)
    {
        // Group by source, keeping the latest value at each timestamp
        var bySource = points
            .GroupBy(p => p.source)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(p => NormalizeTimestamp(p.ts))
                    .ToDictionary(tg => tg.Key, tg => tg.OrderByDescending(p => p.ts).First().value));

        if (bySource.Count == 0)
            return new List<(DateTime ts, float value)>();

        // Collect all unique timestamps across all sources
        var allTimestamps = bySource.Values
            .SelectMany(d => d.Keys)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        // Forward-fill: track last known value per source
        var lastValues = bySource.Keys.ToDictionary(k => k, _ => 0L);
        var result = new List<(DateTime ts, float value)>(allTimestamps.Count);

        foreach (var ts in allTimestamps)
        {
            foreach (var source in bySource.Keys)
            {
                if (bySource[source].TryGetValue(ts, out var val))
                    lastValues[source] = val;
            }

            var sum = lastValues.Values.Sum();
            result.Add((ts, sum));
        }

        return result;
    }

    /// <summary>
    /// Merges player and retainer data using forward-fill logic.
    /// This ensures that at any timestamp, we combine the latest known player value
    /// with the latest known retainer value, even if they weren't sampled at the same time.
    /// </summary>
    private static List<(ulong characterId, DateTime timestamp, long value)> MergePlayerAndRetainerData(
        IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> playerPoints,
        List<(ulong characterId, DateTime timestamp, long value)> retainerPoints)
    {
        // Group points by character ID first
        var playerByChar = playerPoints
            .GroupBy(p => p.characterId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.timestamp).ToList());

        var retainerByChar = retainerPoints
            .GroupBy(p => p.characterId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.timestamp).ToList());

        // Get all unique character IDs
        var allCharIds = playerByChar.Keys.Union(retainerByChar.Keys).ToList();

        var mergedPoints = new List<(ulong characterId, DateTime timestamp, long value)>();

        foreach (var charId in allCharIds)
        {
            var playerPts = playerByChar.GetValueOrDefault(charId) ?? new List<(ulong, DateTime, long)>();
            var retPts = retainerByChar.GetValueOrDefault(charId) ?? new List<(ulong, DateTime, long)>();

            // Collect all unique timestamps
            var allTimestamps = playerPts
                .Select(p => p.timestamp)
                .Union(retPts.Select(p => p.timestamp))
                .OrderBy(t => t)
                .Distinct()
                .ToList();

            // Build lookup for player and retainer values by timestamp
            var playerLookup = playerPts
                .GroupBy(p => p.timestamp)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.value));

            var retainerLookup = retPts
                .GroupBy(p => p.timestamp)
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

        return mergedPoints.OrderBy(p => p.timestamp).ToList();
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
            var memberColumns = group.ColumnIndices
                .Where(idx => idx >= 0 && idx < settings.Columns.Count)
                .Select(idx => settings.Columns[idx])
                .ToList();

            if (memberColumns.Count == 0)
                return null;

            // Collect all points from all member columns (tagged with source for forward-fill)
            var allPoints = new List<(string source, ulong characterId, DateTime ts, long value)>();

            foreach (var column in memberColumns)
            {
                string variableName = column.IsCurrency
                    ? ((TrackedDataType)column.Id).ToString()
                    : $"Item_{column.Id}";

                var pointsDict = CacheService.GetAllPointsBatch(variableName, startTime);
                if (pointsDict.TryGetValue(variableName, out var pts) && pts.Count > 0)
                {
                    var filtered = allowedCharacters != null
                        ? pts.Where(p => allowedCharacters.Contains(p.characterId))
                        : pts;
                    foreach (var p in filtered)
                        allPoints.Add((variableName, p.characterId, p.timestamp, p.value));
                }

                // Also include retainer data if IncludeRetainers is enabled
                if (settings.IncludeRetainers && !column.IsCurrency)
                {
                    var retainerVariableName = $"ItemRetainer_{column.Id}";
                    var retainerPointsDict = CacheService.GetAllPointsBatch(retainerVariableName, startTime);
                    if (retainerPointsDict.TryGetValue(retainerVariableName, out var retainerPts) && retainerPts.Count > 0)
                    {
                        var filtered = allowedCharacters != null
                            ? retainerPts.Where(p => allowedCharacters.Contains(p.characterId))
                            : retainerPts;
                        foreach (var p in filtered)
                            allPoints.Add((retainerVariableName, p.characterId, p.timestamp, p.value));
                    }
                }
            }

            if (allPoints.Count == 0)
                return null;

            // Use the merged group's color if set, otherwise use first member's color
            var baseColor = group.Color;
            if (!baseColor.HasValue && memberColumns.Count > 0 && memberColumns[0].Color.HasValue)
                baseColor = memberColumns[0].Color;

            var result = BuildGroupedSeriesWithAggregation(
                allPoints, settings.GroupingMode, group.Name, baseColor, settings, isSingleItem);

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logDebug($"LoadMergedSeriesData error: {ex.Message}");
            return null;
        }
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

        var retainerNames = GetRetainerNamesMap();
        var baseColor = GetEffectiveSeriesColor(seriesConfig, settings, 0);
        var retainerIndex = 0;

        foreach (var (variableName, points) in perRetainerPointsDict)
        {
            if (points.Count == 0) continue;
            if (!TryResolveRetainerName(variableName, retainerNames, out var retainerName)) continue;

            var retainerColor = GetRetainerSeriesColor(baseColor, retainerIndex);

            if (groupingMode == TableGroupingMode.Character)
                AddPerRetainerCharacterSeries(result, points, defaultName, retainerName, retainerColor, retainerIndex, settings);
            else if (groupingMode == TableGroupingMode.All)
                AddPerRetainerAllSeries(result, points, defaultName, retainerName, retainerColor, seriesConfig);
            else
                AddPerRetainerGroupSeries(result, points, defaultName, retainerName, retainerColor, retainerIndex, groupingMode, settings);

            retainerIndex++;
        }

        return result;
    }

    /// <summary>
    /// Resolves the display name for a per-retainer variable. Handles both the new
    /// format (ItemRetainerX_{retainerId}_{itemId}) and the old combined format (ItemRetainer_{itemId}).
    /// Returns false when the variable name is malformed and should be skipped.
    /// </summary>
    private static bool TryResolveRetainerName(
        string variableName,
        Dictionary<ulong, string> retainerNames,
        out string retainerName)
    {
        retainerName = "Retainers";

        // Old format: ItemRetainer_{itemId} - show as combined "Retainers"
        if (!variableName.StartsWith("ItemRetainerX_"))
            return true;

        // New format: ItemRetainerX_{retainerId}_{itemId}, e.g. ItemRetainerX_12345678_1234
        var parts = variableName.Split('_');
        if (parts.Length < 3) return false;
        if (!ulong.TryParse(parts[1], out var retainerId)) return false;

        retainerName = retainerNames.TryGetValue(retainerId, out var name) ? name : $"Retainer {retainerId}";
        return true;
    }

    private void AddPerRetainerCharacterSeries(
        List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> result,
        List<(ulong characterId, DateTime timestamp, long value)> points,
        string defaultName,
        string retainerName,
        Vector4 retainerColor,
        int retainerIndex,
        DataToolSettings settings)
    {
        foreach (var charGroup in points.GroupBy(p => p.characterId))
        {
            var charName = _getCharacterDisplayName(charGroup.Key);
            var seriesName = $"{defaultName} ({charName} - {retainerName})";

            Vector4 seriesColor;
            if (settings.ColorMode == Models.GraphColorMode.PreferredCharacterColors)
            {
                var charColor = GetPreferredCharacterColor(charGroup.Key) ?? GetDefaultSeriesColor(retainerIndex);
                seriesColor = GetRetainerSeriesColor(charColor, retainerIndex);
            }
            else
            {
                seriesColor = retainerColor;
            }

            var samples = ToOrderedSamples(charGroup);
            if (samples.Count > 0)
                result.Add((seriesName, samples, seriesColor));
        }
    }

    private void AddPerRetainerAllSeries(
        List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> result,
        List<(ulong characterId, DateTime timestamp, long value)> points,
        string defaultName,
        string retainerName,
        Vector4 retainerColor,
        ItemColumnConfig seriesConfig)
    {
        var aggregated = ToOrderedSamples(points);
        if (aggregated.Count > 0)
        {
            var seriesName = $"{seriesConfig.CustomName ?? defaultName} ({retainerName})";
            result.Add((seriesName, aggregated, retainerColor));
        }
    }

    private void AddPerRetainerGroupSeries(
        List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> result,
        List<(ulong characterId, DateTime timestamp, long value)> points,
        string defaultName,
        string retainerName,
        Vector4 retainerColor,
        int retainerIndex,
        TableGroupingMode groupingMode,
        DataToolSettings settings)
    {
        // Group by World, DataCenter, or Region
        var characterGroups = BuildCharacterLocationMap(groupingMode);
        var byGroup = points
            .GroupBy(p => characterGroups.TryGetValue(p.characterId, out var g) ? g : "Unknown")
            .OrderBy(g => g.Key);

        foreach (var group in byGroup)
        {
            var seriesName = $"{defaultName} ({group.Key} - {retainerName})";
            var aggregated = ToOrderedSamples(group);
            if (aggregated.Count > 0)
            {
                var seriesColor = settings.ColorMode == Models.GraphColorMode.PreferredItemColors
                    ? retainerColor
                    : GetRetainerSeriesColor(GetDefaultSeriesColor(0), retainerIndex);
                result.Add((seriesName, aggregated, seriesColor));
            }
        }
    }

    /// <summary>
    /// Gets a mapping of retainer ID to retainer name from inventory cache.
    /// Uses a cached result that is refreshed periodically.
    /// </summary>
    private Dictionary<ulong, string> GetRetainerNamesMap()
    {
        if (_cachedRetainerNames != null &&
            (DateTime.UtcNow - _lastRetainerNamesCacheRefresh) < RetainerNamesCacheExpiry)
        {
            return _cachedRetainerNames;
        }

        var retainerNames = new Dictionary<ulong, string>();
        try
        {
            // Get all inventory caches from memory cache (not DB)
            var allCaches = _inventoryCacheService?.GetAllInventories();
            if (allCaches != null)
            {
                foreach (var cache in allCaches)
                {
                    if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Retainer &&
                        cache.RetainerId != 0 &&
                        !string.IsNullOrEmpty(cache.Name))
                    {
                        retainerNames[cache.RetainerId] = cache.Name;
                    }
                }
            }

            _cachedRetainerNames = retainerNames;
            _lastRetainerNamesCacheRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logDebug($"GetRetainerNamesMap error: {ex.Message}");
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

    /// <summary>
    /// Builds a mapping of character ID to location group name (World, DataCenter, or Region).
    /// </summary>
    private Dictionary<ulong, string> BuildCharacterLocationMap(TableGroupingMode groupingMode)
    {
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
        return characterGroups;
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
    /// Gets the effective color for a series based on ColorMode setting.
    /// </summary>
    private Vector4 GetEffectiveSeriesColor(ItemColumnConfig config, DataToolSettings settings, int seriesIndex)
    {
        // First check if the column has a custom color set
        if (config.Color.HasValue)
            return config.Color.Value;

        // Check ColorMode for preferred colors
        if (settings.ColorMode == Models.GraphColorMode.PreferredItemColors)
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

    #region Series Building Helpers

    /// <summary>
    /// Loads points from the time-series cache and merges any pending (unflushed) inventory samples.
    /// Returns null if no data is available.
    /// </summary>
    /// <param name="variableName">The fully-qualified variable name (e.g. "Item_123", "Gil").</param>
    /// <param name="pendingPrefix">Prefix for pending sample lookup (e.g. "Item_"), or null to skip pending merge.</param>
    /// <param name="pendingSuffix">Suffix for pending sample lookup (e.g. "_123"), or null to skip pending merge.</param>
    /// <param name="startTime">Optional start time filter.</param>
    private IReadOnlyList<(ulong characterId, DateTime timestamp, long value)>? LoadAndMergePoints(
        string variableName,
        string? pendingPrefix,
        string? pendingSuffix,
        DateTime? startTime)
    {
        var allPoints = CacheService.GetAllPointsBatch(variableName, startTime);
        allPoints.TryGetValue(variableName, out var pts);

        // Merge pending samples for real-time display (also serves as fallback when cache is empty)
        if (_inventoryCacheService != null && pendingPrefix != null && pendingSuffix != null)
        {
            var pending = _inventoryCacheService.GetPendingSamples(pendingPrefix, pendingSuffix);
            if (pending.TryGetValue(variableName, out var pendingPts) && pendingPts.Count > 0)
            {
                if (pts == null || pts.Count == 0)
                {
                    pts = pendingPts;
                }
                else
                {
                    var mutablePoints = pts.ToList();
                    mutablePoints.AddRange(pendingPts);
                    pts = mutablePoints;
                }
            }
        }

        return pts == null || pts.Count == 0 ? null : pts;
    }

    /// <summary>
    /// Loads per-retainer points matching a prefix+suffix pattern and merges pending samples.
    /// </summary>
    private Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> LoadPerRetainerPoints(
        string prefix,
        string itemIdSuffix,
        uint itemId,
        DateTime? startTime)
    {
        var dict = CacheService.GetPointsBatchWithSuffix(prefix, itemIdSuffix, startTime);

        // Merge pending samples
        if (_inventoryCacheService != null)
        {
            var pendingSamples = _inventoryCacheService.GetPendingSamples(prefix, itemIdSuffix);
            foreach (var (varName, pendingPoints) in pendingSamples)
            {
                if (!dict.TryGetValue(varName, out var existingList))
                {
                    existingList = new List<(ulong, DateTime, long)>();
                    dict[varName] = existingList;
                }
                existingList.AddRange(pendingPoints);
            }
        }

        // Fallback to old total retainer data if no per-retainer data
        if (dict.Count == 0)
        {
            var fallbackName = $"ItemRetainer_{itemId}";
            var fallbackPoints = CacheService.GetAllPointsBatch(fallbackName, startTime);
            if (fallbackPoints.TryGetValue(fallbackName, out var fallbackPts) && fallbackPts.Count > 0)
                dict[fallbackName] = fallbackPts;
        }

        return dict;
    }

    /// <summary>
    /// Orders points chronologically and projects them to float samples (simple aggregation, no forward-fill).
    /// </summary>
    private static List<(DateTime ts, float value)> ToOrderedSamples(
        IEnumerable<(ulong characterId, DateTime timestamp, long value)> points)
        => points
            .OrderBy(p => p.timestamp)
            .Select(p => (ts: p.timestamp, value: (float)p.value))
            .ToList();

    /// <summary>
    /// Groups flat points by the current grouping mode and builds named/colored series.
    /// For simple aggregation (no forward-fill) — used by LoadSeriesData.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> BuildGroupedSeries(
        IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> points,
        TableGroupingMode groupingMode,
        string seriesLabel,
        Vector4? baseColor,
        DataToolSettings settings,
        bool isSingleItem)
        => BuildGroupedSeriesCore(
            points,
            p => p.characterId,
            ToOrderedSamples,
            groupIndex => settings.ColorMode == Models.GraphColorMode.PreferredItemColors
                ? baseColor ?? GetDefaultSeriesColor(groupIndex)
                : GetDefaultSeriesColor(groupIndex),
            groupingMode, seriesLabel, baseColor, settings, isSingleItem);

    /// <summary>
    /// Groups tagged (multi-source) points by grouping mode with forward-fill aggregation.
    /// Used by LoadMergedSeriesData where multiple variables are summed together.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> BuildGroupedSeriesWithAggregation(
        IReadOnlyList<(string source, ulong characterId, DateTime ts, long value)> taggedPoints,
        TableGroupingMode groupingMode,
        string seriesLabel,
        Vector4? baseColor,
        DataToolSettings settings,
        bool isSingleItem)
        => BuildGroupedSeriesCore(
            taggedPoints,
            p => p.characterId,
            // Key each forward-fill source by (source, character) so distinct characters are summed
            // independently; within a per-character group the character component is constant and inert.
            pts => AggregateWithForwardFill(pts.Select(p => ($"{p.source}_{p.characterId}", p.ts, p.value))),
            groupIndex => baseColor ?? GetDefaultSeriesColor(groupIndex),
            groupingMode, seriesLabel, baseColor, settings, isSingleItem);

    /// <summary>
    /// Shared grouping/series-building logic for the Character / All / World-DC-Region modes.
    /// The point representation, sample aggregation, and group-mode color are supplied by the caller
    /// so simple and forward-filled variants share the branch structure without duplicating it.
    /// </summary>
    /// <param name="characterSelector">Extracts the character ID from a point.</param>
    /// <param name="aggregate">Projects a set of points into ordered samples (simple or forward-filled).</param>
    /// <param name="groupColorSelector">Produces the series color for a World/DataCenter/Region group by index.</param>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> BuildGroupedSeriesCore<TPoint>(
        IReadOnlyList<TPoint> points,
        Func<TPoint, ulong> characterSelector,
        Func<IEnumerable<TPoint>, List<(DateTime ts, float value)>> aggregate,
        Func<int, Vector4> groupColorSelector,
        TableGroupingMode groupingMode,
        string seriesLabel,
        Vector4? baseColor,
        DataToolSettings settings,
        bool isSingleItem)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();

        if (groupingMode == TableGroupingMode.Character)
        {
            var byCharacter = points.GroupBy(characterSelector);
            var charIndex = 0;
            foreach (var charGroup in byCharacter)
            {
                var charName = _getCharacterDisplayName(charGroup.Key);
                var seriesName = isSingleItem ? charName : $"{seriesLabel} ({charName})";

                Vector4 seriesColor;
                if (settings.ColorMode == Models.GraphColorMode.PreferredCharacterColors)
                    seriesColor = GetPreferredCharacterColor(charGroup.Key) ?? GetDefaultSeriesColor(charIndex);
                else
                    seriesColor = baseColor ?? GetDefaultSeriesColor(charIndex);

                var samples = aggregate(charGroup);
                if (samples.Count > 0)
                    result.Add((seriesName, samples, seriesColor));
                charIndex++;
            }
        }
        else if (groupingMode == TableGroupingMode.All)
        {
            var aggregated = aggregate(points);
            if (aggregated.Count > 0)
                result.Add((seriesLabel, aggregated, baseColor));
        }
        else
        {
            // World / DataCenter / Region
            var characterGroups = BuildCharacterLocationMap(groupingMode);
            var byGroup = points
                .GroupBy(p => characterGroups.TryGetValue(characterSelector(p), out var g) ? g : "Unknown")
                .OrderBy(g => g.Key);

            var groupIndex = 0;
            foreach (var group in byGroup)
            {
                var seriesName = isSingleItem ? group.Key : $"{seriesLabel} ({group.Key})";
                var aggregated = aggregate(group);
                if (aggregated.Count > 0)
                    result.Add((seriesName, aggregated, groupColorSelector(groupIndex)));
                groupIndex++;
            }
        }

        return result;
    }

    #endregion
}
