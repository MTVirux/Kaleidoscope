using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.DataTracker;

/// <summary>
/// Tool component wrapper for data tracking features.
/// Can track any TrackedDataType.
/// </summary>
public class DataTrackerTool : ToolComponent
{
    private readonly DataTrackerComponent _inner;
    private readonly ConfigurationService _configService;

    private static readonly string[] LegendPositionNames = { "Outside (right)", "Inside Top-Left", "Inside Top-Right", "Inside Bottom-Left", "Inside Bottom-Right" };
    
    private static readonly string[] TimeUnitNames = { "Seconds", "Minutes", "Hours", "Days", "Weeks" };

    private Configuration Config => _configService.Config;

    /// <summary>
    /// Gets the data type this tool tracks.
    /// </summary>
    public TrackedDataType DataType => _inner.DataType;

    /// <summary>
    /// Gets settings for this specific data type.
    /// </summary>
    private DataTrackerSettings GetSettings()
    {
        if (!Config.DataTrackerSettings.TryGetValue(DataType, out var settings))
        {
            settings = new DataTrackerSettings();
            Config.DataTrackerSettings[DataType] = settings;
        }
        return settings;
    }
    
    public DataTrackerTool(DataTrackerComponent inner, ConfigurationService configService)
    {
        _inner = inner;
        _configService = configService;
        
        var def = inner.Definition;
        Title = def?.ShortName ?? inner.DataType.ToString();
        Size = ConfigStatic.GilTrackerToolSize;
    }
    
    /// <summary>
    /// Gets the hidden series names from the graph widget.
    /// </summary>
    public IReadOnlyCollection<string> HiddenSeries => _inner.HiddenSeries;
    
    /// <summary>
    /// Sets the hidden series names on the graph widget.
    /// </summary>
    public void SetHiddenSeries(IEnumerable<string>? seriesNames) => _inner.SetHiddenSeries(seriesNames);

    public override void DrawContent()
    {
        _inner.Draw();
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            var settings = GetSettings();
            var def = _inner.Definition;
            var displayName = def?.DisplayName ?? DataType.ToString();

            if (!ImGui.CollapsingHeader($"{displayName} Settings", ImGuiTreeNodeFlags.DefaultOpen))
                return;

            var hideCharSelector = settings.HideCharacterSelector;
            if (ImGui.Checkbox("Hide character selector", ref hideCharSelector))
            {
                settings.HideCharacterSelector = hideCharSelector;
                _configService.Save();
            }
            ShowSettingTooltip("Hides the character selection dropdown from the display.", "Off");
            
            var multiSelectEnabled = settings.MultiSelectEnabled;
            if (ImGui.Checkbox("Enable multi-character selection", ref multiSelectEnabled))
            {
                settings.MultiSelectEnabled = multiSelectEnabled;
                _inner.SetMultiSelectMode(multiSelectEnabled);
                _configService.Save();
            }
            ShowSettingTooltip("Allows selecting multiple specific characters instead of just one or all.", "Off");

            var showMultipleLines = settings.ShowMultipleLines;
            if (ImGui.Checkbox("Show multiple lines (per character)", ref showMultipleLines))
            {
                settings.ShowMultipleLines = showMultipleLines;
                _configService.Save();
            }
            ShowSettingTooltip("When viewing 'All', shows a separate line for each character instead of a combined total.", "Off");

            if (showMultipleLines)
            {
                var showLegend = settings.ShowLegend;
                if (ImGui.Checkbox("Show legend", ref showLegend))
                {
                    settings.ShowLegend = showLegend;
                    _configService.Save();
                }
                ShowSettingTooltip("Shows a scrollable legend panel on the right side of the graph.", "On");

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
                        ShowSettingTooltip("Width of the scrollable legend panel on the right side of the graph.", "140");
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

            var showValueLabel = settings.ShowValueLabel;
            if (ImGui.Checkbox("Show current value label", ref showValueLabel))
            {
                settings.ShowValueLabel = showValueLabel;
                _configService.Save();
            }
            ShowSettingTooltip("Shows the current value as a small label near the latest data point.", "Off");

            if (showValueLabel)
            {
                var labelOffsetX = settings.ValueLabelOffsetX;
                if (ImGui.SliderFloat("Label X offset", ref labelOffsetX, -100f, 100f, "%.0f"))
                {
                    settings.ValueLabelOffsetX = labelOffsetX;
                    _configService.Save();
                }
                ShowSettingTooltip("Horizontal offset for the value label. Negative = left, positive = right.", "0");

                var labelOffsetY = settings.ValueLabelOffsetY;
                if (ImGui.SliderFloat("Label Y offset", ref labelOffsetY, -50f, 50f, "%.0f"))
                {
                    settings.ValueLabelOffsetY = labelOffsetY;
                    _configService.Save();
                }
                ShowSettingTooltip("Vertical offset for the value label. Negative = up, positive = down.", "0");
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
            ShowSettingTooltip("The visual style for the graph.\nArea: Filled area chart.\nLine: Simple line chart.\nStairs: Step chart showing discrete changes.\nBars: Vertical bar chart.", "Area");

            var showXAxisTimestamps = settings.ShowXAxisTimestamps;
            if (ImGui.Checkbox("Show X-axis timestamps", ref showXAxisTimestamps))
            {
                settings.ShowXAxisTimestamps = showXAxisTimestamps;
                _configService.Save();
            }
            ShowSettingTooltip("Shows time labels on the X-axis.", "On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Auto-Scroll");
            ImGui.Separator();
            
            var showControlsDrawer = settings.ShowControlsDrawer;
            if (ImGui.Checkbox("Show controls drawer", ref showControlsDrawer))
            {
                settings.ShowControlsDrawer = showControlsDrawer;
                _configService.Save();
            }
            ShowSettingTooltip("Shows a small toggle button in the top-right corner of the graph to access auto-scroll controls.", "On");
            
            var autoScrollEnabled = settings.AutoScrollEnabled;
            if (ImGui.Checkbox("Auto-scroll enabled", ref autoScrollEnabled))
            {
                settings.AutoScrollEnabled = autoScrollEnabled;
                _configService.Save();
            }
            ShowSettingTooltip("When enabled, the graph automatically scrolls to show the most recent data.", "Off");
            
            if (autoScrollEnabled)
            {
                ImGui.TextUnformatted("Auto-scroll time range:");
                ImGui.SetNextItemWidth(60);
                var timeValue = settings.AutoScrollTimeValue;
                if (ImGui.InputInt("##autoscroll_value", ref timeValue))
                {
                    settings.AutoScrollTimeValue = Math.Clamp(timeValue, 1, 999);
                    _configService.Save();
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                var unitIndex = (int)settings.AutoScrollTimeUnit;
                if (ImGui.Combo("##autoscroll_unit", ref unitIndex, TimeUnitNames, TimeUnitNames.Length))
                {
                    settings.AutoScrollTimeUnit = (TimeUnit)unitIndex;
                    _configService.Save();
                }
                ShowSettingTooltip("How much time to show when auto-scrolling.", "1 Hour");
            }

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
            ShowSettingTooltip("Time range to display on the graph.", "7 Days");
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerTool:{DataType}] DrawSettings error: {ex.Message}");
        }
    }
}
