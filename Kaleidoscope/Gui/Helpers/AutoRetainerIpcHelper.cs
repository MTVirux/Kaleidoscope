using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.Helpers;

/// <summary>
/// Helper methods for safely calling AutoRetainer IPC methods.
/// </summary>
public static class AutoRetainerIpcHelper
{
    /// <summary>
    /// Safely retrieves character world data from AutoRetainer IPC.
    /// Returns an empty dictionary if the service is unavailable or an error occurs.
    /// </summary>
    /// <param name="autoRetainerService">The AutoRetainer IPC service (may be null).</param>
    /// <returns>Dictionary mapping character IDs to world names.</returns>
    public static Dictionary<ulong, string> GetCharacterWorlds(AutoRetainerService? autoRetainerService)
    {
        var characterWorlds = new Dictionary<ulong, string>();

        if (autoRetainerService == null || !autoRetainerService.IsAvailable)
            return characterWorlds;

        try
        {
            var arData = autoRetainerService.GetAllCharacterData();
            foreach (var (_, world, _, cid) in arData)
            {
                if (!string.IsNullOrEmpty(world))
                {
                    characterWorlds[cid] = world;
                }
            }
        }
        catch (Exception)
        {
            // AutoRetainer IPC not available, returning empty dictionary
        }

        return characterWorlds;
    }

    /// <summary>
    /// Safely executes an AutoRetainer IPC call, returning a default value on failure.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="autoRetainerService">The AutoRetainer IPC service (may be null).</param>
    /// <param name="action">The action to execute.</param>
    /// <param name="defaultValue">The default value to return on failure.</param>
    /// <returns>The result of the action, or the default value on failure.</returns>
    public static T SafeCall<T>(AutoRetainerService? autoRetainerService, Func<AutoRetainerService, T> action, T defaultValue)
    {
        if (autoRetainerService == null || !autoRetainerService.IsAvailable)
            return defaultValue;

        try
        {
            return action(autoRetainerService);
        }
        catch (Exception)
        {
            // AutoRetainer IPC not available, returning default value
            return defaultValue;
        }
    }
}
