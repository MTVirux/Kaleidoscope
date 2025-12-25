using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
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
    
    // Instance-specific settings (not shared with other tool instances)
    private readonly CrystalTrackerSettings _instanceSettings;

    private static readonly string[] GroupingNames = { "None (Total)", "By Character", "By Element", "By Character & Element", "By Tier", "By Character & Tier" };
    private static readonly string[] LegendPositionNames = { "Outside (right)", "Inside Top-Left", "Inside Top-Right", "Inside Bottom-Left", "Inside Bottom-Right" };
    private static readonly string[] TimeUnitNames = { "Seconds", "Minutes", "Hours", "Days", "Weeks" };

    private CrystalTrackerSettings Settings => _instanceSettings;
    
    public CrystalTrackerTool(CrystalTrackerComponent inner, ConfigurationService configService)
    {
        _inner = inner;
        _configService = configService;
        
        // Initialize instance-specific settings with defaults
        _instanceSettings = new CrystalTrackerSettings();
        
        // Link the inner component to use our instance settings
        _inner.SetSettingsProvider(() => _instanceSettings);
        
        // Subscribe to settings changes from inner component
        _inner.OnSettingsChanged += () => NotifyToolSettingsChanged();
        
        Title = "Crystals";
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
            if (!ImGui.CollapsingHeader("Crystal Tracker Settings", ImGuiTreeNodeFlags.DefaultOpen))
                return;
                
            var settings = Settings;

            // Grouping section
            ImGui.TextUnformatted("Data Grouping");
            ImGui.Separator();

            var grouping = (int)settings.Grouping;
            if (ImGui.Combo("Group by", ref grouping, GroupingNames, GroupingNames.Length))
            {
                settings.Grouping = (CrystalGrouping)grouping;
                NotifyToolSettingsChanged();
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
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Include Shards (lowest tier) in the count.", "On");

            var includeCrystals = settings.IncludeCrystals;
            if (ImGui.Checkbox("Include Crystals", ref includeCrystals))
            {
                settings.IncludeCrystals = includeCrystals;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Include Crystals (middle tier) in the count.", "On");

            var includeClusters = settings.IncludeClusters;
            if (ImGui.Checkbox("Include Clusters", ref includeClusters))
            {
                settings.IncludeClusters = includeClusters;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Include Clusters (highest tier) in the count.", "On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Data Source");
            ImGui.Separator();

            // Show info about retainer tracking instead of a toggle
            ImGui.TextDisabled("Retainer crystals are always tracked.");
            ShowSettingTooltip("Crystal data includes both player inventory and cached retainer inventories. Retainer data is updated when you access each retainer.", "Always On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Element Filters");
            ImGui.Separator();

            // Element filter row 1
            var fire = settings.IncludeFire;
            if (ImGui.Checkbox("Fire", ref fire))
            {
                settings.IncludeFire = fire;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            var ice = settings.IncludeIce;
            if (ImGui.Checkbox("Ice", ref ice))
            {
                settings.IncludeIce = ice;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            var wind = settings.IncludeWind;
            if (ImGui.Checkbox("Wind", ref wind))
            {
                settings.IncludeWind = wind;
                NotifyToolSettingsChanged();
            }

            // Element filter row 2
            var earth = settings.IncludeEarth;
            if (ImGui.Checkbox("Earth", ref earth))
            {
                settings.IncludeEarth = earth;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            var lightning = settings.IncludeLightning;
            if (ImGui.Checkbox("Lightning", ref lightning))
            {
                settings.IncludeLightning = lightning;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            var water = settings.IncludeWater;
            if (ImGui.Checkbox("Water", ref water))
            {
                settings.IncludeWater = water;
                NotifyToolSettingsChanged();
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Display Settings");
            ImGui.Separator();

            var showLegend = settings.ShowLegend;
            if (ImGui.Checkbox("Show legend", ref showLegend))
            {
                settings.ShowLegend = showLegend;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Shows a scrollable legend panel on the right side of the graph.", "On");

            if (showLegend)
            {
                var legendPosition = (int)settings.LegendPosition;
                if (ImGui.Combo("Legend position", ref legendPosition, LegendPositionNames, LegendPositionNames.Length))
                {
                    settings.LegendPosition = (LegendPosition)legendPosition;
                    NotifyToolSettingsChanged();
                }
                ShowSettingTooltip("Where to display the legend: outside the graph or inside at a corner.", "Outside (right)");
                
                if (settings.LegendPosition == LegendPosition.Outside)
                {
                    var legendWidth = settings.LegendWidth;
                    if (ImGui.SliderFloat("Legend width", ref legendWidth, 60f, 250f, "%.0f px"))
                    {
                        settings.LegendWidth = legendWidth;
                        NotifyToolSettingsChanged();
                    }
                    ShowSettingTooltip("Width of the scrollable legend panel.", "140");
                }
                else
                {
                    var legendHeight = settings.LegendHeightPercent;
                    if (ImGui.SliderFloat("Legend height", ref legendHeight, 10f, 80f, "%.0f %%"))
                    {
                        settings.LegendHeightPercent = legendHeight;
                        NotifyToolSettingsChanged();
                    }
                    ShowSettingTooltip("Maximum height of the inside legend as a percentage of the graph height.", "25%");
                }
            }

            var showValueLabel = settings.ShowValueLabel;
            if (ImGui.Checkbox("Show current value label", ref showValueLabel))
            {
                settings.ShowValueLabel = showValueLabel;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Shows the current value as a small label near the latest data point.", "Off");

            if (showValueLabel)
            {
                var labelOffsetX = settings.ValueLabelOffsetX;
                if (ImGui.SliderFloat("Label X offset", ref labelOffsetX, -100f, 100f, "%.0f"))
                {
                    settings.ValueLabelOffsetX = labelOffsetX;
                    NotifyToolSettingsChanged();
                }

                var labelOffsetY = settings.ValueLabelOffsetY;
                if (ImGui.SliderFloat("Label Y offset", ref labelOffsetY, -50f, 50f, "%.0f"))
                {
                    settings.ValueLabelOffsetY = labelOffsetY;
                    NotifyToolSettingsChanged();
                }
            }

            var graphType = settings.GraphType;
            if (GraphTypeSelectorWidget.Draw("Graph type", ref graphType))
            {
                settings.GraphType = graphType;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("The visual style for the graph.\nArea: Filled area chart.\nLine: Simple line chart.\nStairs: Step chart showing discrete changes.\nBars: Vertical bar chart.", "Area");

            var showXAxisTimestamps = settings.ShowXAxisTimestamps;
            if (ImGui.Checkbox("Show X-axis timestamps", ref showXAxisTimestamps))
            {
                settings.ShowXAxisTimestamps = showXAxisTimestamps;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Shows time labels on the X-axis.", "On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Auto-Scroll");
            ImGui.Separator();
            
            var showControlsDrawer = settings.ShowControlsDrawer;
            if (ImGui.Checkbox("Show controls drawer", ref showControlsDrawer))
            {
                settings.ShowControlsDrawer = showControlsDrawer;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Shows a small toggle button in the top-right corner of the graph to access auto-scroll controls.", "On");
            
            var autoScrollEnabled = settings.AutoScrollEnabled;
            if (ImGui.Checkbox("Auto-scroll enabled", ref autoScrollEnabled))
            {
                settings.AutoScrollEnabled = autoScrollEnabled;
                NotifyToolSettingsChanged();
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
                    NotifyToolSettingsChanged();
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                var unitIndex = (int)settings.AutoScrollTimeUnit;
                if (ImGui.Combo("##autoscroll_unit", ref unitIndex, TimeUnitNames, TimeUnitNames.Length))
                {
                    settings.AutoScrollTimeUnit = (AutoScrollTimeUnit)unitIndex;
                    NotifyToolSettingsChanged();
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
                NotifyToolSettingsChanged();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CrystalTrackerTool] DrawSettings error: {ex.Message}");
        }
    }
    
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var s = _instanceSettings;
        return new Dictionary<string, object?>
        {
            ["Grouping"] = (int)s.Grouping,
            ["IncludeShards"] = s.IncludeShards,
            ["IncludeCrystals"] = s.IncludeCrystals,
            ["IncludeClusters"] = s.IncludeClusters,
            ["IncludeFire"] = s.IncludeFire,
            ["IncludeIce"] = s.IncludeIce,
            ["IncludeWind"] = s.IncludeWind,
            ["IncludeEarth"] = s.IncludeEarth,
            ["IncludeLightning"] = s.IncludeLightning,
            ["IncludeWater"] = s.IncludeWater,
            ["IncludeRetainers"] = s.IncludeRetainers,
            ["TimeRangeValue"] = s.TimeRangeValue,
            ["TimeRangeUnit"] = (int)s.TimeRangeUnit,
            ["ShowXAxisTimestamps"] = s.ShowXAxisTimestamps,
            ["ShowValueLabel"] = s.ShowValueLabel,
            ["ValueLabelOffsetX"] = s.ValueLabelOffsetX,
            ["ValueLabelOffsetY"] = s.ValueLabelOffsetY,
            ["LegendWidth"] = s.LegendWidth,
            ["LegendHeightPercent"] = s.LegendHeightPercent,
            ["ShowLegend"] = s.ShowLegend,
            ["LegendPosition"] = (int)s.LegendPosition,
            ["GraphType"] = (int)s.GraphType,
            ["AutoScrollEnabled"] = s.AutoScrollEnabled,
            ["AutoScrollTimeValue"] = s.AutoScrollTimeValue,
            ["AutoScrollTimeUnit"] = (int)s.AutoScrollTimeUnit,
            ["AutoScrollNowPosition"] = s.AutoScrollNowPosition,
            ["ShowControlsDrawer"] = s.ShowControlsDrawer
        };
    }
    
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        var t = _instanceSettings;
        
        t.Grouping = (CrystalGrouping)GetSetting(settings, "Grouping", (int)t.Grouping);
        t.IncludeShards = GetSetting(settings, "IncludeShards", t.IncludeShards);
        t.IncludeCrystals = GetSetting(settings, "IncludeCrystals", t.IncludeCrystals);
        t.IncludeClusters = GetSetting(settings, "IncludeClusters", t.IncludeClusters);
        t.IncludeFire = GetSetting(settings, "IncludeFire", t.IncludeFire);
        t.IncludeIce = GetSetting(settings, "IncludeIce", t.IncludeIce);
        t.IncludeWind = GetSetting(settings, "IncludeWind", t.IncludeWind);
        t.IncludeEarth = GetSetting(settings, "IncludeEarth", t.IncludeEarth);
        t.IncludeLightning = GetSetting(settings, "IncludeLightning", t.IncludeLightning);
        t.IncludeWater = GetSetting(settings, "IncludeWater", t.IncludeWater);
        t.IncludeRetainers = GetSetting(settings, "IncludeRetainers", t.IncludeRetainers);
        t.TimeRangeValue = GetSetting(settings, "TimeRangeValue", t.TimeRangeValue);
        t.TimeRangeUnit = (TimeRangeUnit)GetSetting(settings, "TimeRangeUnit", (int)t.TimeRangeUnit);
        t.ShowXAxisTimestamps = GetSetting(settings, "ShowXAxisTimestamps", t.ShowXAxisTimestamps);
        t.ShowValueLabel = GetSetting(settings, "ShowValueLabel", t.ShowValueLabel);
        t.ValueLabelOffsetX = GetSetting(settings, "ValueLabelOffsetX", t.ValueLabelOffsetX);
        t.ValueLabelOffsetY = GetSetting(settings, "ValueLabelOffsetY", t.ValueLabelOffsetY);
        t.LegendWidth = GetSetting(settings, "LegendWidth", t.LegendWidth);
        t.LegendHeightPercent = GetSetting(settings, "LegendHeightPercent", t.LegendHeightPercent);
        t.ShowLegend = GetSetting(settings, "ShowLegend", t.ShowLegend);
        t.LegendPosition = (LegendPosition)GetSetting(settings, "LegendPosition", (int)t.LegendPosition);
        t.GraphType = (GraphType)GetSetting(settings, "GraphType", (int)t.GraphType);
        t.AutoScrollEnabled = GetSetting(settings, "AutoScrollEnabled", t.AutoScrollEnabled);
        t.AutoScrollTimeValue = GetSetting(settings, "AutoScrollTimeValue", t.AutoScrollTimeValue);
        t.AutoScrollTimeUnit = (AutoScrollTimeUnit)GetSetting(settings, "AutoScrollTimeUnit", (int)t.AutoScrollTimeUnit);
        t.AutoScrollNowPosition = GetSetting(settings, "AutoScrollNowPosition", t.AutoScrollNowPosition);
        t.ShowControlsDrawer = GetSetting(settings, "ShowControlsDrawer", t.ShowControlsDrawer);
    }
}
