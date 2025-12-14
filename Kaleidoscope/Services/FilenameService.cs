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
    public string ConfigDirectory { get; }
    public string ConfigFile { get; }
    public string DatabasePath { get; }

    public FilenameService(IDalamudPluginInterface pi)
    {
        ConfigDirectory = pi.GetPluginConfigDirectory();
        ConfigFile = pi.ConfigFile.FullName;
        DatabasePath = Path.Combine(ConfigDirectory, "kaleidoscope.sqlite");
    }
}
