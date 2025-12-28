using System.Numerics;
using Dalamud.Plugin.Services;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Bundles common service dependencies used when creating tool instances.
/// Reduces parameter count in factory methods and improves maintainability.
/// </summary>
public sealed record ToolCreationContext(
    FilenameService FilenameService,
    SamplerService SamplerService,
    ConfigurationService ConfigService,
    CharacterDataService? CharacterDataService = null,
    InventoryChangeService? InventoryChangeService = null,
    TrackedDataRegistry? Registry = null,
    UniversalisWebSocketService? WebSocketService = null,
    PriceTrackingService? PriceTrackingService = null,
    ItemDataService? ItemDataService = null,
    IDataManager? DataManager = null,
    InventoryCacheService? InventoryCacheService = null,
    AutoRetainerIpcService? AutoRetainerIpc = null,
    ITextureProvider? TextureProvider = null,
    FavoritesService? FavoritesService = null);
