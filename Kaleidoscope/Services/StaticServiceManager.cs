using Dalamud.Plugin;
using Kaleidoscope.Gui.ConfigWindow;
using Kaleidoscope.Gui.MainWindow;
using OtterGui.Log;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Builds the Kaleidoscope service container.
/// </summary>
public static class StaticServiceManager
{
    public static ServiceManager CreateProvider(IDalamudPluginInterface pi, Logger log, KaleidoscopePlugin plugin)
    {
        var services = new ServiceManager(log)
            .AddExistingService(log)
            .AddExistingService(plugin)
            .AddMeta()
            .AddServices()
            .AddUi();

        DalamudServices.AddServices(services, pi);
        services.AddIServices(typeof(KaleidoscopePlugin).Assembly);
        services.CreateProvider();
        return services;
    }

    private static ServiceManager AddMeta(this ServiceManager services)
        => services
            .AddSingleton<FilenameService>()
            .AddSingleton<ConfigurationService>();

    private static ServiceManager AddServices(this ServiceManager services)
        => services
            .AddSingleton<StateService>()
            .AddSingleton<LayoutEditingService>()
            .AddSingleton<AutoRetainerIpcService>()
            .AddSingleton<FavoritesService>()
            .AddSingleton<CharacterDataService>()
            .AddSingleton<UniversalisService>()
            .AddSingleton<UniversalisWebSocketService>()
            .AddSingleton<ListingsService>()
            .AddSingleton<PriceTrackingService>()
            .AddSingleton<TrackedDataRegistry>()
            .AddSingleton<InventoryChangeService>()
            .AddSingleton<TimeSeriesCacheService>()
            .AddSingleton<SamplerService>()
            .AddSingleton<InventoryCacheService>()
            .AddSingleton<ProfilerService>()
            .AddSingleton<CommandService>();

    private static ServiceManager AddUi(this ServiceManager services)
        => services
            .AddSingleton<WindowService>()
            .AddSingleton<MainWindow>()
            .AddSingleton<FullscreenWindow>()
            .AddSingleton<ConfigWindow>();
}
