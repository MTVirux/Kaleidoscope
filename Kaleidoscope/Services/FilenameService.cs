using Dalamud.Plugin;

namespace Kaleidoscope.Services;

/// <summary>
/// Provides commonly used file paths for the plugin.
/// </summary>
public class FilenameService
{
    public string ConfigDirectory { get; }
    public string ConfigFile { get; }
    public string GilTrackerDbPath { get; }

    public FilenameService(IDalamudPluginInterface pi)
    {
        ConfigDirectory = pi.GetPluginConfigDirectory();
        ConfigFile = pi.ConfigFile.FullName;
        GilTrackerDbPath = Path.Combine(ConfigDirectory, "giltracker.sqlite");
    }
}
