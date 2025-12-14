using Dalamud.Plugin;
using Kaleidoscope.Gui.ConfigWindow;
using Kaleidoscope.Gui.MainWindow;
using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using OtterGui.Log;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Static factory for building the Kaleidoscope service container.
/// </summary>
public static class StaticServiceManager
{
    public static ServiceManager CreateProvider(IDalamudPluginInterface pi, Logger log, KaleidoscopePlugin plugin)
    {
        // Initialize ECommons services for static accessor patterns that some code may still use.
        ECommons.DalamudServices.Svc.Init(pi);

        var services = new ServiceManager(log)
            .AddExistingService(log)
            .AddExistingService(plugin)
            .AddMeta()
            .AddServices()
            .AddUi();

        DalamudServices.AddServices(services, pi);

        // Auto-register all IService implementations from the assembly
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
            .AddSingleton<SamplerService>()
            .AddSingleton<CommandService>();

    private static ServiceManager AddUi(this ServiceManager services)
        => services
            .AddSingleton<WindowService>()
            .AddSingleton<MainWindow>()
            .AddSingleton<FullscreenWindow>()
            .AddSingleton<ConfigWindow>()
            .AddSingleton<GilTrackerComponent>();
}
