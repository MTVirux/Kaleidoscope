using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
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

    private static readonly string[] TimeRangeUnitNames = { "Minutes", "Hours", "Days", "Weeks", "Months", "All (no limit)" };

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

            // Display settings section
            ImGui.TextUnformatted($"{displayName} Settings");
            ImGui.Separator();

            var hideCharSelector = settings.HideCharacterSelector;
            if (ImGui.Checkbox("Hide character selector", ref hideCharSelector))
            {
                settings.HideCharacterSelector = hideCharSelector;
                _configService.Save();
            }
            ShowSettingTooltip("Hides the character selection dropdown from the display.", "Off");

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
                    var legendWidth = settings.LegendWidth;
                    if (ImGui.SliderFloat("Legend width", ref legendWidth, 60f, 200f, "%.0f px"))
                    {
                        settings.LegendWidth = legendWidth;
                        _configService.Save();
                    }
                    ShowSettingTooltip("Width of the scrollable legend panel on the right side of the graph.", "120");
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

            var autoScale = settings.AutoScaleGraph;
            if (ImGui.Checkbox("Auto-scale graph", ref autoScale))
            {
                settings.AutoScaleGraph = autoScale;
                _configService.Save();
            }
            ShowSettingTooltip("Automatically scales the graph Y-axis to fit the data range.", "On");

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
            ShowSettingTooltip("Number of time units to display.", "7");

            if (ImGui.Combo("Range unit", ref timeRangeUnit, TimeRangeUnitNames, TimeRangeUnitNames.Length))
            {
                settings.TimeRangeUnit = (TimeRangeUnit)timeRangeUnit;
                _configService.Save();
            }
            ShowSettingTooltip("Time unit for the display range.", "All");
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerTool:{DataType}] DrawSettings error: {ex.Message}");
        }
    }
}
