using Dalamud.Configuration;
using Kaleidoscope.Models;

namespace Kaleidoscope;

/// <summary>
/// Layout type for windowed vs fullscreen mode.
/// </summary>
public enum LayoutType
{
    Windowed = 0,
    Fullscreen = 1
}

/// <summary>
/// Time range unit for data filtering.
/// </summary>
public enum TimeRangeUnit
{
    Minutes = 0,
    Hours = 1,
    Days = 2,
    Weeks = 3,
    Months = 4,
    All = 5
}

/// <summary>
/// Graph visualization type for time-series data.
/// </summary>
public enum GraphType
{
    /// <summary>Filled area chart - good for showing volume over time.</summary>
    Area = 0,
    /// <summary>Simple line chart.</summary>
    Line = 1,
    /// <summary>Step/stairs chart - shows discrete value changes.</summary>
    Stairs = 2,
    /// <summary>Vertical bar chart.</summary>
    Bars = 3
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ShowOnStart { get; set; } = true;
    public bool ExclusiveFullscreen { get; set; } = false;
    public bool PinMainWindow { get; set; } = false;
    public bool PinConfigWindow { get; set; } = false;

    public Vector2 MainWindowPos { get; set; } = new(100, 100);
    public Vector2 MainWindowSize { get; set; } = new(600, 400);
    public Vector2 ConfigWindowPos { get; set; } = new(100, 100);
    public Vector2 ConfigWindowSize { get; set; } = new(600, 400);

    public Vector4 MainWindowBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    public Vector4 FullscreenBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);

    public float ContentGridCellWidthPercent { get; set; } = 25f;
    public float ContentGridCellHeightPercent { get; set; } = 25f;
    public int GridSubdivisions { get; set; } = 8;
    public bool EditMode { get; set; } = false;

    public List<ContentLayoutState> Layouts { get; set; } = new();
    public string ActiveWindowedLayoutName { get; set; } = string.Empty;
    public string ActiveFullscreenLayoutName { get; set; } = string.Empty;

    [Obsolete("Use ActiveWindowedLayoutName or ActiveFullscreenLayoutName instead")]
    public string ActiveLayoutName { get; set; } = string.Empty;

    // GilTracker settings
    public bool GilTrackerHideCharacterSelector { get; set; } = false;
    public bool GilTrackerShowMultipleLines { get; set; } = false;
    public int GilTrackerTimeRangeValue { get; set; } = 7;
    public TimeRangeUnit GilTrackerTimeRangeUnit { get; set; } = TimeRangeUnit.All;
    public bool GilTrackerShowEndGap { get; set; } = false;
    public float GilTrackerEndGapPercent { get; set; } = 5f;
    public bool GilTrackerShowValueLabel { get; set; } = false;
    public float GilTrackerValueLabelOffsetX { get; set; } = 0f;
    public float GilTrackerValueLabelOffsetY { get; set; } = 0f;
    public bool GilTrackerAutoScaleGraph { get; set; } = true;
    public float GilTrackerLegendWidth { get; set; } = 120f;
    public bool GilTrackerShowLegend { get; set; } = true;

    // GilTicker settings
    public float GilTickerScrollSpeed { get; set; } = 30f;
    public List<ulong> GilTickerDisabledCharacters { get; set; } = new();

    // Data Tracking settings
    /// <summary>
    /// Set of enabled data types for tracking. If null or empty, defaults will be used.
    /// </summary>
    public HashSet<TrackedDataType> EnabledTrackedDataTypes { get; set; } = new()
    {
        TrackedDataType.Gil,
        TrackedDataType.TomestonePoetics,
        TrackedDataType.TomestoneCapped,
        TrackedDataType.OrangeCraftersScrip,
        TrackedDataType.OrangeGatherersScrip,
        TrackedDataType.SackOfNuts,
        TrackedDataType.Ventures
        // Individual crystals are now handled by CrystalTracker tool
    };

    /// <summary>
    /// Per-data-type display settings (time range, graph bounds, etc.)
    /// </summary>
    public Dictionary<TrackedDataType, DataTrackerSettings> DataTrackerSettings { get; set; } = new();

    // CrystalTracker settings
    public CrystalTrackerSettings CrystalTracker { get; set; } = new();
}

/// <summary>
/// Per-data-type tracker settings.
/// </summary>
public class DataTrackerSettings
{
    public bool HideCharacterSelector { get; set; } = false;
    public bool ShowMultipleLines { get; set; } = false;
    public int TimeRangeValue { get; set; } = 7;
    public TimeRangeUnit TimeRangeUnit { get; set; } = TimeRangeUnit.All;
    public bool ShowEndGap { get; set; } = false;
    public float EndGapPercent { get; set; } = 5f;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    public bool AutoScaleGraph { get; set; } = true;
    public float GraphMinValue { get; set; } = 0f;
    public float GraphMaxValue { get; set; } = 0f; // 0 means use definition default
    public float LegendWidth { get; set; } = 120f;
    public bool ShowLegend { get; set; } = true;
    public GraphType GraphType { get; set; } = GraphType.Area;
}

