using Dalamud.Plugin.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Provides a static accessor for logging throughout the plugin.
/// Initialized during plugin startup, allowing components without DI access to log properly.
/// </summary>
public static class LogService
{
    private static IPluginLog? _log;

    /// <summary>
    /// Initializes the static log accessor. Call this once during plugin initialization.
    /// </summary>
    public static void Initialize(IPluginLog log) => _log = log;

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public static void Info(string message)
    {
        _log?.Information(message);
    }

    /// <summary>
    /// Logs a debug message.
    /// </summary>
    public static void Debug(string message)
    {
        _log?.Debug(message);
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public static void Warning(string message)
    {
        _log?.Warning(message);
    }

    /// <summary>
    /// Logs an error message.
    /// </summary>
    public static void Error(string message)
    {
        _log?.Error(message);
    }

    /// <summary>
    /// Logs an error message with exception details.
    /// </summary>
    public static void Error(string message, Exception ex)
    {
        _log?.Error($"{message}: {ex.Message}");
    }

    /// <summary>
    /// Logs a fatal error message.
    /// </summary>
    public static void Fatal(string message, Exception ex)
    {
        _log?.Fatal($"{message}: {ex}");
    }
}
