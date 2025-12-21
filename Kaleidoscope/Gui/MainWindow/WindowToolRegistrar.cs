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
            TrackedDataCategory.Currency => "Gil>Graph",
            TrackedDataCategory.Tomestone => "Tomestones>Graph",
            TrackedDataCategory.Scrip => "Scrips>Graph",
            TrackedDataCategory.GrandCompany => "Grand Company>Graph",
            TrackedDataCategory.PvP => "PvP>Graph",
            TrackedDataCategory.Hunt => "Hunt>Graph",
            TrackedDataCategory.GoldSaucer => "Gold Saucer>Graph",
            TrackedDataCategory.Tribal => "Tribal>Graph",
            TrackedDataCategory.Crafting => "Crafting>Graph",
            TrackedDataCategory.Inventory => "Inventory>Graph",
            TrackedDataCategory.FreeCompanyRetainer => "FC/Retainer>Graph",
            _ => "Other>Graph"
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
