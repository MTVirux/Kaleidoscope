using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Registers available tools with the content container using <see cref="ToolFactory"/>
/// for dependency-injected tool creation.
/// </summary>
public static class WindowToolRegistrar
{
    /// <summary>
    /// Well-known tool IDs for layout persistence and programmatic creation.
    /// Kept as constants for backward compatibility with saved layouts.
    /// </summary>
    public static class ToolIds
    {
        public const string GettingStarted = "GettingStarted";
        public const string ImPlotReference = "ImPlotReference";
        public const string WebsocketFeed = "WebsocketFeed";
        public const string TopInventoryValueItems = "TopInventoryValueItems";
        public const string ItemSalesHistory = "ItemSalesHistory";
        public const string ItemSalesTracking = "ItemSalesTracking";
        public const string DataGraph = "DataGraph";
        public const string DataTable = "DataTable";
        public const string Label = "Label";
        public const string UniversalisWebSocketStatus = "UniversalisWebSocketStatus";
        public const string AutoRetainerStatus = "AutoRetainerStatus";
        public const string AutoRetainerControl = "AutoRetainerControl";
        public const string UniversalisApiStatus = "UniversalisApiStatus";
        public const string DatabaseSize = "DatabaseSize";
        public const string CacheSize = "CacheSize";
        public const string RetainerVentureStatus = "RetainerVentureStatus";
        public const string SubmersibleVentureStatus = "SubmersibleVentureStatus";
        public const string Fps = "Fps";
        public const string GilFlux = "GilFlux";
    }

    /// <summary>
    /// Registers all available tool types from <see cref="ToolFactory"/> with the container.
    /// Tools whose required services are unavailable are excluded from the menu.
    /// </summary>
    public static void RegisterTools(WindowContentContainer container, ToolFactory toolFactory)
    {
        if (container == null || toolFactory == null) return;

        try
        {
            var categories = toolFactory.GetAvailableDefinitionsByCategory();
            foreach (var (_, definitions) in categories)
            {
                foreach (var def in definitions)
                {
                    container.DefineToolType(
                        def.Id,
                        def.Label,
                        pos => toolFactory.Create(def.Id, pos),
                        def.Description,
                        def.Category);
                }
            }

            LogService.Debug(LogCategory.UI,
                $"RegisterTools: registered {container.ToolRegistry.Count} tool types from ToolFactory");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, "Failed to register tools", ex);
        }
    }

    /// <summary>
    /// Creates a tool instance by ID using the factory.
    /// </summary>
    public static ToolComponent? CreateToolFromId(string id, System.Numerics.Vector2 pos, ToolFactory toolFactory)
        => toolFactory.Create(id, pos);
}