/// <summary>
/// Crystal element types for filtering.
/// </summary>
public enum CrystalElement
{
    Fire = 0,
    Ice = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Water = 5
}

/// <summary>
/// Crystal tier types for filtering.
/// </summary>
public enum CrystalTier
{
    Shard = 0,
    Crystal = 1,
    Cluster = 2
}

/// <summary>
/// How to group crystal data in the display.
/// </summary>
public enum CrystalGrouping
{
    /// <summary>Show total crystals as a single value.</summary>
    None = 0,
    /// <summary>Show separate lines/values per character.</summary>
    ByCharacter = 1,
    /// <summary>Show separate lines/values per element (Fire, Ice, etc.).</summary>
    ByElement = 2,
    /// <summary>Show separate lines/values per element per character.</summary>
    ByCharacterAndElement = 3
}

/// <summary>
/// Settings for the unified Crystal Tracker tool.
/// </summary>
public class CrystalTrackerSettings
{
    // Grouping
    public CrystalGrouping Grouping { get; set; } = CrystalGrouping.ByElement;

    // Tier filters (which tiers to include)
    public bool IncludeShards { get; set; } = true;
    public bool IncludeCrystals { get; set; } = true;
    public bool IncludeClusters { get; set; } = true;

    // Element filters (which elements to include)
    public bool IncludeFire { get; set; } = true;
    public bool IncludeIce { get; set; } = true;
    public bool IncludeWind { get; set; } = true;
    public bool IncludeEarth { get; set; } = true;
    public bool IncludeLightning { get; set; } = true;
    public bool IncludeWater { get; set; } = true;

    // Source filters
    public bool IncludeRetainers { get; set; } = true;

    // Display settings
    public int TimeRangeValue { get; set; } = 7;
    public TimeRangeUnit TimeRangeUnit { get; set; } = TimeRangeUnit.All;
    public bool AutoScaleGraph { get; set; } = true;
    public bool ShowValueLabel { get; set; } = false;
    public float ValueLabelOffsetX { get; set; } = 0f;
    public float ValueLabelOffsetY { get; set; } = 0f;
    public float LegendWidth { get; set; } = 120f;
    public bool ShowLegend { get; set; } = true;
    public GraphType GraphType { get; set; } = GraphType.Area;

    /// <summary>
    /// Gets whether a specific element is included in the filter.
    /// </summary>
    public bool IsElementIncluded(CrystalElement element) => element switch
    {
        CrystalElement.Fire => IncludeFire,
        CrystalElement.Ice => IncludeIce,
        CrystalElement.Wind => IncludeWind,
        CrystalElement.Earth => IncludeEarth,
        CrystalElement.Lightning => IncludeLightning,
        CrystalElement.Water => IncludeWater,
        _ => true
    };

    /// <summary>
    /// Gets whether a specific tier is included in the filter.
    /// </summary>
    public bool IsTierIncluded(CrystalTier tier) => tier switch
    {
        CrystalTier.Shard => IncludeShards,
        CrystalTier.Crystal => IncludeCrystals,
        CrystalTier.Cluster => IncludeClusters,
        _ => true
    };
}

public class ContentLayoutState
{
    public string Name { get; set; } = string.Empty;
    public LayoutType Type { get; set; } = LayoutType.Windowed;
    public List<ContentComponentState> Components { get; set; } = new();
    public List<ToolLayoutState> Tools { get; set; } = new();

    public bool AutoAdjustResolution { get; set; } = true;
    public int Columns { get; set; } = 16;
    public int Rows { get; set; } = 9;
    public int Subdivisions { get; set; } = 8;
    public int GridResolutionMultiplier { get; set; } = 2;
}

public class ContentComponentState
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
}

/// <summary>
/// Persisted state for a tool within a layout.
/// </summary>
public class ToolLayoutState
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CustomTitle { get; set; } = null;
    public Vector2 Position { get; set; } = new(50, 50);
    public Vector2 Size { get; set; } = new(300, 200);
    public bool Visible { get; set; } = true;

    public bool BackgroundEnabled { get; set; } = false;
    public bool HeaderVisible { get; set; } = true;
    public bool ScrollbarVisible { get; set; } = false;
    public Vector4 BackgroundColor { get; set; } = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);

    public float GridCol { get; set; } = 0f;
    public float GridRow { get; set; } = 0f;
    public float GridColSpan { get; set; } = 4f;
    public float GridRowSpan { get; set; } = 4f;
    public bool HasGridCoords { get; set; } = false;
    
    /// <summary>
    /// List of series names that are hidden in this tool instance.
    /// </summary>
    public List<string> HiddenSeries { get; set; } = new();
}
