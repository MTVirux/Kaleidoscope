using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Kaleidoscope.Libs;
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

            // Initialize static log service for components without DI access
            var dalamudLog = _services.GetService<IPluginLog>();
            LogService.Initialize(dalamudLog);

            // Initialize static services that need Dalamud service references
            var playerState = _services.GetService<IPlayerState>();
            var objectTable = _services.GetService<IObjectTable>();
            GameStateService.Initialize(playerState, objectTable);
            CharacterLib.Initialize(playerState, objectTable);

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
