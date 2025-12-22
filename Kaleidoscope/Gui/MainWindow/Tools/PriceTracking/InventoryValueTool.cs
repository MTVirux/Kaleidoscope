using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImPlot = Dalamud.Bindings.ImPlot.ImPlot;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that tracks character inventory liquid value over time.
/// Shows a time-series graph of total inventory value (items + gil).
/// </summary>
public class InventoryValueTool : ToolComponent
{
    private readonly PriceTrackingService _priceTrackingService;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;

    private static readonly string[] TimeRangeUnitNames = { "Minutes", "Hours", "Days", "Weeks", "Months", "All (no limit)" };
    private static readonly string[] GraphTypeNames = { "Area", "Line", "Stairs", "Bars" };

    // Character selection (0 = all)
    private ulong _selectedCharacterId = 0;
    private string[] _characterNames = Array.Empty<string>();
    private ulong[] _characterIds = Array.Empty<ulong>();
    private int _selectedCharacterIndex = 0;
    
    // Hidden series tracking
    private readonly HashSet<string> _hiddenSeries = new();

    private InventoryValueSettings Settings => _configService.Config.InventoryValue;
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    // Trading platform color palette
    private static class ChartColors
    {
        public static readonly Vector4 PlotBackground = new(0.08f, 0.09f, 0.10f, 1f);
        public static readonly Vector4 Bullish = new(0.20f, 0.90f, 0.40f, 1f);
        public static readonly Vector4 Neutral = new(1.0f, 0.85f, 0.0f, 1f);
        public static readonly Vector4 GridLine = new(0.18f, 0.20f, 0.22f, 0.6f);
    }

    // Color palette for multi-character mode
    private static readonly Vector4[] SeriesColors = new[]
    {
        new Vector4(0.20f, 0.90f, 0.40f, 1f),  // Green
        new Vector4(0.3f, 0.7f, 0.9f, 1f),     // Blue
        new Vector4(0.9f, 0.5f, 0.2f, 1f),     // Orange
        new Vector4(0.9f, 0.3f, 0.5f, 1f),     // Pink
        new Vector4(0.7f, 0.4f, 0.9f, 1f),     // Purple
        new Vector4(0.9f, 0.9f, 0.3f, 1f),     // Yellow
        new Vector4(0.4f, 0.9f, 0.9f, 1f),     // Cyan
        new Vector4(0.9f, 0.3f, 0.3f, 1f),     // Red
    };

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
        
        RefreshCharacterList();
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
        var availableSize = ImGui.GetContentRegionAvail();
        
        // Get time range
        var timeRange = GetTimeRange();
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : DateTime.MinValue;

        // Get data
        List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> data;
        Dictionary<ulong, List<(DateTime, long)>>? perCharacterData = null;

        if (_selectedCharacterId == 0 && settings.ShowMultipleLines)
        {
            // Multi-character mode
            perCharacterData = new();
            var allData = DbService.GetAllInventoryValueHistory();
            
            foreach (var entry in allData)
            {
                if (startTime != DateTime.MinValue && entry.Timestamp < startTime)
                    continue;

                if (!perCharacterData.ContainsKey(entry.CharacterId))
                    perCharacterData[entry.CharacterId] = new();
                
                perCharacterData[entry.CharacterId].Add((entry.Timestamp, entry.TotalValue));
            }
            
            data = new(); // Not used in multi-line mode
        }
        else if (_selectedCharacterId == 0)
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
            data = DbService.GetInventoryValueHistory(_selectedCharacterId);
            
