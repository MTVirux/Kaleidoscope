using Dalamud.Plugin;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Provides commonly used file paths for the plugin.
/// </summary>
/// <remarks>
/// Follows the Glamourer pattern for centralized file path management.
/// All paths are computed once at construction for consistency.
/// </remarks>
public sealed class FilenameService : IService
{
    /// <summary>
    /// Static accessor for components without DI access.
    /// </summary>
    public static FilenameService? Instance { get; private set; }

    public string ConfigDirectory { get; }
    public string ConfigFile { get; }
    public string DatabasePath { get; }
    
    /// <summary>
    /// Gets the log file path, using custom directory if configured.
    /// </summary>
    public string LogFilePath => GetLogFilePath();
    
    /// <summary>
    /// Gets the default log file path (in plugin config directory).
    /// </summary>
    public string DefaultLogFilePath { get; }

    /// <summary>
    /// Gets the base log directory, using custom directory if configured.
    /// </summary>
    public string LogDirectory => GetLogDirectory();

    private Configuration? _config;

    public FilenameService(IDalamudPluginInterface pi)
    {
        ConfigDirectory = pi.GetPluginConfigDirectory();
        ConfigFile = pi.ConfigFile.FullName;
        DatabasePath = Path.Combine(ConfigDirectory, "kaleidoscope.sqlite");
        DefaultLogFilePath = Path.Combine(ConfigDirectory, "kaleidoscope.log");
        Instance = this;
    }

    /// <summary>
    /// Sets the configuration for custom log directory support.
    /// </summary>
    public void SetConfiguration(Configuration config)
    {
        _config = config;
    }

    private string GetLogDirectory()
    {
        if (_config != null && !string.IsNullOrWhiteSpace(_config.FileLoggingDirectory))
        {
            return _config.FileLoggingDirectory;
        }
        return ConfigDirectory;
    }

    private string GetLogFilePath()
    {
        return Path.Combine(GetLogDirectory(), "kaleidoscope.log");
    }

    /// <summary>
    /// Gets the log file path for a specific category.
    /// </summary>
    /// <param name="category">The log category.</param>
    /// <returns>The path to the category-specific log file.</returns>
    public string GetCategoryLogFilePath(LogCategory category)
    {
        var categoryName = GetCategoryFileName(category);
        return Path.Combine(GetLogDirectory(), $"kaleidoscope_{categoryName}.log");
    }

    /// <summary>
    /// Gets the log file path for a specific character.
    /// </summary>
    /// <param name="characterName">The character name (used for the subdirectory).</param>
    /// <returns>The path to the character-specific log file.</returns>
    public string GetCharacterLogFilePath(string characterName)
    {
        var safeName = SanitizeFileName(characterName);
        var charDir = Path.Combine(GetLogDirectory(), "logs", safeName);
        return Path.Combine(charDir, "kaleidoscope.log");
    }

    /// <summary>
    /// Gets the log file path for a specific character and category.
    /// </summary>
    /// <param name="characterName">The character name (used for the subdirectory).</param>
    /// <param name="category">The log category.</param>
    /// <returns>The path to the character and category-specific log file.</returns>
    public string GetCharacterCategoryLogFilePath(string characterName, LogCategory category)
    {
        var safeName = SanitizeFileName(characterName);
        var categoryName = GetCategoryFileName(category);
        var charDir = Path.Combine(GetLogDirectory(), "logs", safeName);
        return Path.Combine(charDir, $"kaleidoscope_{categoryName}.log");
    }

    /// <summary>
    /// Converts a LogCategory enum value to a safe file name suffix.
    /// </summary>
    private static string GetCategoryFileName(LogCategory category)
    {
        // Handle individual categories (not combined flags)
        return category switch
        {
            LogCategory.Database => "database",
            LogCategory.Cache => "cache",
            LogCategory.GameState => "gamestate",
            LogCategory.PriceTracking => "pricetracking",
            LogCategory.Universalis => "universalis",
            LogCategory.AutoRetainer => "autoretainer",
            LogCategory.CurrencyTracker => "currencytracker",
            LogCategory.Inventory => "inventory",
            LogCategory.Character => "character",
            LogCategory.Layout => "layout",
            LogCategory.UI => "ui",
            LogCategory.Listings => "listings",
            LogCategory.Config => "config",
            _ => "general" // For None, All, or unknown categories
        };
    }

    /// <summary>
    /// Sanitizes a string to be safe for use as a file or directory name.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        
        // Replace spaces with underscores for cleaner paths
        sanitized = sanitized.Replace(' ', '_');
        
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
