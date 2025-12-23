using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.MainWindow.Tools.CrystalTracker;
using Kaleidoscope.Gui.MainWindow.Tools.DataTracker;
using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using Kaleidoscope.Gui.MainWindow.Tools.GilTicker;
using Kaleidoscope.Gui.MainWindow.Tools.Help;
using Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;
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
        public const string LivePriceFeed = "LivePriceFeed";
        public const string InventoryValue = "InventoryValue";
        public const string TopItems = "TopItems";
        public const string ItemSalesHistory = "ItemSalesHistory";
        
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
        IDataManager? dataManager = null)
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
        IDataManager? dataManager = null)
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

                case ToolIds.GilTicker:
                    // Create a helper that shares the database with the sampler
                    var tickerHelper = new GilTrackerHelper(samplerService.DbService);
                    var tickerInner = new GilTickerComponent(tickerHelper, configService);
                    return new GilTickerTool(tickerInner, tickerHelper, configService) { Position = pos };

                case ToolIds.GettingStarted:
                    return new GettingStartedTool { Position = pos };

                case ToolIds.ImPlotReference:
                    return new ImPlotReferenceTool { Position = pos };

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
