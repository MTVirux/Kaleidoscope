using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.GilTracker;

/// <summary>
/// Tool component wrapper for the Gil Tracker feature.
/// </summary>
public class GilTrackerTool : ToolComponent
{
    private readonly GilTrackerComponent _inner;
    private readonly ConfigurationService _configService;

    private static readonly string[] TimeRangeUnitNames = { "Minutes", "Hours", "Days", "Weeks", "Months", "All (no limit)" };

    private Configuration Config => _configService.Config;

    public GilTrackerTool(GilTrackerComponent inner, ConfigurationService configService)
    {
        _inner = inner;
        _configService = configService;
        Title = "Gil Tracker";
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
            // Display settings section
            ImGui.TextUnformatted("Display Settings");
            ImGui.Separator();

            var hideCharSelector = Config.GilTrackerHideCharacterSelector;
            if (ImGui.Checkbox("Hide character selector", ref hideCharSelector))
            {
                Config.GilTrackerHideCharacterSelector = hideCharSelector;
                _configService.Save();
            }
            ShowSettingTooltip("Hides the character selection dropdown from the GilTracker display.", "Off");

            var showMultipleLines = Config.GilTrackerShowMultipleLines;
            if (ImGui.Checkbox("Show multiple lines (per character)", ref showMultipleLines))
            {
                Config.GilTrackerShowMultipleLines = showMultipleLines;
                _configService.Save();
            }
            ShowSettingTooltip("When viewing 'All', shows a separate line for each character instead of a combined total.", "Off");

            var showLatestValue = Config.GilTrackerShowLatestValue;
            if (ImGui.Checkbox("Show latest value at line end", ref showLatestValue))
            {
                Config.GilTrackerShowLatestValue = showLatestValue;
                _configService.Save();
            }
            ShowSettingTooltip("Displays the most recent recorded value next to the end of the graph line.", "Off");

            var showValueLabel = Config.GilTrackerShowValueLabel;
            if (ImGui.Checkbox("Show current value label", ref showValueLabel))
            {
                Config.GilTrackerShowValueLabel = showValueLabel;
                _configService.Save();
            }
            ShowSettingTooltip("Shows the current value as a small label near the latest data point.", "Off");

            if (showValueLabel)
            {
                var labelOffsetX = Config.GilTrackerValueLabelOffsetX;
                if (ImGui.SliderFloat("Label X offset", ref labelOffsetX, -100f, 100f, "%.0f"))
                {
                    Config.GilTrackerValueLabelOffsetX = labelOffsetX;
                    _configService.Save();
                }
                ShowSettingTooltip("Horizontal offset for the value label. Negative = left, positive = right.", "0");

                var labelOffsetY = Config.GilTrackerValueLabelOffsetY;
                if (ImGui.SliderFloat("Label Y offset", ref labelOffsetY, -50f, 50f, "%.0f"))
                {
                    Config.GilTrackerValueLabelOffsetY = labelOffsetY;
                    _configService.Save();
                }
                ShowSettingTooltip("Vertical offset for the value label. Negative = up, positive = down.", "0");
            }

            var showEndGap = Config.GilTrackerShowEndGap;
            if (ImGui.Checkbox("Leave gap at graph end", ref showEndGap))
            {
                Config.GilTrackerShowEndGap = showEndGap;
                _configService.Save();
            }
            ShowSettingTooltip("Leaves empty space at the right edge of the graph so the line doesn't touch the border.", "Off");

            if (showEndGap)
            {
                var endGapPercent = Config.GilTrackerEndGapPercent;
                if (ImGui.SliderFloat("End gap %", ref endGapPercent, 1f, 25f, "%.0f%%"))
                {
                    Config.GilTrackerEndGapPercent = endGapPercent;
                    _configService.Save();
                }
                ShowSettingTooltip("Percentage of the graph width to leave empty at the right edge.", "5%");
            }

            ImGui.Spacing();

            // Time range section
            ImGui.TextUnformatted("Time Range");
            ImGui.Separator();

            var timeRangeUnit = (int)Config.GilTrackerTimeRangeUnit;
            if (ImGui.Combo("Time unit", ref timeRangeUnit, TimeRangeUnitNames, TimeRangeUnitNames.Length))
            {
                Config.GilTrackerTimeRangeUnit = (TimeRangeUnit)timeRangeUnit;
                _configService.Save();
            }
            ShowSettingTooltip("Select the time unit for filtering how far back to show data.", "All");

            if (Config.GilTrackerTimeRangeUnit != TimeRangeUnit.All)
            {
                var timeRangeValue = Config.GilTrackerTimeRangeValue;
                if (ImGui.InputInt("Time range value", ref timeRangeValue))
                {
                    if (timeRangeValue < 1) timeRangeValue = 1;
                    Config.GilTrackerTimeRangeValue = timeRangeValue;
                    _configService.Save();
                }
                ShowSettingTooltip("How many units of time back to display data for.", "7");
            }

            ImGui.Spacing();

            // Graph bounds section
            ImGui.TextUnformatted("Graph Bounds (Y-axis)");
            ImGui.Separator();

            var autoScale = Config.GilTrackerAutoScaleGraph;
            if (ImGui.Checkbox("Auto-scale to data", ref autoScale))
            {
                Config.GilTrackerAutoScaleGraph = autoScale;
                _configService.Save();
            }
            ShowSettingTooltip("Automatically adjusts the Y-axis range based on actual data values.", "On");

            if (!autoScale)
            {
                var min = _inner.GraphMinValue;
                var max = _inner.GraphMaxValue;

                if (ImGui.InputFloat("Min value", ref min, 0f, 0f, "%.0f"))
                {
                    // Ensure min isn't greater than or equal to max - clamp
                    if (min >= max) min = MathF.Max(0f, max - 1f);
                    _inner.GraphMinValue = min;
                }
                ShowSettingTooltip("Minimum Y value displayed on the graph. Values below this will be clamped.", "0");

                if (ImGui.InputFloat("Max value", ref max, 0f, 0f, "%.0f"))
                {
                    if (max <= min) max = MathF.Max(min + 1f, min + 1f);
                    _inner.GraphMaxValue = max;
                }
                ShowSettingTooltip($"Maximum Y value displayed on the graph. Values above this will be clamped.", ConfigStatic.GilTrackerMaxGil.ToString("N0"));
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Error drawing GilTracker settings", ex);
        }
    }
}
