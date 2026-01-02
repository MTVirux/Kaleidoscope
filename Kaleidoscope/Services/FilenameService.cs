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

    private string GetLogFilePath()
    {
        if (_config != null && !string.IsNullOrWhiteSpace(_config.FileLoggingDirectory))
        {
            return Path.Combine(_config.FileLoggingDirectory, "kaleidoscope.log");
        }
        return DefaultLogFilePath;
    }
}
