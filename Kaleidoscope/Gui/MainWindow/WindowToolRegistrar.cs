using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.MainWindow.Tools.CrystalTable;
using Kaleidoscope.Gui.MainWindow.Tools.CrystalTracker;
using Kaleidoscope.Gui.MainWindow.Tools.DataTracker;
using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using Kaleidoscope.Gui.MainWindow.Tools.GilTicker;
using Kaleidoscope.Gui.MainWindow.Tools.Help;
using Kaleidoscope.Gui.MainWindow.Tools.ItemGraph;
using Kaleidoscope.Gui.MainWindow.Tools.ItemTable;
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
        public const string GilTicker = "GilTicker";
        public const string GettingStarted = "GettingStarted";
        public const string ImPlotReference = "ImPlotReference";
        public const string CrystalTracker = "CrystalTracker";
        public const string CrystalTable = "CrystalTable";
        public const string LivePriceFeed = "LivePriceFeed";
        public const string InventoryValue = "InventoryValue";
        public const string TopItems = "TopItems";
        public const string ItemSalesHistory = "ItemSalesHistory";
        public const string ItemTable = "ItemTable";
        public const string ItemGraph = "ItemGraph";
        public const string Label = "Label";
        public const string UniversalisWebSocketStatus = "UniversalisWebSocketStatus";
        public const string AutoRetainerStatus = "AutoRetainerStatus";
        public const string UniversalisApiStatus = "UniversalisApiStatus";
        public const string DatabaseSize = "DatabaseSize";
        public const string CacheSize = "CacheSize";
        
        // Data tracker tool IDs are dynamically generated as "DataTracker_{TrackedDataType}"
        public static string DataTracker(TrackedDataType type) => $"DataTracker_{type}";
    }

    /// <summary>
    /// Crystal types that are handled by the unified CrystalTracker tool
    /// and should not be registered as individual DataTracker tools.
    /// </summary>
    private static readonly HashSet<TrackedDataType> ExcludedCrystalTypes = new()
    {
        TrackedDataType.CrystalsTotal,
        TrackedDataType.FireCrystals,
        TrackedDataType.IceCrystals,
        TrackedDataType.WindCrystals,
        TrackedDataType.EarthCrystals,
        TrackedDataType.LightningCrystals,
        TrackedDataType.WaterCrystals
    };

    public static void RegisterTools(
        WindowContentContainer container, 
        FilenameService filenameService, 
        SamplerService samplerService, 
        ConfigurationService configService,
        InventoryChangeService? inventoryChangeService = null,
        TrackedDataRegistry? registry = null,
        UniversalisWebSocketService? webSocketService = null,
        PriceTrackingService? priceTrackingService = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        InventoryCacheService? inventoryCacheService = null,
        AutoRetainerIpcService? autoRetainerIpc = null)
    {
        if (container == null) return;

        try
        {
            // Register all data tracker (graph) tools from the registry
            // (excluding crystal types which are handled by CrystalTracker)
            if (registry != null)
            {
                foreach (var (dataType, definition) in registry.Definitions)
                {
                    // Skip crystal types - they're handled by CrystalTracker
                    if (ExcludedCrystalTypes.Contains(dataType))
                        continue;

                    var toolId = ToolIds.DataTracker(dataType);
                    var category = GetCategoryPath(definition.Category);
                    
                    container.RegisterTool(
                        toolId,
                        definition.DisplayName,
                        pos => CreateDataTrackerTool(dataType, pos, samplerService, configService, registry, inventoryChangeService),
                        definition.Description,
                        category);
                }
            }

            // Register the unified Crystal Tracker tool
            container.RegisterTool(
                ToolIds.CrystalTracker,
                "Crystal Tracker",
                pos => CreateCrystalTrackerTool(pos, samplerService, configService, inventoryChangeService),
                "Tracks shards, crystals, and clusters with grouping by character/element and filtering options",
                "Graph");

            // Register the Crystal Table tool
            container.RegisterTool(
                ToolIds.CrystalTable,
                "Crystal Table",
                pos => CreateCrystalTableTool(pos, samplerService, configService, inventoryChangeService),
                "Table view of crystal counts by element and tier for all characters",
                "Table");

            // Register the Item Table tool
            container.RegisterTool(
                ToolIds.ItemTable,
                "Item Table",
                pos => CreateItemTableTool(pos, samplerService, configService, inventoryCacheService, registry, itemDataService, dataManager),
                "Customizable table for tracking items and currencies across characters",
                "Table");

            // Register the Item Graph tool
            container.RegisterTool(
                ToolIds.ItemGraph,
                "Item Graph",
                pos => CreateItemGraphTool(pos, samplerService, configService, inventoryCacheService, registry, itemDataService, dataManager),
                "Customizable time-series graph for tracking items and currencies",
                "Graph");

            container.RegisterTool(
                ToolIds.GilTicker,
                "Gil Ticker",
                pos => CreateToolInstance(ToolIds.GilTicker, pos, filenameService, samplerService, configService, registry, inventoryChangeService, webSocketService, priceTrackingService),
                "Scrolling ticker of character gil values",
                "Ticker");

            container.RegisterTool(
                ToolIds.GettingStarted,
                "Getting Started",
                pos => CreateToolInstance(ToolIds.GettingStarted, pos, filenameService, samplerService, configService, registry, inventoryChangeService, webSocketService, priceTrackingService),
                "Instructions for new users",
                "Help");

            container.RegisterTool(
                ToolIds.ImPlotReference,
                "Graph Controls",
                pos => CreateToolInstance(ToolIds.ImPlotReference, pos, filenameService, samplerService, configService, registry, inventoryChangeService, webSocketService, priceTrackingService),
                "Instructions for navigating and interacting with graphs",
                "Help");

            container.RegisterTool(
                ToolIds.Label,
                "Label",
                pos => CreateToolInstance(ToolIds.Label, pos, filenameService, samplerService, configService, registry, inventoryChangeService, webSocketService, priceTrackingService),
                "A simple text label for adding notes or annotations to your layout",
                "Utility");

            // Register price tracking tools
            if (webSocketService != null && priceTrackingService != null && itemDataService != null)
            {
                container.RegisterTool(
                    ToolIds.LivePriceFeed,
                    "Live Price Feed",
                    pos => CreateLivePriceFeedTool(pos, webSocketService, priceTrackingService, configService, itemDataService),
                    "Real-time feed of Universalis market updates from the WebSocket",
                    "Price Tracking");

                container.RegisterTool(
                    ToolIds.InventoryValue,
                    "Inventory Value",
                    pos => CreateInventoryValueTool(pos, priceTrackingService, samplerService, configService),
                    "Tracks the liquid value of character inventories over time",
                    "Price Tracking");

                container.RegisterTool(
                    ToolIds.TopItems,
                    "Top Items",
                    pos => CreateTopItemsTool(pos, priceTrackingService, samplerService, configService, itemDataService, dataManager, inventoryChangeService),
                    "Shows the most valuable items in character inventories",
                    "Price Tracking");

                container.RegisterTool(
                    ToolIds.ItemSalesHistory,
                    "Item Sales History",
                    pos => CreateItemSalesHistoryTool(pos, priceTrackingService, configService, itemDataService, dataManager),
                    "View sale history for any marketable item from Universalis",
                    "Price Tracking");
            }

            // Register status/utility tools
            container.RegisterTool(
                ToolIds.UniversalisWebSocketStatus,
                "WebSocket Status",
                pos => CreateUniversalisWebSocketStatusTool(pos, configService, webSocketService),
                "Shows the Universalis WebSocket connection status",
                "Utility");

            container.RegisterTool(
                ToolIds.AutoRetainerStatus,
                "AutoRetainer Status",
                pos => CreateAutoRetainerStatusTool(pos, autoRetainerIpc),
                "Shows the AutoRetainer IPC connection status",
                "Utility");

            container.RegisterTool(
                ToolIds.UniversalisApiStatus,
                "Universalis API Status",
                pos => CreateUniversalisApiStatusTool(pos, configService, priceTrackingService),
                "Shows the Universalis REST API status and configuration",
                "Utility");

            container.RegisterTool(
                ToolIds.DatabaseSize,
                "Database Size",
                pos => CreateDatabaseSizeTool(pos, samplerService),
                "Shows the current size of the SQLite database file",
                "Utility");

            container.RegisterTool(
                ToolIds.CacheSize,
                "Cache Size",
                pos => CreateCacheSizeTool(pos, inventoryCacheService),
                "Shows the current size of the inventory memory cache",
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

    private static ToolComponent? CreateCrystalTrackerTool(
        Vector2 pos,
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryChangeService? inventoryChangeService)
    {
        try
        {
            var component = new CrystalTrackerComponent(samplerService, configService, inventoryChangeService);
            return new CrystalTrackerTool(component, configService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create CrystalTrackerTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateCrystalTableTool(
        Vector2 pos,
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryChangeService? inventoryChangeService)
    {
        try
        {
            return new CrystalTableTool(samplerService, configService, inventoryChangeService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create CrystalTableTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateItemTableTool(
        Vector2 pos,
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService,
        TrackedDataRegistry? registry,
        ItemDataService? itemDataService,
        IDataManager? dataManager)
    {
        try
        {
            return new ItemTableTool(
                samplerService, 
                configService, 
                inventoryCacheService, 
                registry, 
                itemDataService, 
                dataManager) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create ItemTableTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateItemGraphTool(
        Vector2 pos,
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService,
        TrackedDataRegistry? registry,
        ItemDataService? itemDataService,
        IDataManager? dataManager)
    {
        try
        {
            return new ItemGraphTool(
                samplerService, 
                configService, 
                inventoryCacheService, 
                registry, 
                itemDataService, 
                dataManager) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create ItemGraphTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateDataTrackerTool(
        TrackedDataType dataType,
        Vector2 pos,
        SamplerService samplerService,
        ConfigurationService configService,
        TrackedDataRegistry registry,
        InventoryChangeService? inventoryChangeService)
    {
        try
        {
            var component = new DataTrackerComponent(dataType, samplerService, configService, registry, inventoryChangeService);
            return new DataTrackerTool(component, configService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to create DataTrackerTool for {dataType}", ex);
            return null;
        }
    }

    private static ToolComponent? CreateLivePriceFeedTool(
        Vector2 pos,
        UniversalisWebSocketService webSocketService,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService,
        ItemDataService itemDataService)
    {
        try
        {
            return new LivePriceFeedTool(webSocketService, priceTrackingService, configService, itemDataService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create LivePriceFeedTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateInventoryValueTool(
        Vector2 pos,
        PriceTrackingService priceTrackingService,
        SamplerService samplerService,
        ConfigurationService configService)
    {
        try
        {
            return new InventoryValueTool(priceTrackingService, samplerService, configService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create InventoryValueTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateTopItemsTool(
        Vector2 pos,
        PriceTrackingService priceTrackingService,
        SamplerService samplerService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        IDataManager? dataManager,
        InventoryChangeService? inventoryChangeService)
    {
        try
        {
            if (dataManager == null)
            {
                LogService.Debug("CreateTopItemsTool: IDataManager is null, tool will have limited functionality");
                return null;
            }
            return new TopItemsTool(priceTrackingService, samplerService, configService, itemDataService, dataManager, inventoryChangeService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create TopItemsTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateItemSalesHistoryTool(
        Vector2 pos,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        IDataManager? dataManager)
    {
        try
        {
            if (dataManager == null)
            {
                LogService.Debug("CreateItemSalesHistoryTool: IDataManager is null");
                return null;
            }
            return new ItemSalesHistoryTool(
                priceTrackingService.UniversalisService, 
                priceTrackingService, 
                configService, 
                itemDataService, 
                dataManager) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create ItemSalesHistoryTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateUniversalisWebSocketStatusTool(
        Vector2 pos,
        ConfigurationService configService,
        UniversalisWebSocketService? webSocketService)
    {
        try
        {
            return new UniversalisWebSocketStatusTool(configService, webSocketService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create UniversalisWebSocketStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateAutoRetainerStatusTool(
        Vector2 pos,
        AutoRetainerIpcService? autoRetainerIpc)
    {
        try
        {
            return new AutoRetainerStatusTool(autoRetainerIpc) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create AutoRetainerStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateUniversalisApiStatusTool(
        Vector2 pos,
        ConfigurationService configService,
        PriceTrackingService? priceTrackingService)
    {
        try
        {
            return new UniversalisApiStatusTool(configService, priceTrackingService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create UniversalisApiStatusTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateDatabaseSizeTool(
        Vector2 pos,
        SamplerService samplerService)
    {
        try
        {
            return new DatabaseSizeTool(samplerService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create DatabaseSizeTool", ex);
            return null;
        }
    }

    private static ToolComponent? CreateCacheSizeTool(
        Vector2 pos,
        InventoryCacheService? inventoryCacheService)
    {
        try
        {
            if (inventoryCacheService == null)
            {
                LogService.Debug("CreateCacheSizeTool: InventoryCacheService is null");
                return null;
            }
            return new CacheSizeTool(inventoryCacheService) { Position = pos };
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create CacheSizeTool", ex);
            return null;
        }
    }

    public static ToolComponent? CreateToolInstance(
        string id, 
        Vector2 pos, 
        FilenameService filenameService, 
        SamplerService samplerService, 
        ConfigurationService configService,
        TrackedDataRegistry? registry = null,
        InventoryChangeService? inventoryChangeService = null,
        UniversalisWebSocketService? webSocketService = null,
        PriceTrackingService? priceTrackingService = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        InventoryCacheService? inventoryCacheService = null,
        AutoRetainerIpcService? autoRetainerIpc = null)
    {
        try
        {
            // Check if this is a data tracker tool ID
            if (id.StartsWith("DataTracker_") && registry != null)
            {
                var typeName = id.Substring("DataTracker_".Length);
                if (Enum.TryParse<TrackedDataType>(typeName, out var dataType))
                {
                    return CreateDataTrackerTool(dataType, pos, samplerService, configService, registry, inventoryChangeService);
                }
            }

            switch (id)
            {
                case ToolIds.CrystalTracker:
                    return CreateCrystalTrackerTool(pos, samplerService, configService, inventoryChangeService);

                case ToolIds.CrystalTable:
                    return CreateCrystalTableTool(pos, samplerService, configService, inventoryChangeService);

                case ToolIds.ItemTable:
                    return CreateItemTableTool(pos, samplerService, configService, inventoryCacheService, registry, itemDataService, dataManager);

                case ToolIds.ItemGraph:
                    return CreateItemGraphTool(pos, samplerService, configService, inventoryCacheService, registry, itemDataService, dataManager);

                case ToolIds.GilTicker:
                    // Create a helper that shares the database with the sampler
                    var tickerHelper = new GilTrackerHelper(samplerService.DbService);
                    var tickerInner = new GilTickerComponent(tickerHelper, configService);
                    return new GilTickerTool(tickerInner, tickerHelper, configService) { Position = pos };

                case ToolIds.GettingStarted:
                    return new GettingStartedTool { Position = pos };

                case ToolIds.ImPlotReference:
                    return new ImPlotReferenceTool { Position = pos };

                case ToolIds.Label:
                    return new LabelTool(configService) { Position = pos };

                case ToolIds.LivePriceFeed:
                    if (webSocketService != null && priceTrackingService != null && itemDataService != null)
                        return CreateLivePriceFeedTool(pos, webSocketService, priceTrackingService, configService, itemDataService);
                    return null;

                case ToolIds.InventoryValue:
                    if (priceTrackingService != null)
                        return CreateInventoryValueTool(pos, priceTrackingService, samplerService, configService);
                    return null;

                case ToolIds.TopItems:
                    if (priceTrackingService != null && itemDataService != null && dataManager != null)
                        return CreateTopItemsTool(pos, priceTrackingService, samplerService, configService, itemDataService, dataManager, inventoryChangeService);
                    return null;

                case ToolIds.ItemSalesHistory:
                    if (priceTrackingService != null && itemDataService != null && dataManager != null)
                        return CreateItemSalesHistoryTool(pos, priceTrackingService, configService, itemDataService, dataManager);
                    return null;

                case ToolIds.UniversalisWebSocketStatus:
                    return CreateUniversalisWebSocketStatusTool(pos, configService, webSocketService);

                case ToolIds.AutoRetainerStatus:
                    return CreateAutoRetainerStatusTool(pos, autoRetainerIpc);

                case ToolIds.UniversalisApiStatus:
                    return CreateUniversalisApiStatusTool(pos, configService, priceTrackingService);

                case ToolIds.DatabaseSize:
                    return CreateDatabaseSizeTool(pos, samplerService);

                case ToolIds.CacheSize:
                    return CreateCacheSizeTool(pos, inventoryCacheService);

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
