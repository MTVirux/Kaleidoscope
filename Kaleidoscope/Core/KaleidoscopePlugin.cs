using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Kaleidoscope.Core;
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

    private readonly ServiceManager? _services;

    /// <summary>
    /// Deferred startup state. Non-null while services are still being loaded,
    /// set to null once startup completes or fails.
    /// </summary>
    private DeferredStartupService? _startup;

    public KaleidoscopePlugin(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            _services = StaticServiceManager.CreateProvider(pluginInterface, Log, this);

            // Lightweight init — these are fast (Dalamud singletons / simple config reads).
            var dalamudLog = _services.GetService<IPluginLog>();
            LogService.Initialize(dalamudLog);

            var configService = _services.GetService<ConfigurationService>();
            var filenameService = _services.GetService<FilenameService>();
            filenameService.SetConfiguration(configService.Config);
            LogService.SetConfiguration(configService.Config);

            var playerState = _services.GetService<IPlayerState>();
            var objectTable = _services.GetService<IObjectTable>();
            GameStateService.Initialize(playerState, objectTable);

            // Kick off deferred startup — resolves services on a background thread
            // while a Framework.Update handler polls progress for the notification.
            _startup = new DeferredStartupService(_services, typeof(KaleidoscopePlugin).Assembly);
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
        _startup?.Dispose();
        _startup = null;

        LogService.Shutdown();
        GameStateService.Cleanup();
        _services?.Dispose();
        Log.Information("Kaleidoscope disposed.");
    }
}
