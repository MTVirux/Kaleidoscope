using Dalamud.Configuration;
using System.Numerics;
using System.Collections.Generic;

namespace Kaleidoscope
{
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

        // Keep a single simple setting to control whether the UI opens on start.
        public bool ShowOnStart { get; set; } = true;
        // When true, plugin will open in fullscreen by default and will close
        // instead of returning to the main window when exiting fullscreen.
        public bool ExclusiveFullscreen { get; set; } = false;
        
        // Window pin states used by the UI lock button component.
        public bool PinMainWindow { get; set; } = false;
        public bool PinConfigWindow { get; set; } = false;
        
        // Saved position/size for windows when pinned
        public Vector2 MainWindowPos { get; set; } = new Vector2(100, 100);
        public Vector2 MainWindowSize { get; set; } = new Vector2(600, 400);
        public Vector2 ConfigWindowPos { get; set; } = new Vector2(100, 100);
        public Vector2 ConfigWindowSize { get; set; } = new Vector2(600, 400);

        // Content container grid cell sizes as percentages (1-100).
        // These control the width and height of each grid cell inside the content container.
        public float ContentGridCellWidthPercent { get; set; } = 25f;
        public float ContentGridCellHeightPercent { get; set; } = 25f;
        // Number of subdivisions inside a grid cell for finer snapping and grid display.
        // For example, a value of 4 splits each cell into 4x4 minor cells.
        public int GridSubdivisions { get; set; } = 8;
        // When true, content containers should show their grid/edit overlays by default.
        public bool EditMode { get; set; } = false;

        // Layout persistence: allow multiple named layouts each containing a set of components.
        // Start empty: do not create a "Default" layout automatically on first run.
        public List<ContentLayoutState> Layouts { get; set; } = new List<ContentLayoutState>();
        // Name of the currently active windowed layout. If empty, the first windowed layout will be used when present.
        public string ActiveWindowedLayoutName { get; set; } = string.Empty;
        // Name of the currently active fullscreen layout. If empty, the first fullscreen layout will be used when present.
        public string ActiveFullscreenLayoutName { get; set; } = string.Empty;
        
        // Legacy: kept for migration, will be removed in future versions
        [Obsolete("Use ActiveWindowedLayoutName or ActiveFullscreenLayoutName instead")]
        public string ActiveLayoutName { get; set; } = string.Empty;
    }

    public class ContentLayoutState
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>Whether this layout is for windowed or fullscreen mode.</summary>
        public LayoutType Type { get; set; } = LayoutType.Windowed;
        public List<ContentComponentState> Components { get; set; } = new List<ContentComponentState>();
        // New: explicit tool layout entries for the HUD tools (absolute positioning)
        public List<ToolLayoutState> Tools { get; set; } = new List<ToolLayoutState>();
        
        // Layout-specific grid resolution settings
        /// <summary>
        /// When true, grid resolution is calculated from aspect ratio * GridResolutionMultiplier.
        /// When false, Columns and Rows are used directly.
        /// </summary>
        public bool AutoAdjustResolution { get; set; } = true;
        /// <summary>Number of columns in the grid (used when AutoAdjustResolution is false).</summary>
        public int Columns { get; set; } = 16;
        /// <summary>Number of rows in the grid (used when AutoAdjustResolution is false).</summary>
        public int Rows { get; set; } = 9;
        /// <summary>Number of subdivisions inside each grid cell for finer snapping/display.</summary>
        public int Subdivisions { get; set; } = 8;
        /// <summary>
        /// Grid resolution multiplier (1-10). When AutoAdjustResolution is true:
        /// Columns = AspectRatioWidth * GridResolutionMultiplier
        /// Rows = AspectRatioHeight * GridResolutionMultiplier
        /// </summary>
        public int GridResolutionMultiplier { get; set; } = 2;
    }

    public class ContentComponentState
    {
        public int Col { get; set; }
        public int Row { get; set; }
        public int ColSpan { get; set; }
        public int RowSpan { get; set; }
    }

    // Represents a single tool's persisted state inside a layout
    public class ToolLayoutState
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public Vector2 Position { get; set; } = new Vector2(50, 50);
        public Vector2 Size { get; set; } = new Vector2(300, 200);
        public bool Visible { get; set; } = true;
        // Optional background persistence for tools
        public bool BackgroundEnabled { get; set; } = false;
        // Default to Dalamud red (approx. #D23A3A)
        public Vector4 BackgroundColor { get; set; } = new Vector4(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
        
        // Grid-based coordinates for proportional resizing
        // These are the column/row positions (can be fractional for sub-grid positions)
        /// <summary>Column position in grid coordinates (0-based, fractional).</summary>
        public float GridCol { get; set; } = 0f;
        /// <summary>Row position in grid coordinates (0-based, fractional).</summary>
        public float GridRow { get; set; } = 0f;
        /// <summary>Width in grid columns (fractional).</summary>
        public float GridColSpan { get; set; } = 4f;
        /// <summary>Height in grid rows (fractional).</summary>
        public float GridRowSpan { get; set; } = 4f;
        /// <summary>Whether grid coordinates have been set (for migration from pixel-based layouts).</summary>
        public bool HasGridCoords { get; set; } = false;
    }
}
