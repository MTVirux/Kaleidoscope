using Kaleidoscope.Services.Database;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// Pre-loads ResourceStore from the resources table on plugin startup. Without this,
/// the in-memory store starts empty and only fills as capture events fire — meaning
/// offline characters and offline retainers are invisible to read consumers (data tools,
/// graphs, cross-character aggregates) until those owners come online.
/// </summary>
public sealed class ResourceStoreHydrator : IRequiredService
{
    public ResourceStoreHydrator(ResourceStore store, KaleidoscopeDbService db)
    {
        var purged = db.PurgeStaleRetainerGilRows();
        if (purged > 0)
            LogService.Info(LogCategory.Inventory, $"[ResourceStoreHydrator] Purged {purged} stale Phase 1 retainer-gil rows");
        var purgedZero = db.PurgeZeroOwnerRetainerRows();
        if (purgedZero > 0)
            LogService.Info(LogCategory.Inventory, $"[ResourceStoreHydrator] Purged {purgedZero} legacy retainer rows with owner_id=0");
        var loaded = db.LoadAllResourcesInto(store);
        LogService.Info(LogCategory.Inventory, $"[ResourceStoreHydrator] Pre-loaded {loaded} resources into in-memory store");
    }
}
