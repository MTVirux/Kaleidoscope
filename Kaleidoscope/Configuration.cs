using Dalamud.Configuration;
using System.Numerics;
using System.Collections.Generic;

namespace Kaleidoscope
{
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
        public List<ContentLayoutState> Layouts { get; set; } = new List<ContentLayoutState>() { new ContentLayoutState() { Name = "Default" } };
        // Name of the currently active layout. If missing, the first layout will be used.
        public string ActiveLayoutName { get; set; } = "Default";
    }

    public class ContentLayoutState
    {
        public string Name { get; set; } = string.Empty;
        public List<ContentComponentState> Components { get; set; } = new List<ContentComponentState>();
        // New: explicit tool layout entries for the HUD tools (absolute positioning)
        public List<ToolLayoutState> Tools { get; set; } = new List<ToolLayoutState>();
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
    }
}
