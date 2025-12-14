namespace Kaleidoscope.Gui.MainWindow
{
    using System.Numerics;
    using Kaleidoscope.Gui.MainWindow.Tools.Help;
    using Kaleidoscope.Services;

    public static class WindowToolRegistrar
    {
        // Register available tools into the container. The registrar constructs
        // a fresh tool instance for each Add action so each tool has its own
        // independent state and settings.
        public static void RegisterTools(WindowContentContainer container, FilenameService filenameService, SamplerService samplerService)
        {
            if (container == null) return;

            try
            {
                // Gil tracking tools
                container.RegisterTool("GilTracker", "Gil Tracker", pos => CreateToolInstance("GilTracker", pos, filenameService, samplerService), "Track gil and history", "Gil>Graph");
                
                // Help tools
                container.RegisterTool("GettingStarted", "Getting Started", pos => CreateToolInstance("GettingStarted", pos, filenameService, samplerService), "Instructions for new users", "Help");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to register tools", ex);
            }
        }

        // Create a new tool instance by id. Each call returns a fresh instance
        // with default settings/state so tools are independent.
        public static ToolComponent? CreateToolInstance(string id, System.Numerics.Vector2 pos, FilenameService filenameService, SamplerService samplerService)
        {
            try
            {
                if (id == "GilTracker")
                {
                    // Each GilTracker tool gets its own GilTrackerComponent instance via DI services
                    var inner = new GilTrackerComponent(filenameService, samplerService);
                    var gt = new Tools.GilTracker.GilTrackerTool(inner);
                    gt.Position = pos;
                    return gt;
                }
                
                if (id == "GettingStarted")
                {
                    var tool = new GettingStartedTool();
                    tool.Position = pos;
                    return tool;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Failed to create tool instance '{id}'", ex);
            }
            return null;
        }
    }
}
