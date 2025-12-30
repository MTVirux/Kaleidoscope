using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;
using Kaleidoscope.Gui.MainWindow.Tools.Data;
using Kaleidoscope.Gui.MainWindow.Tools.Help;
using Kaleidoscope.Gui.MainWindow.Tools.Label;
using Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;
using Kaleidoscope.Gui.MainWindow.Tools.Status;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Registers available tools with the content container.
/// </summary>
public static class WindowToolRegistrar
{
    public static class ToolIds
    {
        public const string GettingStarted = "GettingStarted";
        public const string ImPlotReference = "ImPlotReference";
        public const string LivePriceFeed = "LivePriceFeed";
        public const string InventoryValue = "InventoryValue";
        public const string TopItems = "TopItems";
        public const string ItemSalesHistory = "ItemSalesHistory";
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
    }

    /// <summary>
    /// Registers tools using a bundled context of dependencies.
    /// </summary>
    public static void RegisterTools(WindowContentContainer container, ToolCreationContext ctx)
    {
        RegisterTools(
            container,
            ctx.FilenameService,
            ctx.CurrencyTrackerService,
            ctx.ConfigService,
            ctx.CharacterDataService,
            ctx.InventoryChangeService,
            ctx.Registry,
            ctx.WebSocketService,
            ctx.PriceTrackingService,
            ctx.ItemDataService,
            ctx.DataManager,
            ctx.InventoryCacheService,
            ctx.AutoRetainerIpc,
            ctx.TextureProvider,
            ctx.FavoritesService);
    }

    public static void RegisterTools(
        WindowContentContainer container, 
        FilenameService filenameService, 
        CurrencyTrackerService CurrencyTrackerService, 
        ConfigurationService configService,
        CharacterDataService? characterDataService = null,
        InventoryChangeService? inventoryChangeService = null,
        TrackedDataRegistry? registry = null,
        UniversalisWebSocketService? webSocketService = null,
        PriceTrackingService? priceTrackingService = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        InventoryCacheService? inventoryCacheService = null,
        AutoRetainerIpcService? autoRetainerIpc = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null)
    {
        if (container == null) return;

        // Bundle context for cleaner factory method calls
        var ctx = new ToolCreationContext(
            filenameService, CurrencyTrackerService, configService, characterDataService,
            inventoryChangeService, registry, webSocketService,
            priceTrackingService, itemDataService, dataManager,
            inventoryCacheService, autoRetainerIpc, textureProvider, favoritesService);

        try
        {
            // Register Data tool variants for items/currency tracking
            container.DefineToolType(
                ToolIds.DataGraph,
                "Data Graph",
                pos => CreateDataToolGraph(pos, ctx),
                "Track items and currencies over time with graphing visualization",
                "Items/Currency");

            container.DefineToolType(
                ToolIds.DataTable,
                "Data Table",
                pos => CreateDataToolTable(pos, ctx),
                "Track items and currencies in a table view with characters as rows",
                "Items/Currency");

            // Register tool presets from separate file
            ToolPresets.RegisterPresets(container, CurrencyTrackerService, configService, inventoryCacheService, registry, itemDataService, dataManager, textureProvider, favoritesService, autoRetainerIpc, priceTrackingService);

            container.DefineToolType(
                ToolIds.GettingStarted,
                "Getting Started",
                pos => CreateToolFromId(ToolIds.GettingStarted, pos, ctx),
                "Instructions for new users",
                "Help");

            container.DefineToolType(
                ToolIds.ImPlotReference,
                "Graph Controls",
                pos => CreateToolFromId(ToolIds.ImPlotReference, pos, ctx),
                "Instructions for navigating and interacting with graphs",
                "Help");

            container.DefineToolType(
                ToolIds.Label,
                "Label",
                pos => CreateToolFromId(ToolIds.Label, pos, ctx),
                "A simple text label for adding notes or annotations to your layout",
                "Utility");

            // Register price tracking tools
            if (webSocketService != null && priceTrackingService != null && itemDataService != null)
            {
                container.DefineToolType(
                    ToolIds.LivePriceFeed,
                    "Live Price Feed",
                    pos => CreateLivePriceFeedTool(pos, ctx),
                    "Real-time feed of Universalis market updates from the WebSocket. Click entries to view full market data via API.",
                    "Universalis");

                container.DefineToolType(
                    ToolIds.InventoryValue,
                    "Inventory Value",
                    pos => CreateInventoryValueTool(pos, ctx),
                    "Tracks the liquid value of character inventories over time",
                    "Universalis");

                container.DefineToolType(
                    ToolIds.TopItems,
                    "Top Items",
                    pos => CreateTopInventoryValueTool(pos, ctx),
                    "Shows the most valuable items in character inventories",
                    "Universalis");

                container.DefineToolType(
                    ToolIds.ItemSalesHistory,
                    "Item Sales History",
                    pos => CreateItemSalesHistoryTool(pos, ctx),
                    "View sale history for any marketable item from Universalis",
                    "Universalis");
            }

            // Register status/utility tools
            container.DefineToolType(
                ToolIds.UniversalisWebSocketStatus,
                "WebSocket Status",
                pos => CreateUniversalisWebSocketStatusTool(pos, ctx),
                "Shows the Universalis WebSocket connection status",
                "Universalis");

            container.DefineToolType(
                ToolIds.AutoRetainerStatus,
                "AutoRetainer IPC Status",
                pos => CreateAutoRetainerStatusTool(pos, ctx),
                "Shows the AutoRetainer IPC connection status",
                "AutoRetainer");

            container.DefineToolType(
                ToolIds.AutoRetainerControl,
                "AutoRetainer Control",
                pos => CreateAutoRetainerControlTool(pos, ctx),
                "Control AutoRetainer functions via IPC: Multi-Mode, suppress, relog, and view character data",
                "AutoRetainer");

            container.DefineToolType(
                ToolIds.UniversalisApiStatus,
                "Universalis API Status",
                pos => CreateUniversalisApiStatusTool(pos, ctx),
                "Shows the Universalis REST API status and configuration",
                "Universalis");

            container.DefineToolType(
                ToolIds.DatabaseSize,
                "Database Size",
                pos => CreateDatabaseSizeTool(pos, ctx),
                "Shows the current size of the SQLite database file",
                "Utility");

            container.DefineToolType(
                ToolIds.CacheSize,
                "Cache Size",
                pos => CreateCacheSizeTool(pos, ctx),
                "Shows the current size of the inventory memory cache",
                "Utility");

            container.DefineToolType(
                ToolIds.RetainerVentureStatus,
                "Ventures Status",
                pos => CreateRetainerVentureStatusTool(pos, ctx),
                "Displays retainer venture timers with millisecond precision",
                "AutoRetainer");

            container.DefineToolType(
                ToolIds.SubmersibleVentureStatus,
                "Submersibles Status",
                pos => CreateSubmersibleVentureStatusTool(pos, ctx),
                "Displays submersible voyage timers with millisecond precision",
                "AutoRetainer");

            container.DefineToolType(
                ToolIds.Fps,
                "FPS",
                pos => CreateFpsTool(pos),
                "Displays the current frames per second",
                "Utility");
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to register tools", ex);
        }
    }

