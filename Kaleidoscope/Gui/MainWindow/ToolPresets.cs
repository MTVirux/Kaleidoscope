using System.Numerics;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.MainWindow.Tools.DataTable;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Factory methods for creating pre-configured tool instances.
/// </summary>
public static class ToolPresets
{
    /// <summary>
    /// Tool IDs for preset tools.
    /// </summary>
    public static class ToolIds
    {
        public const string CrystalTable = "CrystalTable";
    }

    /// <summary>
    /// Registers all preset tools with the container.
    /// </summary>
    public static void RegisterPresets(
        WindowContentContainer container,
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService,
        TrackedDataRegistry? registry,
        ItemDataService? itemDataService,
        IDataManager? dataManager,
        ITextureProvider? textureProvider,
        FavoritesService? favoritesService,
        AutoRetainerIpcService? autoRetainerIpc,
        PriceTrackingService? priceTrackingService)
    {
        // Table Presets
        container.DefineToolType(
            ToolIds.CrystalTable,
            "Crystal Table",
            pos => CreateCrystalTable(pos, samplerService, configService, inventoryCacheService, registry, itemDataService, dataManager, textureProvider, favoritesService, autoRetainerIpc, priceTrackingService),
            "Pre-configured table showing all shards, crystals, and clusters",
            "Table > Presets");
    }

    /// <summary>
    /// Creates a DataTable pre-configured with all crystal items.
    /// </summary>
    public static ToolComponent? CreateCrystalTable(
        Vector2 pos,
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService,
        TrackedDataRegistry? registry,
        ItemDataService? itemDataService,
        IDataManager? dataManager,
        ITextureProvider? textureProvider,
        FavoritesService? favoritesService,
        AutoRetainerIpcService? autoRetainerIpc,
        PriceTrackingService? priceTrackingService)
    {
        try
        {
            var tool = new DataTableTool(
                samplerService,
                configService,
                inventoryCacheService,
                registry,
                itemDataService,
                dataManager,
                textureProvider,
                favoritesService,
                autoRetainerIpc,
                priceTrackingService) { Position = pos };

            // Pre-configure with all crystal items (shards, crystals, clusters)
            // Crystal item IDs: Shards (2-7), Crystals (8-13), Clusters (14-19)
            // Elements: Fire=0, Ice=1, Wind=2, Earth=3, Lightning=4, Water=5
            var columns = new List<ItemColumnConfig>();

            // Add all 18 crystal types
            for (uint itemId = 2; itemId <= 19; itemId++)
            {
                columns.Add(new ItemColumnConfig
                {
                    Id = itemId,
                    IsCurrency = false,
                    Width = 60f,
                    StoreHistory = false
                });
            }

            tool.SetColumns(columns);
            tool.ConfigureSettings(s =>
            {
                s.TextColorMode = Widgets.TableTextColorMode.PreferredItemColors;
                s.AutoSizeEqualColumns = true;
            });
            tool.SetPresetName("Crystal Table");

            return tool;
        }
        catch (Exception ex)
        {
            LogService.Error("Failed to create Crystal Table preset", ex);
            return null;
        }
    }
}
