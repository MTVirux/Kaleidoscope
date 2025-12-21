using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.CrystalTracker;

/// <summary>
/// Component for tracking all crystal types with flexible grouping and filtering.
/// Stores shards, crystals, and clusters separately per element for dynamic filtering.
/// </summary>
public class CrystalTrackerComponent
{
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly SampleGraphWidget _graphWidget;

    private DateTime _lastSampleTime = DateTime.MinValue;
    private int _sampleIntervalMs = ConfigStatic.DefaultSamplerIntervalMs;

    private CrystalTrackerSettings Settings => _configService.Config.CrystalTracker;

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

    public CrystalTrackerComponent(SamplerService samplerService, ConfigurationService configService)
    {
        _samplerService = samplerService;
        _configService = configService;

        _graphWidget = new SampleGraphWidget(new SampleGraphWidget.GraphConfig
        {
            MinValue = 0,
            MaxValue = 999_999,
            PlotId = "crystaltracker_plot",
            NoDataText = "No crystal data yet.",
            FloatEpsilon = ConfigStatic.FloatEpsilon
        });
    }

    public void Draw()
    {
        var settings = Settings;

        // Sample crystals at regular intervals
        try
        {
            _sampleIntervalMs = Math.Max(1, _samplerService.IntervalMs);

            var now = DateTime.UtcNow;
            if ((now - _lastSampleTime).TotalMilliseconds >= _sampleIntervalMs)
            {
                SampleCrystals();
                _lastSampleTime = now;
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
            settings.AutoScaleGraph,
            settings.LegendWidth,
            settings.ShowLegend);

        // Calculate time cutoff
        DateTime? timeCutoff = null;
        if (settings.TimeRangeUnit != TimeRangeUnit.All)
        {
            timeCutoff = CalculateTimeCutoff(settings);
        }

        // Get and draw data based on grouping mode
        try
        {
            switch (settings.Grouping)
            {
                case CrystalGrouping.None:
                    DrawTotalCrystals(timeCutoff);
                    break;
                case CrystalGrouping.ByCharacter:
                    DrawByCharacter(timeCutoff);
                    break;
                case CrystalGrouping.ByElement:
                    DrawByElement(timeCutoff);
                    break;
                case CrystalGrouping.ByCharacterAndElement:
                    DrawByCharacterAndElement(timeCutoff);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CrystalTrackerComponent] Draw error: {ex.Message}");
            ImGui.TextUnformatted("Error loading crystal data.");
        }
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
    /// </summary>
    private unsafe void SampleCrystals()
    {
        var cid = GameStateService.PlayerContentId;
        if (cid == 0) return;

        var im = GameStateService.InventoryManagerInstance();
        if (im == null) return;

        // Sample ALL elements and tiers (not filtered by settings)
        // This ensures we always have complete data regardless of current filter settings
        for (int element = 0; element < 6; element++)
        {
            for (int tier = 0; tier < 3; tier++)
            {
                // Item IDs: Shard = 2 + element, Crystal = 8 + element, Cluster = 14 + element
                uint itemId = (uint)(2 + element + tier * 6);
                
                long count = 0;
                try { count += im->GetInventoryItemCount(itemId); } catch { }
                
                if (GameStateService.IsRetainerActive())
                {
                    try { count += GameStateService.GetActiveRetainerCrystalCount(im, itemId); } catch { }
                }

                var variableName = GetVariableName(element, tier);
                _samplerService.DbService.SaveSampleIfChanged(variableName, cid, count);
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

    /// <summary>
    /// Draws a single total line for all crystals.
    /// Aggregates across enabled elements and tiers.
    /// </summary>
    private void DrawTotalCrystals(DateTime? timeCutoff)
    {
        var allPoints = new Dictionary<DateTime, long>();
        var enabledTiers = GetEnabledTiers().ToList();

        // Aggregate all enabled element/tier data across all characters
        foreach (var element in GetEnabledElements())
        {
            foreach (var tier in enabledTiers)
            {
                var variableName = GetVariableName(element, tier);
                var points = _samplerService.DbService.GetAllPoints(variableName);

                foreach (var (_, ts, value) in points)
                {
                    if (timeCutoff.HasValue && ts < timeCutoff.Value) continue;

                    if (!allPoints.ContainsKey(ts))
                        allPoints[ts] = 0;
                    allPoints[ts] += value;
                }
            }
        }

        if (allPoints.Count == 0)
        {
            ImGui.TextUnformatted("No crystal data yet.");
            return;
        }

        var samples = allPoints
            .OrderBy(p => p.Key)
            .Select(p => (float)p.Value)
            .ToList();

        _graphWidget.Draw(samples);
    }

    /// <summary>
    /// Draws separate lines per character (all elements combined).
    /// </summary>
    private void DrawByCharacter(DateTime? timeCutoff)
    {
        var characterData = new Dictionary<ulong, Dictionary<DateTime, long>>();
        var characterNames = _samplerService.DbService.GetAllCharacterNames().ToDictionary(c => c.characterId, c => c.name);
        var enabledTiers = GetEnabledTiers().ToList();

        // Aggregate all enabled element/tier data per character
        foreach (var element in GetEnabledElements())
        {
            foreach (var tier in enabledTiers)
            {
                var variableName = GetVariableName(element, tier);
                var points = _samplerService.DbService.GetAllPoints(variableName);

                foreach (var (charId, ts, value) in points)
                {
                    if (timeCutoff.HasValue && ts < timeCutoff.Value) continue;

                    if (!characterData.ContainsKey(charId))
                        characterData[charId] = new Dictionary<DateTime, long>();

                    if (!characterData[charId].ContainsKey(ts))
                        characterData[charId][ts] = 0;
                    characterData[charId][ts] += value;
                }
            }
        }

        // Convert to series format
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

        if (series.Count == 0)
        {
            ImGui.TextUnformatted("No crystal data yet.");
            return;
        }

        _graphWidget.DrawMultipleSeries(series);
    }

    /// <summary>
    /// Draws separate lines per element (all characters combined).
    /// </summary>
    private void DrawByElement(DateTime? timeCutoff)
    {
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        var enabledTiers = GetEnabledTiers().ToList();

        foreach (var element in GetEnabledElements())
        {
            var allPoints = new Dictionary<DateTime, long>();

            foreach (var tier in enabledTiers)
            {
                var variableName = GetVariableName(element, tier);
                var points = _samplerService.DbService.GetAllPoints(variableName);

                foreach (var (_, ts, value) in points)
                {
                    if (timeCutoff.HasValue && ts < timeCutoff.Value) continue;

                    if (!allPoints.ContainsKey(ts))
                        allPoints[ts] = 0;
                    allPoints[ts] += value;
                }
            }

            var samples = allPoints
                .OrderBy(p => p.Key)
                .Select(p => (p.Key, (float)p.Value))
                .ToList();

            if (samples.Count > 0)
                series.Add((ElementNames[element], samples));
        }

        if (series.Count == 0)
        {
            ImGui.TextUnformatted("No crystal data yet.");
            return;
        }

        _graphWidget.DrawMultipleSeries(series);
    }

    /// <summary>
    /// Draws separate lines per element per character.
    /// </summary>
    private void DrawByCharacterAndElement(DateTime? timeCutoff)
    {
        var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
        var characterNames = _samplerService.DbService.GetAllCharacterNames().ToDictionary(c => c.characterId, c => c.name);
        var enabledTiers = GetEnabledTiers().ToList();

        foreach (var element in GetEnabledElements())
        {
            // Aggregate all enabled tiers, grouped by character
            var byCharacter = new Dictionary<ulong, Dictionary<DateTime, long>>();

            foreach (var tier in enabledTiers)
            {
                var variableName = GetVariableName(element, tier);
                var points = _samplerService.DbService.GetAllPoints(variableName);

                foreach (var (charId, ts, value) in points)
                {
                    if (timeCutoff.HasValue && ts < timeCutoff.Value) continue;

                    if (!byCharacter.ContainsKey(charId))
                        byCharacter[charId] = new Dictionary<DateTime, long>();

                    if (!byCharacter[charId].ContainsKey(ts))
                        byCharacter[charId][ts] = 0;
                    byCharacter[charId][ts] += value;
                }
            }

            foreach (var (charId, charPoints) in byCharacter)
            {
                var charName = characterNames.TryGetValue(charId, out var n) ? n ?? $"CID:{charId}" : $"CID:{charId}";
                var seriesName = $"{charName} - {ElementNames[element]}";
                var samples = charPoints
                    .OrderBy(p => p.Key)
                    .Select(p => (p.Key, (float)p.Value))
                    .ToList();

                if (samples.Count > 0)
                    series.Add((seriesName, samples));
            }
        }

        if (series.Count == 0)
        {
            ImGui.TextUnformatted("No crystal data yet.");
            return;
        }

        _graphWidget.DrawMultipleSeries(series);
    }

    private static DateTime CalculateTimeCutoff(CrystalTrackerSettings settings)
    {
        var now = DateTime.UtcNow;
        return settings.TimeRangeUnit switch
        {
            TimeRangeUnit.Minutes => now.AddMinutes(-settings.TimeRangeValue),
            TimeRangeUnit.Hours => now.AddHours(-settings.TimeRangeValue),
            TimeRangeUnit.Days => now.AddDays(-settings.TimeRangeValue),
            TimeRangeUnit.Weeks => now.AddDays(-settings.TimeRangeValue * 7),
            TimeRangeUnit.Months => now.AddMonths(-settings.TimeRangeValue),
            _ => DateTime.MinValue
        };
    }
}
