using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services;

/// <summary>
/// Pure decision logic for <see cref="AutoRetainerFcPointsSyncService"/>, kept Dalamud-free so it
/// can be unit-tested without the service's plugin dependencies.
/// </summary>
public static class AutoRetainerFcPointsSyncPolicy
{
    /// <summary>
    /// Whether an FC-points entry should be recorded: always when forced (the manual import,
    /// which is authoritative because the live FC-credit read is only reliable while the FC Credit
    /// Shop is open), otherwise only when there is no stored value or the config value is strictly
    /// newer than what's stored. The strict comparison makes the background re-read of unchanged
    /// config a no-op and stops stale config data from clobbering a fresher live capture.
    /// </summary>
    public static bool ShouldRecord(Resource? existing, DateTime entryUpdatedAt, bool force)
        => force || existing is null || existing.Value.UpdatedAt < entryUpdatedAt;
}
