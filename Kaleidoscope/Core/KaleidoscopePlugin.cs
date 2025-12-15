using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OtterGui.Log;
using OtterGui.Services;

namespace Kaleidoscope;

/// <summary>
/// Main plugin entry point.
/// </summary>
public sealed class KaleidoscopePlugin : IDalamudPlugin
{
    public static readonly Logger Log = new();

    private readonly ServiceManager _services;

    public KaleidoscopePlugin(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            _services = Services.StaticServiceManager.CreateProvider(pluginInterface, Log, this);

            // Initialize static log service for components without DI access
            var dalamudLog = _services.GetService<IPluginLog>();
            Services.LogService.Initialize(dalamudLog);

            // Initialize all services marked with IRequiredService
            // This follows the Glamourer pattern for service initialization
            _services.EnsureRequiredServices();

            Log.Information("Kaleidoscope loaded successfully.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to initialize Kaleidoscope: {ex}");
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _services?.Dispose();
        Log.Information("Kaleidoscope disposed.");
    }
}
