using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OtterGui.Log;
using OtterGui.Services;

namespace Kaleidoscope;

/// <summary>
/// Main plugin entry point. Creates and manages the service container.
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

            // Initialize the static log service for components without DI access
            var dalamudLog = _services.GetService<IPluginLog>();
            Services.LogService.Initialize(dalamudLog);

            // Initialize required services to ensure they are constructed
            _services.GetService<Services.ConfigurationService>();
            _services.GetService<Services.SamplerService>();
            _services.GetService<Services.WindowService>();
            _services.GetService<Services.CommandService>();

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
