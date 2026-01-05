using Dalamud.Plugin;
using OtterGui.Log;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Builds the Kaleidoscope service container.
/// </summary>
/// <remarks>
/// <para>
/// Service registration follows the OtterGui pattern used by Glamourer:
/// - Services implementing <see cref="IService"/> are auto-discovered and lazy-loaded
/// - Services implementing <see cref="IRequiredService"/> are auto-discovered and eagerly initialized
/// - Dalamud services are registered explicitly via <see cref="DalamudServices"/>
/// </para>
/// <para>
/// Auto-discovery via <see cref="ServiceManager.AddIServices"/> scans the assembly for all types
/// implementing IService or IRequiredService and registers them as singletons automatically.
/// This eliminates the need for explicit AddSingleton calls and prevents double-registration.
/// </para>
/// </remarks>
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
