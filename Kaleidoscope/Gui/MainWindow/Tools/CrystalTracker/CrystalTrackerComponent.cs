using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.CrystalTracker;

/// <summary>
/// Component for tracking all crystal types with flexible grouping and filtering.
/// Stores shards, crystals, and clusters separately per element for dynamic filtering.
/// Uses IGameInventory events for immediate updates when crystals change.
/// </summary>
public class CrystalTrackerComponent : IDisposable
{
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryChangeService? _inventoryChangeService;
    private readonly ImplotGraphWidget _graphWidget;

    private volatile bool _pendingCrystalUpdate = false;
    
    // Caching fields for optimization
    private DateTime _lastCacheTime = DateTime.MinValue;
    private CrystalGrouping _cachedGrouping;
    private TimeUnit _cachedTimeRangeUnit;
    private int _cachedTimeRangeValue;
    private int _cachedFilterHash;
    private List<float>? _cachedSingleSeriesData;
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? _cachedMultiSeriesData;
    private bool _cacheIsDirty = true;
    private const double CacheValiditySeconds = 2.0; // Refresh cache every 2 seconds max (data changes trigger immediate refresh via _cacheIsDirty)

    // Settings provider for instance-specific settings
    private Func<CrystalTrackerSettings>? _settingsProvider;
    
    private CrystalTrackerSettings Settings => _settingsProvider?.Invoke() ?? _configService.Config.CrystalTracker;
    
    /// <summary>
    /// Sets a custom settings provider for instance-specific settings.
    /// </summary>
    public void SetSettingsProvider(Func<CrystalTrackerSettings> provider) => _settingsProvider = provider;
    
    /// <summary>
    /// Event raised when settings are changed and need persistence.
    /// </summary>
    public event Action? OnSettingsChanged;

    /// <summary>
    /// Element names for display.
    /// </summary>
    private static readonly string[] ElementNames = { "Fire", "Ice", "Wind", "Earth", "Lightning", "Water" };

    /// <summary>
    /// Tier names for storage.
    /// </summary>
    private static readonly string[] TierNames = { "Shard", "Crystal", "Cluster" };

    /// <summary>
    /// Element colors for graph lines.
    /// </summary>
    private static readonly Vector4[] ElementColors =
    {
        new(1.0f, 0.3f, 0.2f, 1.0f),  // Fire - red/orange
        new(0.4f, 0.7f, 1.0f, 1.0f),  // Ice - light blue
        new(0.3f, 0.9f, 0.5f, 1.0f),  // Wind - green
        new(0.8f, 0.6f, 0.3f, 1.0f),  // Earth - brown/tan
        new(0.9f, 0.8f, 0.2f, 1.0f),  // Lightning - yellow
        new(0.3f, 0.5f, 1.0f, 1.0f)   // Water - blue
    };

