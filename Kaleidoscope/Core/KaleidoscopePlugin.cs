using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Kaleidoscope.Services;
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
            _services = StaticServiceManager.CreateProvider(pluginInterface, Log, this);

            var dalamudLog = _services.GetService<IPluginLog>();
            LogService.Initialize(dalamudLog);

            // Set up FilenameService with config BEFORE LogService so file logging paths are ready
            var configService = _services.GetService<ConfigurationService>();
            var filenameService = _services.GetService<FilenameService>();
            filenameService.SetConfiguration(configService.Config);
            
            // Set up configuration for category-based log filtering and file logging
            LogService.SetConfiguration(configService.Config);

            var playerState = _services.GetService<IPlayerState>();
            var objectTable = _services.GetService<IObjectTable>();
            GameStateService.Initialize(playerState, objectTable);

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
        LogService.Shutdown();
        _services?.Dispose();
        Log.Information("Kaleidoscope disposed.");
    }
}
