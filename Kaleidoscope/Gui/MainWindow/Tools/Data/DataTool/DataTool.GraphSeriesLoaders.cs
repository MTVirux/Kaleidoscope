using System.Numerics;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// DataTool partial class containing graph series loading, grouping, and color logic.
/// Extracted to reduce the size of DataTool.GraphView.cs.
/// </summary>
public sealed partial class DataTool
{
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
            return Kaleidoscope.Libs.CharacterNameFormatter.FormatName(runtimeName, _configService.Config.CharacterNameFormat) ?? runtimeName;

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
            string variableName;
            string? pendingPrefix = null;
            string? pendingSuffix = null;
            string? perRetainerVariablePrefix = null;
            
            if (seriesConfig.IsCurrency)
            {
                variableName = ((TrackedDataType)seriesConfig.Id).ToString();
            }
            else
            {
                variableName = $"Item_{seriesConfig.Id}";
                pendingPrefix = "Item_";
                pendingSuffix = $"_{seriesConfig.Id}";
                if (settings.IncludeRetainers && settings.ShowRetainerBreakdownInGraph)
                    perRetainerVariablePrefix = "ItemRetainerX_";
            }
            
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
            LogDebug($"LoadSeriesData error: {ex.Message}");
            return null;
        }
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
            LogDebug($"LoadMergedSeriesData error: {ex.Message}");
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
                    .OrderBy(p => p.timestamp)
                    .Select(p => (ts: p.timestamp, value: (float)p.value))
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
                var characterGroups = BuildCharacterLocationMap(groupingMode);
                
                var byGroup = points
                    .GroupBy(p => characterGroups.TryGetValue(p.characterId, out var g) ? g : "Unknown")
                    .OrderBy(g => g.Key);
                
                foreach (var group in byGroup)
                {
                    var groupName = group.Key;
                    var seriesName = $"{defaultName} ({groupName} - {retainerName})";
                    
                    var aggregated = group
                        .OrderBy(p => p.timestamp)
                        .Select(p => (ts: p.timestamp, value: (float)p.value))
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
            LogDebug($"GetRetainerNamesMap error: {ex.Message}");
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
    /// Groups flat points by the current grouping mode and builds named/colored series.
    /// For simple aggregation (no forward-fill) — used by LoadSeriesData and BuildPerRetainerSeries.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> BuildGroupedSeries(
        IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> points,
        TableGroupingMode groupingMode,
        string seriesLabel,
        Vector4? baseColor,
        DataToolSettings settings,
        bool isSingleItem)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        
        if (groupingMode == TableGroupingMode.Character)
        {
            var byCharacter = points.GroupBy(p => p.characterId);
            var charIndex = 0;
            foreach (var charGroup in byCharacter)
            {
                var charName = GetCharacterDisplayName(charGroup.Key);
                var seriesName = isSingleItem ? charName : $"{seriesLabel} ({charName})";
                
                Vector4 seriesColor;
                if (settings.ColorMode == Models.GraphColorMode.PreferredCharacterColors)
                    seriesColor = GetPreferredCharacterColor(charGroup.Key) ?? GetDefaultSeriesColor(charIndex);
                else
                    seriesColor = baseColor ?? GetDefaultSeriesColor(charIndex);
                
                var samples = charGroup
                    .OrderBy(p => p.timestamp)
                    .Select(p => (ts: p.timestamp, value: (float)p.value))
                    .ToList();
                
                if (samples.Count > 0)
                    result.Add((seriesName, samples, seriesColor));
                charIndex++;
            }
        }
        else if (groupingMode == TableGroupingMode.All)
        {
            var aggregated = points
                .OrderBy(p => p.timestamp)
                .Select(p => (ts: p.timestamp, value: (float)p.value))
                .ToList();
            
            if (aggregated.Count > 0)
                result.Add((seriesLabel, aggregated, baseColor));
        }
        else
        {
            // World / DataCenter / Region
            var characterGroups = BuildCharacterLocationMap(groupingMode);
            var byGroup = points
                .GroupBy(p => characterGroups.TryGetValue(p.characterId, out var g) ? g : "Unknown")
                .OrderBy(g => g.Key);
            
            var groupIndex = 0;
            foreach (var group in byGroup)
            {
                var seriesName = isSingleItem ? group.Key : $"{seriesLabel} ({group.Key})";
                var aggregated = group
                    .OrderBy(p => p.timestamp)
                    .Select(p => (ts: p.timestamp, value: (float)p.value))
                    .ToList();
                
                if (aggregated.Count > 0)
                {
                    var seriesColor = settings.ColorMode == Models.GraphColorMode.PreferredItemColors
                        ? baseColor ?? GetDefaultSeriesColor(groupIndex)
                        : GetDefaultSeriesColor(groupIndex);
                    result.Add((seriesName, aggregated, seriesColor));
                }
                groupIndex++;
            }
        }
        
        return result;
    }
    
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
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        
        if (groupingMode == TableGroupingMode.Character)
        {
            var byCharacter = taggedPoints.GroupBy(p => p.characterId);
            var charIndex = 0;
            foreach (var charGroup in byCharacter)
            {
                var charName = GetCharacterDisplayName(charGroup.Key);
                var seriesName = isSingleItem ? charName : $"{seriesLabel} ({charName})";
                
                Vector4 seriesColor;
                if (settings.ColorMode == Models.GraphColorMode.PreferredCharacterColors)
                    seriesColor = GetPreferredCharacterColor(charGroup.Key) ?? GetDefaultSeriesColor(charIndex);
                else
                    seriesColor = baseColor ?? GetDefaultSeriesColor(charIndex);
                
                var samples = AggregateWithForwardFill(charGroup.Select(p => (p.source, p.ts, p.value)));
                if (samples.Count > 0)
                    result.Add((seriesName, samples, seriesColor));
                charIndex++;
            }
        }
        else if (groupingMode == TableGroupingMode.All)
        {
            var tagged = taggedPoints.Select(p => ($"{p.source}_{p.characterId}", p.ts, p.value));
            var aggregated = AggregateWithForwardFill(tagged);
            if (aggregated.Count > 0)
                result.Add((seriesLabel, aggregated, baseColor));
        }
        else
        {
            var characterGroups = BuildCharacterLocationMap(groupingMode);
            var byGroup = taggedPoints
                .GroupBy(p => characterGroups.TryGetValue(p.characterId, out var g) ? g : "Unknown")
                .OrderBy(g => g.Key);
            
            var groupIndex = 0;
            foreach (var group in byGroup)
            {
                var seriesName = isSingleItem ? group.Key : $"{seriesLabel} ({group.Key})";
                var aggregated = AggregateWithForwardFill(
                    group.Select(p => ($"{p.source}_{p.characterId}", p.ts, p.value)));
                
                if (aggregated.Count > 0)
                {
                    var seriesColor = baseColor ?? GetDefaultSeriesColor(groupIndex);
                    result.Add((seriesName, aggregated, seriesColor));
                }
                groupIndex++;
            }
        }
        
        return result;
    }
    
    #endregion
}