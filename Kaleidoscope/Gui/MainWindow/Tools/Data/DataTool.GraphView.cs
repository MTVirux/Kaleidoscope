using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// DataTool partial class containing graph view rendering and data loading logic.
/// </summary>
public partial class DataTool
{
    private bool _cachedShowRetainerBreakdownInGraph;

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
        
        var timeRange = GetTimeRange();
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
            var groups = new List<MTGraphSeriesGroup>();
            foreach (var (itemName, seriesNames) in seriesByItem)
            {
                // Only create a group if the item has multiple series
                if (seriesNames.Count > 1)
                {
                    var color = itemColors.TryGetValue(itemName, out var c) 
                        ? new Vector3(c.X, c.Y, c.Z) 
                        : new Vector3(0.6f, 0.6f, 0.6f);
                    
                    groups.Add(new MTGraphSeriesGroup
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
            using (ProfilerService.BeginStaticChildScope("CacheGetPoints"))
            {
                // Cache-first: get points from TimeSeriesCacheService
                var allPoints = CacheService.GetAllPointsBatch(variableName, startTime);
            
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
                    // Cache-first: get retainer points from TimeSeriesCacheService
                    var retainerPoints = CacheService.GetAllPointsBatch(retainerVariableName, startTime);
                    
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
                    // Cache-first: use TimeSeriesCacheService with prefix and suffix matching
                    var itemIdSuffix = $"_{seriesConfig.Id}";
                    perRetainerPointsDict = CacheService.GetPointsBatchWithSuffix(perRetainerVariablePrefix, itemIdSuffix, startTime);
                    
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
                        // Cache-first: get fallback points from TimeSeriesCacheService
                        var fallbackPoints = CacheService.GetAllPointsBatch(fallbackVariableName, startTime);
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
                    if (settings.ColorMode == Models.GraphColorMode.PreferredCharacterColors)
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
                
                // Cache-first: use TimeSeriesCacheService instead of direct DB access
                var pointsDict = CacheService.GetAllPointsBatch(variableName, startTime);
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
                    // Cache-first: use TimeSeriesCacheService instead of direct DB access
                    var retainerPointsDict = CacheService.GetAllPointsBatch(retainerVariableName, startTime);
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
                    if (settings.ColorMode == Models.GraphColorMode.PreferredCharacterColors)
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
                var seriesColor = settings.ColorMode == Models.GraphColorMode.PreferredItemColors 
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
        
        var retainerNames = GetRetainerNamesMap();
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
            
            var retainerColor = GetRetainerSeriesColor(baseColor, retainerIndex);
            
            if (groupingMode == TableGroupingMode.Character)
            {
                var byCharacter = points.GroupBy(p => p.characterId);
                
                foreach (var charGroup in byCharacter)
                {
                    var charName = GetCharacterDisplayName(charGroup.Key);
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
                        var seriesColor = settings.ColorMode == Models.GraphColorMode.PreferredItemColors
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
}