    private static string GetCategoryPath(TrackedDataCategory category)
    {
        // All data tracker tools are graphs - category path is just "Graph"
        // The TrackedDataCategory is used for grouping within the tool's data selection,
        // not for the top-level tool type categorization
        return "Graph";
    }

    private static ToolComponent? CreateDataToolGraph(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            var tool = new DataTool(
                ctx.CurrencyTrackerService, 
                ctx.ConfigService, 
                ctx.InventoryCacheService, 
                ctx.Registry, 
                ctx.ItemDataService, 
                ctx.DataManager,
                ctx.TextureProvider,
                ctx.FavoritesService,
                ctx.AutoRetainerIpc,
                ctx.PriceTrackingService) { Position = pos };
            tool.ConfigureSettings(s => s.ViewMode = DataToolViewMode.Graph);
            return tool;
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create DataTool (Graph)", ex);
            return null;
        }
    }

    private static ToolComponent? CreateDataToolTable(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            var tool = new DataTool(
                ctx.CurrencyTrackerService, 
                ctx.ConfigService, 
                ctx.InventoryCacheService, 
                ctx.Registry, 
                ctx.ItemDataService, 
                ctx.DataManager,
                ctx.TextureProvider,
                ctx.FavoritesService,
                ctx.AutoRetainerIpc,
                ctx.PriceTrackingService) { Position = pos };
            tool.ConfigureSettings(s => s.ViewMode = DataToolViewMode.Table);
            return tool;
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create DataTool (Table)", ex);
            return null;
        }
    }

    private static ToolComponent? CreateLivePriceFeedTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            if (ctx.WebSocketService == null || ctx.PriceTrackingService == null || ctx.ItemDataService == null)
                return null;
            return new LivePriceFeedTool(
                ctx.WebSocketService, 
                ctx.PriceTrackingService, 
                ctx.ConfigService, 
                ctx.ItemDataService,
                ctx.PriceTrackingService.UniversalisService,
                ctx.CurrencyTrackerService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create LivePriceFeedTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateInventoryValueTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            if (ctx.PriceTrackingService == null || ctx.CharacterDataService == null)
                return null;
            return new InventoryValueTool(ctx.PriceTrackingService, ctx.CurrencyTrackerService, ctx.ConfigService, ctx.CharacterDataService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create InventoryValueTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateTopInventoryValueTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            if (ctx.PriceTrackingService == null || ctx.CharacterDataService == null || ctx.ItemDataService == null || 
                ctx.DataManager == null || ctx.TextureProvider == null || ctx.FavoritesService == null)
            {
                LogService.Debug("CreateTopInventoryValueTool: Required service is null");
                return null;
            }
            return new TopInventoryValueTool(ctx.PriceTrackingService, ctx.CurrencyTrackerService, ctx.ConfigService, ctx.CharacterDataService,
                ctx.ItemDataService, ctx.DataManager, ctx.TextureProvider, ctx.FavoritesService, 
                ctx.InventoryChangeService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create TopInventoryValueTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateItemSalesHistoryTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            if (ctx.PriceTrackingService == null || ctx.ItemDataService == null ||
                ctx.DataManager == null || ctx.TextureProvider == null || ctx.FavoritesService == null)
            {
                LogService.Debug("CreateItemSalesHistoryTool: Required service is null");
                return null;
            }
            return new ItemSalesHistoryTool(
                ctx.PriceTrackingService.UniversalisService, 
                ctx.PriceTrackingService, 
                ctx.ConfigService, 
                ctx.ItemDataService,
                ctx.CurrencyTrackerService,
                ctx.DataManager,
                ctx.TextureProvider,
                ctx.FavoritesService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create ItemSalesHistoryTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateUniversalisWebSocketStatusTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return new UniversalisWebSocketStatusTool(ctx.ConfigService, ctx.WebSocketService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create UniversalisWebSocketStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateAutoRetainerStatusTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return new AutoRetainerStatusTool(ctx.AutoRetainerIpc) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create AutoRetainerStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateAutoRetainerControlTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return new AutoRetainerControlTool(ctx.AutoRetainerIpc) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create AutoRetainerControlTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateRetainerVentureStatusTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return new RetainerVentureStatusTool(ctx.AutoRetainerIpc, ctx.ConfigService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create RetainerVentureStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateSubmersibleVentureStatusTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return new SubmersibleVentureStatusTool(ctx.AutoRetainerIpc, ctx.ConfigService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create SubmersibleVentureStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateUniversalisApiStatusTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return new UniversalisApiStatusTool(ctx.ConfigService, ctx.PriceTrackingService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create UniversalisApiStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateDatabaseSizeTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return new DatabaseSizeTool(ctx.CurrencyTrackerService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create DatabaseSizeTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateCacheSizeTool(Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            if (ctx.InventoryCacheService == null)
            {
                LogService.Debug("CreateCacheSizeTool: InventoryCacheService is null");
                return null;
            }
            return new CacheSizeTool(ctx.InventoryCacheService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create CacheSizeTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateFpsTool(Vector2 pos)
    {
        try
        {
            return new FpsTool { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create FpsTool", ex);
            return null;
        }
    }

    /// <summary>
    /// Creates a tool instance by ID using a bundled context.
    /// </summary>
    public static ToolComponent? CreateToolFromId(string id, Vector2 pos, ToolCreationContext ctx)
    {
        try
        {
            return id switch
            {
                ToolIds.DataGraph => CreateDataToolGraph(pos, ctx),
                ToolIds.DataTable => CreateDataToolTable(pos, ctx),
                ToolIds.GettingStarted => new GettingStartedTool { Position = pos },
                ToolIds.ImPlotReference => new ImPlotReferenceTool { Position = pos },
                ToolIds.Label => new LabelTool(ctx.ConfigService) { Position = pos },
                ToolIds.LivePriceFeed => CreateLivePriceFeedTool(pos, ctx),
                ToolIds.InventoryValue => CreateInventoryValueTool(pos, ctx),
                ToolIds.TopItems => CreateTopInventoryValueTool(pos, ctx),
                ToolIds.ItemSalesHistory => CreateItemSalesHistoryTool(pos, ctx),
                ToolIds.UniversalisWebSocketStatus => CreateUniversalisWebSocketStatusTool(pos, ctx),
                ToolIds.AutoRetainerStatus => CreateAutoRetainerStatusTool(pos, ctx),
                ToolIds.AutoRetainerControl => CreateAutoRetainerControlTool(pos, ctx),
                ToolIds.RetainerVentureStatus => CreateRetainerVentureStatusTool(pos, ctx),
                ToolIds.SubmersibleVentureStatus => CreateSubmersibleVentureStatusTool(pos, ctx),
                ToolIds.UniversalisApiStatus => CreateUniversalisApiStatusTool(pos, ctx),
                ToolIds.DatabaseSize => CreateDatabaseSizeTool(pos, ctx),
                ToolIds.CacheSize => CreateCacheSizeTool(pos, ctx),
                ToolIds.Fps => CreateFpsTool(pos),
                _ => null
            };
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to create tool instance '{id}'", ex);
            return null;
        }
    }
}
