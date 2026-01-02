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
/// 
/// Supports category-based filtering to reduce log noise. Categories can be enabled/disabled
/// in the Developer section of the config window.
/// </remarks>
public static class LogService
{
    private static IPluginLog? _log;
    private static Configuration? _config;

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

    /// <summary>
    /// Sets the configuration for category-based filtering.
    /// Should be called after ConfigurationService is available.
    /// </summary>
    public static void SetConfiguration(Configuration config)
    {
        _config = config;
    }

    /// <summary>
    /// Checks if logging is enabled for the specified category.
    /// </summary>
    public static bool IsCategoryEnabled(LogCategory category)
    {
        if (_config == null || !_config.LogCategoryFilteringEnabled)
            return true; // If filtering disabled, all categories pass through
        
        return (_config.EnabledLogCategories & category) != 0;
    }

    // Original methods (no category filtering - for backwards compatibility)
    public static void Verbose(string message) => _log?.Verbose(message);
    public static void Info(string message) => _log?.Information(message);
    public static void Debug(string message) => _log?.Debug(message);
    public static void Warning(string message) => _log?.Warning(message);
    public static void Error(string message) => _log?.Error(message);
    public static void Error(string message, Exception ex) => _log?.Error($"{message}: {ex.Message}");
    public static void Fatal(string message, Exception ex) => _log?.Fatal($"{message}: {ex}");

    // Category-aware logging methods
    
    /// <summary>
    /// Logs a verbose message if the specified category is enabled.
    /// </summary>
    public static void Verbose(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
            _log?.Verbose(message);
    }

    /// <summary>
    /// Logs an info message if the specified category is enabled.
    /// </summary>
    public static void Info(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
            _log?.Information(message);
    }

    /// <summary>
    /// Logs a debug message if the specified category is enabled.
    /// </summary>
    public static void Debug(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
            _log?.Debug(message);
    }

    /// <summary>
    /// Logs a warning message if the specified category is enabled.
    /// </summary>
    public static void Warning(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
            _log?.Warning(message);
    }

    /// <summary>
    /// Logs an error message if the specified category is enabled.
    /// Note: Errors are typically always logged, but this allows filtering if desired.
    /// </summary>
    public static void Error(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
            _log?.Error(message);
    }

    /// <summary>
    /// Logs an error message with exception if the specified category is enabled.
    /// </summary>
    public static void Error(LogCategory category, string message, Exception ex)
    {
        if (IsCategoryEnabled(category))
            _log?.Error($"{message}: {ex.Message}");
    }
}
