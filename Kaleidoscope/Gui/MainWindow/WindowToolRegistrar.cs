namespace Kaleidoscope.Gui.MainWindow
{
    using System.Numerics;

    public static class WindowToolRegistrar
    {
        // Register available tools into the container. The registrar constructs
        // a fresh tool instance for each Add action so each tool has its own
        // independent state and settings.
        public static void RegisterTools(WindowContentContainer container, string? sharedDbPath = null)
        {
            if (container == null) return;

            try
            {
                container.RegisterTool("GilTracker", "Gil Tracker", pos => CreateToolInstance("GilTracker", pos, sharedDbPath), "Track gil and history");
            }
            catch { }
        }

        // Create a new tool instance by id. Each call returns a fresh instance
        // with default settings/state so tools are independent.
        public static ToolComponent? CreateToolInstance(string id, System.Numerics.Vector2 pos, string? sharedDbPath = null)
        {
            try
            {
                if (id == "CharacterPicker")
                {
                    var cp = new Tools.CharacterPicker.CharacterPickerTool();
                    cp.Position = pos;
                    return cp;
                }

                if (id == "GilTracker")
                {
                    // Each GilTracker tool gets its own GilTrackerComponent instance
                    var inner = new GilTrackerComponent(sharedDbPath);
                    var gt = new Tools.GilTracker.GilTrackerTool(inner);
                    gt.Position = pos;
                    return gt;
                }
            }
            catch { }
            return null;
        }
    }
}
