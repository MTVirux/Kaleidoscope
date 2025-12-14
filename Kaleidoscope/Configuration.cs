using Dalamud.Configuration;

namespace Kaleidoscope;

/// <summary>
/// Defines whether a layout is intended for windowed or fullscreen mode.
/// </summary>
public enum LayoutType
{
    /// <summary>Layout for the main windowed UI.</summary>
    Windowed = 0,
    /// <summary>Layout for fullscreen mode.</summary>
    Fullscreen = 1
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Whether the UI opens on plugin start.</summary>
    public bool ShowOnStart { get; set; } = true;

    /// <summary>
    /// When true, plugin will open in fullscreen by default and will close
    /// instead of returning to the main window when exiting fullscreen.
    /// </summary>
    public bool ExclusiveFullscreen { get; set; } = false;

    /// <summary>Whether the main window is pinned (locked position/size).</summary>
    public bool PinMainWindow { get; set; } = false;

    /// <summary>Whether the config window is pinned (locked position/size).</summary>
    public bool PinConfigWindow { get; set; } = false;

    /// <summary>Saved position for the main window when pinned.</summary>
    public Vector2 MainWindowPos { get; set; } = new Vector2(100, 100);

    /// <summary>Saved size for the main window when pinned.</summary>
    public Vector2 MainWindowSize { get; set; } = new Vector2(600, 400);

    /// <summary>Saved position for the config window when pinned.</summary>
    public Vector2 ConfigWindowPos { get; set; } = new Vector2(100, 100);

    /// <summary>Saved size for the config window when pinned.</summary>
    public Vector2 ConfigWindowSize { get; set; } = new Vector2(600, 400);

    /// <summary>Grid cell width percentage (1-100) for the content container.</summary>
    public float ContentGridCellWidthPercent { get; set; } = 25f;

    /// <summary>Grid cell height percentage (1-100) for the content container.</summary>
    public float ContentGridCellHeightPercent { get; set; } = 25f;

    /// <summary>Number of subdivisions inside each grid cell for finer snapping.</summary>
    public int GridSubdivisions { get; set; } = 8;

    /// <summary>Whether content containers should show edit overlays by default.</summary>
    public bool EditMode { get; set; } = false;

    /// <summary>All saved layouts (both windowed and fullscreen).</summary>
    public List<ContentLayoutState> Layouts { get; set; } = new List<ContentLayoutState>();

    /// <summary>Name of the currently active windowed layout.</summary>
    public string ActiveWindowedLayoutName { get; set; } = string.Empty;

    /// <summary>Name of the currently active fullscreen layout.</summary>
    public string ActiveFullscreenLayoutName { get; set; } = string.Empty;

    /// <summary>Legacy: kept for migration, will be removed in future versions.</summary>
    [Obsolete("Use ActiveWindowedLayoutName or ActiveFullscreenLayoutName instead")]
    public string ActiveLayoutName { get; set; } = string.Empty;
}

public class ContentLayoutState
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this layout is for windowed or fullscreen mode.</summary>
    public LayoutType Type { get; set; } = LayoutType.Windowed;

    public List<ContentComponentState> Components { get; set; } = new List<ContentComponentState>();

    /// <summary>Tool layout entries for HUD tools (absolute positioning).</summary>
    public List<ToolLayoutState> Tools { get; set; } = new List<ToolLayoutState>();

    /// <summary>When true, grid resolution is calculated from aspect ratio.</summary>
    public bool AutoAdjustResolution { get; set; } = true;

    /// <summary>Number of columns in the grid (used when AutoAdjustResolution is false).</summary>
    public int Columns { get; set; } = 16;

    /// <summary>Number of rows in the grid (used when AutoAdjustResolution is false).</summary>
    public int Rows { get; set; } = 9;

    /// <summary>Number of subdivisions inside each grid cell.</summary>
    public int Subdivisions { get; set; } = 8;

    /// <summary>Grid resolution multiplier (1-10) for auto-adjusted resolution.</summary>
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
/// Represents a single tool's persisted state inside a layout.
/// </summary>
public class ToolLayoutState
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Vector2 Position { get; set; } = new Vector2(50, 50);
    public Vector2 Size { get; set; } = new Vector2(300, 200);
    public bool Visible { get; set; } = true;

    /// <summary>Whether a background rectangle is drawn behind the tool.</summary>
    public bool BackgroundEnabled { get; set; } = false;

    /// <summary>Whether the tool header (title/separator) is visible.</summary>
    public bool HeaderVisible { get; set; } = true;

    /// <summary>Background color (RGBA). Default is Dalamud red with 50% alpha.</summary>
    public Vector4 BackgroundColor { get; set; } = new Vector4(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);

    /// <summary>Column position in grid coordinates (0-based, fractional).</summary>
    public float GridCol { get; set; } = 0f;

    /// <summary>Row position in grid coordinates (0-based, fractional).</summary>
    public float GridRow { get; set; } = 0f;

    /// <summary>Width in grid columns (fractional).</summary>
    public float GridColSpan { get; set; } = 4f;

    /// <summary>Height in grid rows (fractional).</summary>
    public float GridRowSpan { get; set; } = 4f;

    /// <summary>Whether grid coordinates have been set (for migration).</summary>
    public bool HasGridCoords { get; set; } = false;
}
