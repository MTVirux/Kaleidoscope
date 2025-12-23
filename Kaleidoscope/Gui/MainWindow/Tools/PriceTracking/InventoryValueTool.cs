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
                _characterNames[i + 1] = chars[i].name;
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
            DrawGraph();
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
        }
    }

    private void DrawGraph()
    {
        var settings = Settings;
        
        // Get time range
        var timeRange = GetTimeRange();
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : DateTime.MinValue;

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

        if (_selectedCharacterId == 0 && settings.ShowMultipleLines)
        {
            // Multi-character mode - show each character as a separate line
            var allData = DbService.GetAllInventoryValueHistory();
            
            // Group data by character
            var perCharacterData = new Dictionary<ulong, List<(DateTime ts, float value)>>();
            foreach (var entry in allData)
            {
                if (startTime != DateTime.MinValue && entry.Timestamp < startTime)
                    continue;

                if (!perCharacterData.ContainsKey(entry.CharacterId))
                    perCharacterData[entry.CharacterId] = new();
                
                var value = settings.IncludeGil ? entry.TotalValue : entry.ItemValue;
                perCharacterData[entry.CharacterId].Add((entry.Timestamp, value));
            }
            
            // Convert to the format expected by ImplotGraphWidget
            var series = perCharacterData
                .Select(kvp => (
                    name: GetCharacterDisplayName(kvp.Key),
                    samples: (IReadOnlyList<(DateTime ts, float value)>)kvp.Value
                ))
                .ToList();
            
            _graphWidget.DrawMultipleSeries(series);
        }
        else
        {
            // Single line mode (either single character or all aggregated)
            List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> data;
            
            if (_selectedCharacterId == 0)
            {
                // All characters combined - aggregate all history data
                var allData = DbService.GetAllInventoryValueHistory();
                var aggregated = allData
                    .GroupBy(e => e.Timestamp)
                    .Select(g => (
                        Timestamp: g.Key, 
                        TotalValue: g.Sum(x => x.TotalValue),
                        GilValue: g.Sum(x => x.GilValue),
                        ItemValue: g.Sum(x => x.ItemValue)))
                    .OrderBy(x => x.Timestamp)
                    .ToList();
                
                if (startTime != DateTime.MinValue)
                {
                    data = aggregated.Where(d => d.Timestamp >= startTime).ToList();
                }
                else
                {
                    data = aggregated;
                }
            }
            else
            {
                // Single character
                data = DbService.GetInventoryValueHistory(_selectedCharacterId);
                
                if (startTime != DateTime.MinValue)
                {
                    data = data.Where(d => d.Timestamp >= startTime).ToList();
                }
            }
            
            // Convert to timestamped series format for the widget
            var samples = data
                .Select(d => (
                    ts: d.Timestamp, 
                    value: (float)(settings.IncludeGil ? d.TotalValue : d.ItemValue)
                ))
                .ToList();
            
            // Use DrawMultipleSeries with single series for consistent time-based rendering
            if (samples.Count > 0)
            {
                var seriesName = _selectedCharacterId == 0 ? "Total Value" : GetCharacterDisplayName(_selectedCharacterId);
                var series = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>
                {
                    (seriesName, samples)
                };
                _graphWidget.DrawMultipleSeries(series);
            }
            else
            {
                ImGui.TextDisabled("No value history data");
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

            ImGui.TextUnformatted("Inventory Value Settings");
            ImGui.Separator();

            var includeRetainers = settings.IncludeRetainers;
            if (ImGui.Checkbox("Include retainer inventories", ref includeRetainers))
            {
                settings.IncludeRetainers = includeRetainers;
                _configService.Save();
            }
            ShowSettingTooltip("Include items from retainer inventories in the value calculation.", "On");

            var includeGil = settings.IncludeGil;
            if (ImGui.Checkbox("Include gil", ref includeGil))
            {
                settings.IncludeGil = includeGil;
                _configService.Save();
            }
            ShowSettingTooltip("Include character and retainer gil in the total value.", "On");

            var showMultipleLines = settings.ShowMultipleLines;
            if (ImGui.Checkbox("Show multiple lines (per character)", ref showMultipleLines))
            {
                settings.ShowMultipleLines = showMultipleLines;
                _configService.Save();
            }
            ShowSettingTooltip("When viewing 'All Characters', show a separate line for each character.", "On");

            if (showMultipleLines)
            {
                var showLegend = settings.ShowLegend;
                if (ImGui.Checkbox("Show legend", ref showLegend))
                {
                    settings.ShowLegend = showLegend;
                    _configService.Save();
                }
                ShowSettingTooltip("Show a legend panel on the right side of the graph.", "On");

                if (showLegend)
                {
                    var legendPosition = (int)settings.LegendPosition;
                    if (ImGui.Combo("Legend position", ref legendPosition, LegendPositionNames, LegendPositionNames.Length))
                    {
                        settings.LegendPosition = (LegendPosition)legendPosition;
                        _configService.Save();
                    }
                    ShowSettingTooltip("Where to display the legend: outside the graph or inside at a corner.", "Outside (right)");

                    if (settings.LegendPosition == LegendPosition.Outside)
                    {
                        var legendWidth = settings.LegendWidth;
                        if (ImGui.SliderFloat("Legend width", ref legendWidth, 60f, 250f, "%.0f px"))
                        {
                            settings.LegendWidth = legendWidth;
                            _configService.Save();
                        }
                        ShowSettingTooltip("Width of the scrollable legend panel.", "140");
                    }
                    else
                    {
                        var legendHeight = settings.LegendHeightPercent;
                        if (ImGui.SliderFloat("Legend height", ref legendHeight, 10f, 80f, "%.0f %%"))
                        {
                            settings.LegendHeightPercent = legendHeight;
                            _configService.Save();
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
                _configService.Save();
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
                _configService.Save();
            }

            // Refresh character list button
            ImGui.Spacing();
            if (ImGui.Button("Refresh Character List"))
            {
                RefreshCharacterList();
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
