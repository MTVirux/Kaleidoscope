using Dalamud.Configuration;

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
    public bool GilTrackerShowLatestValue { get; set; } = false;
    public bool GilTrackerShowEndGap { get; set; } = false;
    public float GilTrackerEndGapPercent { get; set; } = 5f;
    public bool GilTrackerShowValueLabel { get; set; } = false;
    public float GilTrackerValueLabelOffsetX { get; set; } = 0f;
    public float GilTrackerValueLabelOffsetY { get; set; } = 0f;
    public bool GilTrackerAutoScaleGraph { get; set; } = true;
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
    public Vector4 BackgroundColor { get; set; } = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);

    public float GridCol { get; set; } = 0f;
    public float GridRow { get; set; } = 0f;
    public float GridColSpan { get; set; } = 4f;
    public float GridRowSpan { get; set; } = 4f;
    public bool HasGridCoords { get; set; } = false;
}