            if (startTime != DateTime.MinValue)
            {
                data = data.Where(d => d.Timestamp >= startTime).ToList();
            }
        }

        // Calculate graph area
        float legendWidth = settings.ShowMultipleLines && settings.ShowLegend ? settings.LegendWidth : 0;
        var graphWidth = availableSize.X - legendWidth - 10;
        var graphHeight = availableSize.Y - 5;

        if (graphWidth <= 0 || graphHeight <= 0) return;

        // Draw legend if multi-line mode
        if (perCharacterData != null && settings.ShowLegend)
        {
            DrawLegend(perCharacterData, legendWidth, graphHeight);
            ImGui.SameLine();
        }

        // Draw the plot
        if (perCharacterData != null)
        {
            DrawMultiLineGraph(perCharacterData, graphWidth, graphHeight);
        }
        else
        {
            DrawSingleLineGraph(data, graphWidth, graphHeight, settings.IncludeGil);
        }
    }

    private void DrawLegend(Dictionary<ulong, List<(DateTime, long)>> perCharacterData, float width, float height)
    {
        if (ImGui.BeginChild("##ValueLegend", new Vector2(width, height), false))
        {
            int colorIdx = 0;
            foreach (var (charId, points) in perCharacterData)
            {
                var charName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(charId) ?? $"Char {charId}";
                var color = SeriesColors[colorIdx % SeriesColors.Length];
                var isHidden = _hiddenSeries.Contains(charName);

                // Colored bullet
                ImGui.TextColored(isHidden ? new Vector4(0.5f, 0.5f, 0.5f, 1f) : color, "●");
                ImGui.SameLine();
                
                var clicked = ImGui.Selectable(charName, false);
                if (clicked)
                {
                    if (isHidden)
                        _hiddenSeries.Remove(charName);
                    else
                        _hiddenSeries.Add(charName);
                }

                colorIdx++;
            }
            ImGui.EndChild();
        }
    }

    private void DrawMultiLineGraph(Dictionary<ulong, List<(DateTime, long)>> perCharacterData, float width, float height)
    {
        if (!ImPlot.BeginPlot("##InventoryValuePlot", new Vector2(width, height)))
            return;

        try
        {
            var now = DateTime.UtcNow;
            var settings = Settings;

            // Setup axes
            ImPlot.SetupAxes("", "", ImPlotAxisFlags.None, ImPlotAxisFlags.None);

            int colorIdx = 0;
            foreach (var (charId, points) in perCharacterData)
            {
                if (points.Count == 0) continue;

                var charName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(charId) ?? $"Char {charId}";
                if (_hiddenSeries.Contains(charName))
                {
                    colorIdx++;
                    continue;
                }

                var color = SeriesColors[colorIdx % SeriesColors.Length];
                var xValues = points.Select(p => (now - p.Item1).TotalSeconds * -1).ToArray();
                var yValues = points.Select(p => (double)p.Item2).ToArray();

                ImPlot.SetNextLineStyle(color);
                
                switch (settings.GraphType)
                {
                    case GraphType.Line:
                        ImPlot.PlotLine(charName, ref xValues[0], ref yValues[0], points.Count);
                        break;
                    case GraphType.Stairs:
                        ImPlot.PlotStairs(charName, ref xValues[0], ref yValues[0], points.Count);
                        break;
                    case GraphType.Bars:
                        ImPlot.PlotBars(charName, ref xValues[0], ref yValues[0], points.Count, 100);
                        break;
                    default: // Area
                        ImPlot.PlotShaded(charName, ref xValues[0], ref yValues[0], points.Count);
                        ImPlot.PlotLine(charName + "##line", ref xValues[0], ref yValues[0], points.Count);
                        break;
                }

                colorIdx++;
            }
        }
        finally
        {
            ImPlot.EndPlot();
        }
    }

    private void DrawSingleLineGraph(List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> data, 
        float width, float height, bool includeGil)
    {
        if (!ImPlot.BeginPlot("##InventoryValuePlot", new Vector2(width, height)))
            return;

        try
        {
            if (data.Count == 0)
            {
                ImPlot.EndPlot();
                ImGui.TextDisabled("No value history data");
                return;
            }

            var now = DateTime.UtcNow;
            var settings = Settings;

            // Prepare data
            var xValues = data.Select(d => (now - d.Timestamp).TotalSeconds * -1).ToArray();
            var yValues = data.Select(d => (double)(includeGil ? d.TotalValue : d.ItemValue)).ToArray();

            // Setup axes
            ImPlot.SetupAxes("", "", ImPlotAxisFlags.None, ImPlotAxisFlags.None);

            if (settings.AutoScaleGraph && yValues.Length > 0)
            {
                var minY = yValues.Min() * 0.9;
                var maxY = yValues.Max() * 1.1;
                ImPlot.SetupAxisLimits(ImAxis.Y1, minY, maxY, ImPlotCond.Always);
            }

            ImPlot.SetNextLineStyle(ChartColors.Bullish);
            ImPlot.SetNextFillStyle(new Vector4(ChartColors.Bullish.X, ChartColors.Bullish.Y, ChartColors.Bullish.Z, 0.3f));

            switch (settings.GraphType)
            {
                case GraphType.Line:
                    ImPlot.PlotLine("Value", ref xValues[0], ref yValues[0], data.Count);
                    break;
                case GraphType.Stairs:
                    ImPlot.PlotStairs("Value", ref xValues[0], ref yValues[0], data.Count);
                    break;
                case GraphType.Bars:
                    ImPlot.PlotBars("Value", ref xValues[0], ref yValues[0], data.Count, 100);
                    break;
                default: // Area
                    ImPlot.PlotShaded("Value", ref xValues[0], ref yValues[0], data.Count);
                    ImPlot.PlotLine("Value##line", ref xValues[0], ref yValues[0], data.Count);
                    break;
            }

            // Current value label
            if (yValues.Length > 0)
            {
                var lastValue = yValues[^1];
                var valueStr = FormatGil((long)lastValue);
                
                // Draw annotation at the last point
                ImPlot.Annotation(xValues[^1], lastValue, ChartColors.Neutral, new Vector2(10, -10), false, valueStr);
            }
        }
        finally
        {
            ImPlot.EndPlot();
        }
    }

    private TimeSpan? GetTimeRange()
    {
        var settings = Settings;
        return settings.TimeRangeUnit switch
        {
            TimeRangeUnit.Minutes => TimeSpan.FromMinutes(settings.TimeRangeValue),
            TimeRangeUnit.Hours => TimeSpan.FromHours(settings.TimeRangeValue),
            TimeRangeUnit.Days => TimeSpan.FromDays(settings.TimeRangeValue),
            TimeRangeUnit.Weeks => TimeSpan.FromDays(settings.TimeRangeValue * 7),
            TimeRangeUnit.Months => TimeSpan.FromDays(settings.TimeRangeValue * 30),
            TimeRangeUnit.All => null,
            _ => null
        };
    }

    private static string FormatGil(long amount)
    {
        if (amount >= 1_000_000_000)
            return $"{amount / 1_000_000_000.0:F2}B";
        if (amount >= 1_000_000)
            return $"{amount / 1_000_000.0:F1}M";
        if (amount >= 1_000)
            return $"{amount / 1_000.0:F1}K";
        return amount.ToString("N0");
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
                    var legendWidth = settings.LegendWidth;
                    if (ImGui.SliderFloat("Legend width", ref legendWidth, 60f, 250f, "%.0f px"))
                    {
                        settings.LegendWidth = legendWidth;
                        _configService.Save();
                    }
                }
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Graph Settings");
            ImGui.Separator();

            var graphType = (int)settings.GraphType;
            if (ImGui.Combo("Graph type", ref graphType, GraphTypeNames, GraphTypeNames.Length))
            {
                settings.GraphType = (GraphType)graphType;
                _configService.Save();
            }
            ShowSettingTooltip("Visual style for the graph.", "Area");

            var autoScale = settings.AutoScaleGraph;
            if (ImGui.Checkbox("Auto-scale graph", ref autoScale))
            {
                settings.AutoScaleGraph = autoScale;
                _configService.Save();
            }
            ShowSettingTooltip("Automatically scale the Y-axis to fit the data.", "On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Time Range");
            ImGui.Separator();

            var timeRangeValue = settings.TimeRangeValue;
            var timeRangeUnit = (int)settings.TimeRangeUnit;

            if (ImGui.InputInt("Range value", ref timeRangeValue, 1, 10))
            {
                if (timeRangeValue < 1) timeRangeValue = 1;
                settings.TimeRangeValue = timeRangeValue;
                _configService.Save();
            }

            if (ImGui.Combo("Range unit", ref timeRangeUnit, TimeRangeUnitNames, TimeRangeUnitNames.Length))
            {
                settings.TimeRangeUnit = (TimeRangeUnit)timeRangeUnit;
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
        // No resources to dispose
    }
}
