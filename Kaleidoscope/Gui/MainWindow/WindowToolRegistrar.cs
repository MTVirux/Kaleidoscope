using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using Kaleidoscope.Gui.MainWindow.Tools.Help;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Registers available tools into the content container.
/// </summary>
public static class WindowToolRegistrar
{
    /// <summary>
    /// Registers available tools into the container. Each tool instance is independent.
    /// </summary>
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

    /// <summary>
    /// Creates a new tool instance by ID. Each call returns a fresh instance.
    /// </summary>
    public static ToolComponent? CreateToolInstance(string id, Vector2 pos, FilenameService filenameService, SamplerService samplerService)
    {
        try
        {
            if (id == "GilTracker")
            {
                // Each GilTracker tool gets its own GilTrackerComponent instance via DI services
                var inner = new GilTrackerComponent(filenameService, samplerService);
                var gt = new GilTrackerTool(inner);
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
