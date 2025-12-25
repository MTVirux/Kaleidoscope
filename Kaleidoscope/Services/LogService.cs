using Dalamud.Plugin.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Static logging facade for components without DI access (e.g., static methods, libraries).
/// This is a fallback mechanism for static contexts - prefer injecting IPluginLog directly.
/// </summary>
/// <remarks>
/// This pattern is used by InventoryTools and other Dalamud plugins for logging in static
/// contexts where dependency injection is not available. It should be initialized early
/// in the plugin lifecycle and used sparingly.
/// </remarks>
public static class LogService
{
    private static IPluginLog? _log;

    /// <summary>
    /// Gets whether the log service has been initialized.
    /// </summary>
    public static bool IsInitialized => _log != null;

    /// <summary>
    /// Initializes the static log service. Should be called once during plugin startup.
    /// </summary>
    public static void Initialize(IPluginLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public static void Verbose(string message) => _log?.Verbose(message);
    public static void Info(string message) => _log?.Information(message);
    public static void Debug(string message) => _log?.Debug(message);
    public static void Warning(string message) => _log?.Warning(message);
    public static void Error(string message) => _log?.Error(message);
    public static void Error(string message, Exception ex) => _log?.Error($"{message}: {ex.Message}");
    public static void Fatal(string message, Exception ex) => _log?.Fatal($"{message}: {ex}");
}
