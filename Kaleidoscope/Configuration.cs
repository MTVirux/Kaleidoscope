using Dalamud.Configuration;

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
        // When true, content containers should show their grid/edit overlays by default.
        public bool EditMode { get; set; } = false;
    }
}
