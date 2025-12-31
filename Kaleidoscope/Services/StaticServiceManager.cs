using Dalamud.Plugin;
using Kaleidoscope.Gui.ConfigWindow;
using Kaleidoscope.Gui.MainWindow;
using OtterGui.Log;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Builds the Kaleidoscope service container.
/// </summary>
/// <remarks>
/// Services are organized into categories for clarity:
/// - Meta: Core infrastructure (config, filenames)
/// - State: Application state management
/// - Data: Data tracking and persistence
/// - Market: Universalis integration and price tracking
/// - Ui: Windows and UI services
/// </remarks>
public static class StaticServiceManager
{
    public static ServiceManager CreateProvider(IDalamudPluginInterface pi, Logger log, KaleidoscopePlugin plugin)
    {
        var services = new ServiceManager(log)
            .AddExistingService(log)
            .AddExistingService(plugin)
            .AddMeta()
            .AddState()
            .AddData()
            .AddMarket()
            .AddUi();

        DalamudServices.AddServices(services, pi);
        services.AddIServices(typeof(KaleidoscopePlugin).Assembly);
        services.CreateProvider();
        return services;
    }

    /// <summary>
    /// Core infrastructure services required for plugin operation.
    /// </summary>
    private static ServiceManager AddMeta(this ServiceManager services)
        => services
            .AddSingleton<FilenameService>()
            .AddSingleton<ConfigurationService>()
            .AddSingleton<CommandService>();

    /// <summary>
    /// Application state management services.
    /// </summary>
    private static ServiceManager AddState(this ServiceManager services)
        => services
            .AddSingleton<StateService>()
            .AddSingleton<LayoutEditingService>()
            .AddSingleton<ProfilerService>()
            .AddSingleton<FrameLimiterService>();

    /// <summary>
    /// Data tracking, persistence, and caching services.
    /// </summary>
    private static ServiceManager AddData(this ServiceManager services)
        => services
            .AddSingleton<TrackedDataRegistry>()
            .AddSingleton<TimeSeriesCacheService>()
            .AddSingleton<CurrencyTrackerService>()
            .AddSingleton<InventoryCacheService>()
            .AddSingleton<InventoryChangeService>()
            .AddSingleton<CharacterDataService>()
            .AddSingleton<FavoritesService>()
            .AddSingleton<AutoRetainerIpcService>();

    /// <summary>
    /// Universalis integration and market data services.
    /// </summary>
    private static ServiceManager AddMarket(this ServiceManager services)
        => services
            .AddSingleton<UniversalisService>()
            .AddSingleton<UniversalisWebSocketService>()
            .AddSingleton<ListingsService>()
            .AddSingleton<MarketDataCacheService>()
            .AddSingleton<PriceTrackingService>();

    /// <summary>
    /// Window and UI services.
    /// </summary>
    private static ServiceManager AddUi(this ServiceManager services)
        => services
            .AddSingleton<WindowService>()
            .AddSingleton<MainWindow>()
            .AddSingleton<ConfigWindow>();
}