    public CrystalTrackerComponent(SamplerService samplerService, ConfigurationService configService, InventoryChangeService? inventoryChangeService = null)
    {
        _samplerService = samplerService;
        _configService = configService;
        _inventoryChangeService = inventoryChangeService;

        _graphWidget = new ImplotGraphWidget(new ImplotGraphWidget.GraphConfig
        {
            MinValue = 0,
            MaxValue = 999_999,
            PlotId = "crystaltracker_plot",
            NoDataText = "No crystal data yet.",
            FloatEpsilon = ConfigStatic.FloatEpsilon
        });
        
        // Subscribe to auto-scroll settings changes from the controls drawer
        _graphWidget.OnAutoScrollSettingsChanged += OnAutoScrollSettingsChanged;

        // Subscribe to inventory change events for immediate crystal updates
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnCrystalsChanged += OnCrystalsChanged;
        }
    }
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, TimeUnit timeUnit, float nowPosition)
    {
        var settings = Settings;
        settings.AutoScrollEnabled = enabled;
        settings.AutoScrollTimeValue = timeValue;
        settings.AutoScrollTimeUnit = timeUnit;
        settings.AutoScrollNowPosition = nowPosition;
        OnSettingsChanged?.Invoke();
    }

    private void OnCrystalsChanged()
    {
        // Flag that crystals have changed - will be processed on next Draw()
        _pendingCrystalUpdate = true;
        _cacheIsDirty = true;
    }
    
    /// <summary>
    /// Calculates a hash of current filter settings to detect changes.
    /// </summary>
    private int CalculateFilterHash()
    {
        var settings = Settings;
        var hash = HashCode.Combine(
            settings.IncludeShards,
            settings.IncludeCrystals,
            settings.IncludeClusters,
            settings.IncludeFire,
            settings.IncludeIce,
            settings.IncludeWind);
        return HashCode.Combine(hash,
            settings.IncludeEarth,
            settings.IncludeLightning,
            settings.IncludeWater);
    }
    
    /// <summary>
    /// Checks if the cache needs to be refreshed.
    /// </summary>
    private bool NeedsCacheRefresh()
    {
        if (_cacheIsDirty) return true;
        
        var settings = Settings;
        var currentFilterHash = CalculateFilterHash();
        
        // Check if settings changed
        if (_cachedGrouping != settings.Grouping ||
            _cachedTimeRangeUnit != settings.TimeRangeUnit ||
            _cachedTimeRangeValue != settings.TimeRangeValue ||
            _cachedFilterHash != currentFilterHash)
        {
            return true;
        }
        
        // Check if cache is stale
        var elapsed = (DateTime.UtcNow - _lastCacheTime).TotalSeconds;
        return elapsed > CacheValiditySeconds;
    }

    public void Dispose()
    {
        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnCrystalsChanged -= OnCrystalsChanged;
        }
    }
    
    /// <summary>
    /// Gets the hidden series names from the graph widget.
    /// </summary>
    public IReadOnlyCollection<string> HiddenSeries => _graphWidget.HiddenSeries;
    
    /// <summary>
    /// Sets the hidden series names on the graph widget.
    /// </summary>
    public void SetHiddenSeries(IEnumerable<string>? seriesNames) => _graphWidget.SetHiddenSeries(seriesNames);

    public void Draw()
    {
        var settings = Settings;

        // Sample crystals only when inventory change events fire (no polling)
        try
        {
            if (_pendingCrystalUpdate)
            {
                _pendingCrystalUpdate = false;
                SampleCrystals();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CrystalTrackerComponent] Sampling error: {ex.Message}");
        }

        // Update graph display options
        _graphWidget.UpdateDisplayOptions(
            settings.ShowValueLabel,
            settings.ValueLabelOffsetX,
            settings.ValueLabelOffsetY,
            settings.LegendWidth,
            settings.ShowLegend,
            settings.GraphType,
            settings.ShowXAxisTimestamps,
            legendPosition: settings.LegendPosition,
            legendHeightPercent: settings.LegendHeightPercent,
            autoScrollEnabled: settings.AutoScrollEnabled,
            autoScrollTimeValue: settings.AutoScrollTimeValue,
            autoScrollTimeUnit: settings.AutoScrollTimeUnit,
            autoScrollNowPosition: settings.AutoScrollNowPosition,
            showControlsDrawer: settings.ShowControlsDrawer);

        // Check if we need to refresh the cache
        var needsRefresh = NeedsCacheRefresh();

        // Calculate time cutoff based on auto-scroll settings for efficient queries
        DateTime? timeCutoff = null;
        if (settings.AutoScrollEnabled)
        {
            // Only load data within the visible time window + buffer
            var timeRangeSeconds = settings.AutoScrollTimeUnit.ToSeconds(settings.AutoScrollTimeValue);
            var bufferSeconds = timeRangeSeconds * 2; // 2x buffer for smooth scrolling
            timeCutoff = DateTime.UtcNow.AddSeconds(-bufferSeconds);
        }
        else if (settings.TimeRangeUnit != TimeUnit.All)
        {
            timeCutoff = CalculateTimeCutoff(settings);
        }

        // Get and draw data based on grouping mode
        try
        {
            // Refresh cache if needed
            if (needsRefresh)
            {
                using (ProfilerService.BeginStaticChildScope("RefreshCache"))
                {
                    RefreshCachedData(settings, timeCutoff);
                }
            }
            
            // Draw from cache
            if (settings.Grouping == CrystalGrouping.None)
            {
                if (_cachedSingleSeriesData != null && _cachedSingleSeriesData.Count > 0)
                {
                    using (ProfilerService.BeginStaticChildScope("DrawSingleSeries"))
                    {
                        _graphWidget.Draw(_cachedSingleSeriesData);
                    }
                }
                else
                {
                    ImGui.TextUnformatted("No crystal data yet.");
                }
            }
            else
            {
                if (_cachedMultiSeriesData != null && _cachedMultiSeriesData.Count > 0)
                {
                    using (ProfilerService.BeginStaticChildScope("DrawMultiSeries"))
                    {
                        _graphWidget.DrawMultipleSeries(_cachedMultiSeriesData);
                    }
                }
                else
                {
                    ImGui.TextUnformatted("No crystal data yet.");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CrystalTrackerComponent] Draw error: {ex.Message}");
            ImGui.TextUnformatted("Error loading crystal data.");
        }
    }
    
    /// <summary>
    /// Refreshes the cached data from the database using a single batch query.
    /// </summary>
    private void RefreshCachedData(CrystalTrackerSettings settings, DateTime? timeCutoff)
    {
        // Update cache tracking
        _lastCacheTime = DateTime.UtcNow;
        _cachedGrouping = settings.Grouping;
        _cachedTimeRangeUnit = settings.TimeRangeUnit;
        _cachedTimeRangeValue = settings.TimeRangeValue;
        _cachedFilterHash = CalculateFilterHash();
        _cacheIsDirty = false;
        
        // Fetch all crystal data in one batch query
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> allCrystalData;
        using (ProfilerService.BeginStaticChildScope("DbQuery"))
        {
            allCrystalData = _samplerService.DbService.GetAllPointsBatch("Crystal_", timeCutoff);
        }
        
        IReadOnlyDictionary<ulong, string?> characterNames;
        using (ProfilerService.BeginStaticChildScope("CharacterNames"))
        {
            characterNames = _samplerService.DbService.GetAllCharacterNamesDict();
        }
        
        // Prepare cached data based on grouping mode
        switch (settings.Grouping)
        {
            case CrystalGrouping.None:
                _cachedSingleSeriesData = PrepareTotalCrystals(allCrystalData, settings);
                _cachedMultiSeriesData = null;
                break;
            case CrystalGrouping.ByCharacter:
                _cachedMultiSeriesData = PrepareByCharacter(allCrystalData, settings, characterNames);
                _cachedSingleSeriesData = null;
                break;
            case CrystalGrouping.ByElement:
                _cachedMultiSeriesData = PrepareByElement(allCrystalData, settings);
                _cachedSingleSeriesData = null;
                break;
            case CrystalGrouping.ByCharacterAndElement:
                _cachedMultiSeriesData = PrepareByCharacterAndElement(allCrystalData, settings, characterNames);
                _cachedSingleSeriesData = null;
                break;
            case CrystalGrouping.ByTier:
                _cachedMultiSeriesData = PrepareByTier(allCrystalData, settings);
                _cachedSingleSeriesData = null;
                break;
            case CrystalGrouping.ByCharacterAndTier:
                _cachedMultiSeriesData = PrepareByCharacterAndTier(allCrystalData, settings, characterNames);
                _cachedSingleSeriesData = null;
                break;
        }
    }
    
    /// <summary>
    /// Parses a crystal variable name to extract element and tier indices.
    /// </summary>
    private static bool TryParseVariableName(string variableName, out int element, out int tier)
    {
        element = -1;
        tier = -1;
        
        // Format: Crystal_{Element}_{Tier}
        if (!variableName.StartsWith("Crystal_")) return false;
        
        var parts = variableName.Split('_');
        if (parts.Length != 3) return false;
        
        element = Array.IndexOf(ElementNames, parts[1]);
        tier = Array.IndexOf(TierNames, parts[2]);
        
        return element >= 0 && tier >= 0;
    }
    
    /// <summary>
    /// Checks if a given element/tier combination is enabled in settings.
    /// </summary>
    private static bool IsEnabled(CrystalTrackerSettings settings, int element, int tier)
    {
        // Check tier filter
        var tierEnabled = tier switch
        {
            0 => settings.IncludeShards,
            1 => settings.IncludeCrystals,
            2 => settings.IncludeClusters,
            _ => false
        };
        if (!tierEnabled) return false;
        
        // Check element filter
        return settings.IsElementIncluded((CrystalElement)element);
    }
    
    /// <summary>
    /// Prepares aggregated total data from batch query results.
    /// </summary>
    private static List<float> PrepareTotalCrystals(
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> allData,
        CrystalTrackerSettings settings)
    {
        var allPoints = new Dictionary<DateTime, long>();
        
        foreach (var (variableName, points) in allData)
        {
            if (!TryParseVariableName(variableName, out var element, out var tier)) continue;
            if (!IsEnabled(settings, element, tier)) continue;
            
            foreach (var (_, ts, value) in points)
            {
                if (!allPoints.ContainsKey(ts))
                    allPoints[ts] = 0;
                allPoints[ts] += value;
            }
        }
        
        return allPoints
            .OrderBy(p => p.Key)
            .Select(p => (float)p.Value)
            .ToList();
    }
    
    /// <summary>
    /// Prepares per-character series data from batch query results.
    /// </summary>
    private static List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> PrepareByCharacter(
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> allData,
        CrystalTrackerSettings settings,
        IReadOnlyDictionary<ulong, string?> characterNames)
    {
        var characterData = new Dictionary<ulong, Dictionary<DateTime, long>>();
        
        foreach (var (variableName, points) in allData)
        {
            if (!TryParseVariableName(variableName, out var element, out var tier)) continue;
            if (!IsEnabled(settings, element, tier)) continue;
            
            foreach (var (charId, ts, value) in points)
            {
                if (!characterData.TryGetValue(charId, out var charPoints))
                {
                    charPoints = new Dictionary<DateTime, long>();
                    characterData[charId] = charPoints;
                }
                
                if (!charPoints.ContainsKey(ts))
                    charPoints[ts] = 0;
                charPoints[ts] += value;
            }
        }
        
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        foreach (var (charId, points) in characterData)
        {
            var name = characterNames.TryGetValue(charId, out var n) ? n ?? $"CID:{charId}" : $"CID:{charId}";
            var samples = points
                .OrderBy(p => p.Key)
                .Select(p => (p.Key, (float)p.Value))
                .ToList();
            series.Add((name, samples));
        }
        
        return series;
    }
    
    /// <summary>
    /// Prepares per-element series data from batch query results.
    /// </summary>
    private static List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> PrepareByElement(
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> allData,
        CrystalTrackerSettings settings)
    {
        var elementData = new Dictionary<int, Dictionary<DateTime, long>>();
        
        foreach (var (variableName, points) in allData)
        {
            if (!TryParseVariableName(variableName, out var element, out var tier)) continue;
            if (!IsEnabled(settings, element, tier)) continue;
            
            if (!elementData.TryGetValue(element, out var elemPoints))
            {
                elemPoints = new Dictionary<DateTime, long>();
                elementData[element] = elemPoints;
            }
            
            foreach (var (_, ts, value) in points)
            {
                if (!elemPoints.ContainsKey(ts))
                    elemPoints[ts] = 0;
                elemPoints[ts] += value;
            }
        }
        
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        foreach (var (element, points) in elementData.OrderBy(e => e.Key))
        {
            var samples = points
                .OrderBy(p => p.Key)
                .Select(p => (p.Key, (float)p.Value))
                .ToList();
            if (samples.Count > 0)
                series.Add((ElementNames[element], samples));
        }
        
        return series;
    }
    
    /// <summary>
    /// Prepares per-character-per-element series data from batch query results.
    /// </summary>
    private static List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> PrepareByCharacterAndElement(
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> allData,
        CrystalTrackerSettings settings,
        IReadOnlyDictionary<ulong, string?> characterNames)
    {
        // Key: (charId, element)
        var data = new Dictionary<(ulong, int), Dictionary<DateTime, long>>();
        
        foreach (var (variableName, points) in allData)
        {
            if (!TryParseVariableName(variableName, out var element, out var tier)) continue;
            if (!IsEnabled(settings, element, tier)) continue;
            
            foreach (var (charId, ts, value) in points)
            {
                var key = (charId, element);
                if (!data.TryGetValue(key, out var keyPoints))
                {
                    keyPoints = new Dictionary<DateTime, long>();
                    data[key] = keyPoints;
                }
                
                if (!keyPoints.ContainsKey(ts))
                    keyPoints[ts] = 0;
                keyPoints[ts] += value;
            }
        }
        
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        foreach (var ((charId, element), points) in data.OrderBy(k => k.Key.Item2))
        {
            var charName = characterNames.TryGetValue(charId, out var n) ? n ?? $"CID:{charId}" : $"CID:{charId}";
            var seriesName = $"{charName} - {ElementNames[element]}";
            var samples = points
                .OrderBy(p => p.Key)
                .Select(p => (p.Key, (float)p.Value))
                .ToList();
            if (samples.Count > 0)
                series.Add((seriesName, samples));
        }
        
        return series;
    }
    
    /// <summary>
    /// Prepares per-tier series data from batch query results.
    /// </summary>
    private static List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> PrepareByTier(
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> allData,
        CrystalTrackerSettings settings)
    {
        var tierData = new Dictionary<int, Dictionary<DateTime, long>>();
        
        foreach (var (variableName, points) in allData)
        {
            if (!TryParseVariableName(variableName, out var element, out var tier)) continue;
            if (!IsEnabled(settings, element, tier)) continue;
            
            if (!tierData.TryGetValue(tier, out var tierPoints))
            {
                tierPoints = new Dictionary<DateTime, long>();
                tierData[tier] = tierPoints;
            }
            
            foreach (var (_, ts, value) in points)
            {
                if (!tierPoints.ContainsKey(ts))
                    tierPoints[ts] = 0;
                tierPoints[ts] += value;
            }
        }
        
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        foreach (var (tier, points) in tierData.OrderBy(t => t.Key))
        {
            var samples = points
                .OrderBy(p => p.Key)
                .Select(p => (p.Key, (float)p.Value))
                .ToList();
            if (samples.Count > 0)
                series.Add((TierNames[tier], samples));
        }
        
        return series;
    }
    
    /// <summary>
    /// Prepares per-character-per-tier series data from batch query results.
    /// </summary>
    private static List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> PrepareByCharacterAndTier(
        Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> allData,
        CrystalTrackerSettings settings,
        IReadOnlyDictionary<ulong, string?> characterNames)
    {
        // Key: (charId, tier)
        var data = new Dictionary<(ulong, int), Dictionary<DateTime, long>>();
        
        foreach (var (variableName, points) in allData)
        {
            if (!TryParseVariableName(variableName, out var element, out var tier)) continue;
            if (!IsEnabled(settings, element, tier)) continue;
            
            foreach (var (charId, ts, value) in points)
            {
                var key = (charId, tier);
                if (!data.TryGetValue(key, out var keyPoints))
                {
                    keyPoints = new Dictionary<DateTime, long>();
                    data[key] = keyPoints;
                }
                
                if (!keyPoints.ContainsKey(ts))
                    keyPoints[ts] = 0;
                keyPoints[ts] += value;
            }
        }
        
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        foreach (var ((charId, tier), points) in data.OrderBy(k => k.Key.Item2))
        {
            var charName = characterNames.TryGetValue(charId, out var n) ? n ?? $"CID:{charId}" : $"CID:{charId}";
            var seriesName = $"{charName} - {TierNames[tier]}";
            var samples = points
                .OrderBy(p => p.Key)
                .Select(p => (p.Key, (float)p.Value))
                .ToList();
            if (samples.Count > 0)
                series.Add((seriesName, samples));
        }
        
        return series;
    }

    /// <summary>
    /// Gets the variable name for a specific element and tier.
    /// Format: Crystal_{Element}_{Tier} (e.g., Crystal_Fire_Shard)
    /// </summary>
    private static string GetVariableName(int element, int tier)
    {
        return $"Crystal_{ElementNames[element]}_{TierNames[tier]}";
    }

    /// <summary>
    /// Gets all variable names for tiers that are currently enabled in settings.
    /// </summary>
    private IEnumerable<(int element, int tier, string variableName)> GetEnabledVariables()
    {
        var settings = Settings;
        
        for (int element = 0; element < 6; element++)
        {
            if (!settings.IsElementIncluded((CrystalElement)element)) continue;

            // Tier 0 = Shard, 1 = Crystal, 2 = Cluster
            if (settings.IncludeShards)
                yield return (element, 0, GetVariableName(element, 0));
            if (settings.IncludeCrystals)
                yield return (element, 1, GetVariableName(element, 1));
            if (settings.IncludeClusters)
                yield return (element, 2, GetVariableName(element, 2));
        }
    }

    /// <summary>
    /// Samples current crystal values and persists them separately by tier.
    /// Always samples total (player + cached retainer) for consistent historical tracking.
    /// Retainer inventories are cached automatically by InventoryCacheService.
    /// </summary>
    private unsafe void SampleCrystals()
    {
        var cid = GameStateService.PlayerContentId;
        if (cid == 0) return;

        var im = GameStateService.InventoryManagerInstance();
        if (im == null) return;

        // Sample ALL elements and tiers (not filtered by settings)
        // This ensures we always have complete data regardless of current filter settings
        // Always sample total (player + cached retainer) for consistent historical data
        for (int element = 0; element < 6; element++)
        {
            for (int tier = 0; tier < 3; tier++)
            {
                // Item IDs: Shard = 2 + element, Crystal = 8 + element, Cluster = 14 + element
                uint itemId = (uint)(2 + element + tier * 6);
                
                // Get player's crystal count
                long playerCount = 0;
                try { playerCount = im->GetInventoryItemCount(itemId); } catch { }
                
                // Get cached retainer crystal totals for this element/tier (from inventory cache)
                long cachedRetainerTotal = _samplerService.DbService.GetTotalRetainerCrystals(cid, element, tier);
                
                // Save the combined total (player + all cached retainers) for historical tracking
                var variableName = GetVariableName(element, tier);
                _samplerService.DbService.SaveSampleIfChanged(variableName, cid, playerCount + cachedRetainerTotal);
            }
        }
    }

    /// <summary>
    /// Gets all enabled elements based on current filter settings.
    /// </summary>
    private IEnumerable<int> GetEnabledElements()
    {
        var settings = Settings;
        for (int i = 0; i < 6; i++)
        {
            if (settings.IsElementIncluded((CrystalElement)i))
                yield return i;
        }
    }

    /// <summary>
    /// Gets enabled tier indices based on current filter settings.
    /// Tier 0 = Shard, 1 = Crystal, 2 = Cluster.
    /// </summary>
    private IEnumerable<int> GetEnabledTiers()
    {
        var settings = Settings;
        if (settings.IncludeShards) yield return 0;
        if (settings.IncludeCrystals) yield return 1;
        if (settings.IncludeClusters) yield return 2;
    }

    private static DateTime CalculateTimeCutoff(CrystalTrackerSettings settings)
    {
        var now = DateTime.UtcNow;
        return settings.TimeRangeUnit switch
        {
            TimeUnit.Minutes => now.AddMinutes(-settings.TimeRangeValue),
            TimeUnit.Hours => now.AddHours(-settings.TimeRangeValue),
            TimeUnit.Days => now.AddDays(-settings.TimeRangeValue),
            TimeUnit.Weeks => now.AddDays(-settings.TimeRangeValue * 7),
            TimeUnit.Months => now.AddMonths(-settings.TimeRangeValue),
            _ => DateTime.MinValue
        };
    }
}
