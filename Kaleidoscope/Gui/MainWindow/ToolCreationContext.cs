using System.Numerics;
using Dalamud.Plugin.Services;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Bundles common service dependencies used when creating tool instances.
/// Reduces parameter count in factory methods and improves maintainability.
/// </summary>
public sealed record ToolCreationContext(
    FilenameService FilenameService,
    CurrencyTrackerService CurrencyTrackerService,
    ConfigurationService ConfigService,
    CharacterDataService? CharacterDataService = null,
    InventoryChangeService? InventoryChangeService = null,
    TrackedDataRegistry? Registry = null,
    UniversalisWebSocketService? WebSocketService = null,
    PriceTrackingService? PriceTrackingService = null,
    ItemDataService? ItemDataService = null,
    IDataManager? DataManager = null,
    InventoryCacheService? InventoryCacheService = null,
    AutoRetainerService? AutoRetainerIpc = null,
    ITextureProvider? TextureProvider = null,
    FavoritesService? FavoritesService = null,
    SalePriceCacheService? SalePriceCacheService = null);
