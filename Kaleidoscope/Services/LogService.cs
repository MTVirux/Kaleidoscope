using Dalamud.Plugin.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Static logging facade for components without DI access (e.g., static methods, libraries).
/// Prefer injecting IPluginLog directly when possible.
/// </summary>
public static class LogService
{
    private static IPluginLog? _log;

    public static void Initialize(IPluginLog log) => _log = log;

    public static void Info(string message) => _log?.Information(message);
    public static void Debug(string message) => _log?.Debug(message);
    public static void Warning(string message) => _log?.Warning(message);
    public static void Error(string message) => _log?.Error(message);
    public static void Error(string message, Exception ex) => _log?.Error($"{message}: {ex.Message}");
    public static void Fatal(string message, Exception ex) => _log?.Fatal($"{message}: {ex}");
}
