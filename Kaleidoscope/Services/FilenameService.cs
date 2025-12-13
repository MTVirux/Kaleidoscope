using Dalamud.Plugin;

namespace Kaleidoscope.Services;

/// <summary>
/// Provides commonly used file paths for the plugin.
/// </summary>
public class FilenameService
{
    public string ConfigDirectory { get; }
    public string ConfigFile { get; }
    
    /// <summary>
    /// Path to the main Kaleidoscope SQLite database.
    /// Stores time-series data for gil tracking, inventory snapshots, currencies, etc.
    /// </summary>
    public string DatabasePath { get; }

    public FilenameService(IDalamudPluginInterface pi)
    {
        ConfigDirectory = pi.GetPluginConfigDirectory();
        ConfigFile = pi.ConfigFile.FullName;
        DatabasePath = Path.Combine(ConfigDirectory, "kaleidoscope.sqlite");
    }
}
