using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using Kaleidoscope.Gui.MainWindow.Tools.Help;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Registers available tools with the content container.
/// </summary>
public static class WindowToolRegistrar
{
    public static class ToolIds
    {
        public const string GilTracker = "GilTracker";
        public const string GettingStarted = "GettingStarted";
    }

    public static void RegisterTools(WindowContentContainer container, FilenameService filenameService, SamplerService samplerService, ConfigurationService configService)
    {
        if (container == null) return;

        try
        {
            container.RegisterTool(
                ToolIds.GilTracker,
                "Gil Tracker",
                pos => CreateToolInstance(ToolIds.GilTracker, pos, filenameService, samplerService, configService),
                "Track gil and history",
                "Gil>Graph");

            container.RegisterTool(
                ToolIds.GettingStarted,
                "Getting Started",
                pos => CreateToolInstance(ToolIds.GettingStarted, pos, filenameService, samplerService, configService),
                "Instructions for new users",
                "Help");
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to register tools", ex);
        }
    }

    public static ToolComponent? CreateToolInstance(string id, Vector2 pos, FilenameService filenameService, SamplerService samplerService, ConfigurationService configService)
    {
        try
        {
            switch (id)
            {
                case ToolIds.GilTracker:
                    var inner = new GilTrackerComponent(filenameService, samplerService, configService);
                    return new GilTrackerTool(inner, configService) { Position = pos };

                case ToolIds.GettingStarted:
                    return new GettingStartedTool { Position = pos };

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to create tool instance '{id}'", ex);
            return null;
        }
    }
}
