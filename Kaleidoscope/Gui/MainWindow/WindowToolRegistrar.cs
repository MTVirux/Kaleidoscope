using Kaleidoscope.Gui.MainWindow.Tools.CrystalTracker;
using Kaleidoscope.Gui.MainWindow.Tools.DataTracker;
using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using Kaleidoscope.Gui.MainWindow.Tools.GilTicker;
using Kaleidoscope.Gui.MainWindow.Tools.Help;
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
        public const string CrystalTracker = "CrystalTracker";
        
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
        TrackedDataRegistry? registry = null)
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
                pos => CreateToolInstance(ToolIds.GilTicker, pos, filenameService, samplerService, configService, registry),
                "Scrolling ticker of character gil values",
                "Ticker");

            container.RegisterTool(
                ToolIds.GettingStarted,
                "Getting Started",
                pos => CreateToolInstance(ToolIds.GettingStarted, pos, filenameService, samplerService, configService, registry),
                "Instructions for new users",
                "Help");
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

    public static ToolComponent? CreateToolInstance(
        string id, 
        Vector2 pos, 
        FilenameService filenameService, 
        SamplerService samplerService, 
        ConfigurationService configService,
        TrackedDataRegistry? registry = null,
        InventoryChangeService? inventoryChangeService = null)
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
