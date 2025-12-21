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
        public const string GilTracker = "GilTracker";
        public const string GilTicker = "GilTicker";
        public const string GettingStarted = "GettingStarted";
        
        // Data tracker tool IDs are dynamically generated as "DataTracker_{TrackedDataType}"
        public static string DataTracker(TrackedDataType type) => $"DataTracker_{type}";
    }

    public static void RegisterTools(
        WindowContentContainer container, 
        FilenameService filenameService, 
        SamplerService samplerService, 
        ConfigurationService configService,
        TrackedDataRegistry? registry = null)
    {
        if (container == null) return;

        try
        {
            // Legacy Gil Tracker (kept for backwards compatibility)
            container.RegisterTool(
                ToolIds.GilTracker,
                "Gil Tracker (Legacy)",
                pos => CreateToolInstance(ToolIds.GilTracker, pos, filenameService, samplerService, configService, registry),
                "Track gil and history",
                "Gil>Graph");

            container.RegisterTool(
                ToolIds.GilTicker,
                "Gil Ticker",
                pos => CreateToolInstance(ToolIds.GilTicker, pos, filenameService, samplerService, configService, registry),
                "Scrolling ticker of character gil values",
                "Gil>Ticker");

            // Register all data tracker tools from the registry
            if (registry != null)
            {
                foreach (var (dataType, definition) in registry.Definitions)
                {
                    var toolId = ToolIds.DataTracker(dataType);
                    var category = GetCategoryPath(definition.Category);
                    
                    container.RegisterTool(
                        toolId,
                        definition.DisplayName,
                        pos => CreateDataTrackerTool(dataType, pos, samplerService, configService, registry),
                        definition.Description,
                        category);
                }
            }

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
        return category switch
        {
            TrackedDataCategory.Currency => "Currency>Tracker",
            TrackedDataCategory.Tomestone => "Tomestones>Tracker",
            TrackedDataCategory.Scrip => "Scrips>Tracker",
            TrackedDataCategory.GrandCompany => "Grand Company>Tracker",
            TrackedDataCategory.PvP => "PvP>Tracker",
            TrackedDataCategory.Hunt => "Hunt>Tracker",
            TrackedDataCategory.GoldSaucer => "Gold Saucer>Tracker",
            TrackedDataCategory.Tribal => "Tribal>Tracker",
            TrackedDataCategory.Crafting => "Crafting>Tracker",
            TrackedDataCategory.Inventory => "Inventory>Tracker",
            TrackedDataCategory.FreeCompanyRetainer => "FC/Retainer>Tracker",
            _ => "Other>Tracker"
        };
    }

    private static ToolComponent? CreateDataTrackerTool(
        TrackedDataType dataType,
        Vector2 pos,
        SamplerService samplerService,
        ConfigurationService configService,
        TrackedDataRegistry registry)
    {
        try
        {
            var component = new DataTrackerComponent(dataType, samplerService, configService, registry);
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
        TrackedDataRegistry? registry = null)
    {
        try
        {
            // Check if this is a data tracker tool ID
            if (id.StartsWith("DataTracker_") && registry != null)
            {
                var typeName = id.Substring("DataTracker_".Length);
                if (Enum.TryParse<TrackedDataType>(typeName, out var dataType))
                {
                    return CreateDataTrackerTool(dataType, pos, samplerService, configService, registry);
                }
            }

            switch (id)
            {
                case ToolIds.GilTracker:
                    var gilTrackerInner = new GilTrackerComponent(filenameService, samplerService, configService);
                    return new GilTrackerTool(gilTrackerInner, configService) { Position = pos };

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
