using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.CrystalTracker;

/// <summary>
/// Tool component wrapper for the unified Crystal Tracker.
/// Tracks all crystal types (shards, crystals, clusters) with flexible grouping and filtering.
/// </summary>
public class CrystalTrackerTool : ToolComponent
{
    private readonly CrystalTrackerComponent _inner;
    private readonly ConfigurationService _configService;

    private static readonly string[] TimeRangeUnitNames = { "Minutes", "Hours", "Days", "Weeks", "Months", "All (no limit)" };
    private static readonly string[] GroupingNames = { "None (Total)", "By Character", "By Element", "By Character & Element" };

    private CrystalTrackerSettings Settings => _configService.Config.CrystalTracker;

    public CrystalTrackerTool(CrystalTrackerComponent inner, ConfigurationService configService)
    {
        _inner = inner;
        _configService = configService;
        
        Title = "Crystals";
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
            var settings = Settings;

            // Grouping section
            ImGui.TextUnformatted("Data Grouping");
            ImGui.Separator();

            var grouping = (int)settings.Grouping;
            if (ImGui.Combo("Group by", ref grouping, GroupingNames, GroupingNames.Length))
            {
                settings.Grouping = (CrystalGrouping)grouping;
                _configService.Save();
            }
            ShowSettingTooltip("How to group crystal data in the display.\n" +
                "None: Single total across all.\n" +
                "By Character: Separate line per character.\n" +
                "By Element: Separate line per element.\n" +
                "By Both: Separate line per character per element.", "By Element");

            ImGui.Spacing();
            ImGui.TextUnformatted("Tier Filters");
            ImGui.Separator();

            var includeShards = settings.IncludeShards;
            if (ImGui.Checkbox("Include Shards", ref includeShards))
            {
                settings.IncludeShards = includeShards;
                _configService.Save();
            }
            ShowSettingTooltip("Include Shards (lowest tier) in the count.", "On");

            var includeCrystals = settings.IncludeCrystals;
            if (ImGui.Checkbox("Include Crystals", ref includeCrystals))
            {
                settings.IncludeCrystals = includeCrystals;
                _configService.Save();
            }
            ShowSettingTooltip("Include Crystals (middle tier) in the count.", "On");

            var includeClusters = settings.IncludeClusters;
            if (ImGui.Checkbox("Include Clusters", ref includeClusters))
            {
                settings.IncludeClusters = includeClusters;
                _configService.Save();
            }
            ShowSettingTooltip("Include Clusters (highest tier) in the count.", "On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Element Filters");
            ImGui.Separator();

            // Element filter row 1
            var fire = settings.IncludeFire;
            if (ImGui.Checkbox("Fire", ref fire))
            {
                settings.IncludeFire = fire;
                _configService.Save();
            }
            ImGui.SameLine();
            var ice = settings.IncludeIce;
            if (ImGui.Checkbox("Ice", ref ice))
            {
                settings.IncludeIce = ice;
                _configService.Save();
            }
            ImGui.SameLine();
            var wind = settings.IncludeWind;
            if (ImGui.Checkbox("Wind", ref wind))
            {
                settings.IncludeWind = wind;
                _configService.Save();
            }

            // Element filter row 2
            var earth = settings.IncludeEarth;
            if (ImGui.Checkbox("Earth", ref earth))
            {
                settings.IncludeEarth = earth;
                _configService.Save();
            }
            ImGui.SameLine();
            var lightning = settings.IncludeLightning;
            if (ImGui.Checkbox("Lightning", ref lightning))
            {
                settings.IncludeLightning = lightning;
                _configService.Save();
            }
            ImGui.SameLine();
            var water = settings.IncludeWater;
            if (ImGui.Checkbox("Water", ref water))
            {
                settings.IncludeWater = water;
                _configService.Save();
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Display Settings");
            ImGui.Separator();

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
                ShowSettingTooltip("Width of the scrollable legend panel.", "120");
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

                var labelOffsetY = settings.ValueLabelOffsetY;
                if (ImGui.SliderFloat("Label Y offset", ref labelOffsetY, -50f, 50f, "%.0f"))
                {
                    settings.ValueLabelOffsetY = labelOffsetY;
                    _configService.Save();
                }
            }

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

            if (ImGui.Combo("Range unit", ref timeRangeUnit, TimeRangeUnitNames, TimeRangeUnitNames.Length))
            {
                settings.TimeRangeUnit = (TimeRangeUnit)timeRangeUnit;
                _configService.Save();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CrystalTrackerTool] DrawSettings error: {ex.Message}");
        }
    }
}
