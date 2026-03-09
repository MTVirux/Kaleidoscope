using Dalamud.Plugin;
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
            .AddExistingService(plugin);

        // Register Dalamud-provided services (must be before auto-discovery)
        DalamudServices.AddServices(services, pi);

        // Auto-discover and register all services implementing IService/IRequiredService
        // This includes: services, windows, widgets, and UI components
        services.AddIServices(typeof(KaleidoscopePlugin).Assembly);

        services.CreateProvider();
        return services;
    }
}
