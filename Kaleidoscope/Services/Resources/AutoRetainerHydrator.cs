using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Database;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// On startup, pulls character and retainer metadata from AutoRetainer's IPC and
/// populates owner_names (so the data table shows real names for offline owners)
/// plus the SpecialPlayer/Gil and RetainerGil rows in `resources` (so cross-character
/// gil totals are accurate without requiring user to log into each character).
///
/// Inventory items are NOT pulled from AutoRetainer — those continue to come from
/// our own capture pipeline. AutoRetainer's data is best-effort: stale (as of last
/// AR run) but better than nothing for offline owners.
///
/// Live captures from our own pipeline always win on conflict — they upsert and
/// overwrite AR-seeded data when the user actually logs into / opens an owner.
///
/// ResourceStoreHydrator is listed as an explicit constructor dependency to ensure
/// DI constructs it first, so the "skip if already exists" guard checks against
/// the most recent live data already loaded from the DB.
/// </summary>
public sealed class AutoRetainerHydrator : IRequiredService
{
    public AutoRetainerHydrator(
        AutoRetainerService ar,
        KaleidoscopeDbService db,
        ResourceStore store,
        ResourceObservationService obs,
        ResourceStoreHydrator _ /* explicit dep to force ordering */)
    {
        if (!ar.IsAvailable)
        {
            LogService.Debug(LogCategory.Inventory, "[AutoRetainerHydrator] AutoRetainer IPC not available; skipping seed");
            return;
        }

        try
        {
            var characters = ar.GetAllFullCharacterData();
            int charSeeded = 0;
            int retainerSeeded = 0;
            int retainerIdsMissing = 0;

            foreach (var ch in characters)
            {
                if (ch.CID == 0 || string.IsNullOrEmpty(ch.Name)) continue;

                // Player owner name + world
                db.UpsertOwnerName(ch.CID, OwnerKind.Player, ch.Name, ch.World);

                // Player gil — only seed if we don't already have a fresher live row.
                // ResourceStore is hydrated from DB before this hydrator runs (DI ordering),
                // so we can check it.
                var playerGilKey = new ResourceKey
                {
                    OwnerId = ch.CID,
                    OwnerKind = OwnerKind.Player,
                    Container = Container.SpecialPlayer,
                    ItemId = ResourceCatalog.GilItemId,
                    Slot = -1,
                };
                var existingPlayerGil = store.Get(playerGilKey);
                if (existingPlayerGil == null)
                {
                    obs.RecordObservation(new ResourceObservation
                    {
                        Key = playerGilKey,
                        Quantity = ch.Gil,
                        UpdatedAt = DateTime.UtcNow,
                        ParentOwnerId = 0,
                    });
                }

                charSeeded++;

                // Retainers
                foreach (var ret in ch.Retainers)
                {
                    if (string.IsNullOrEmpty(ret.Name)) continue;

                    if (ret.RetainerId == 0)
                    {
                        retainerIdsMissing++;
                        // Can't write owner_names or resources without a valid ID — skip
                        continue;
                    }

                    db.UpsertOwnerName(ret.RetainerId, OwnerKind.Retainer, ret.Name);

                    var retainerGilKey = new ResourceKey
                    {
                        OwnerId = ret.RetainerId,
                        OwnerKind = OwnerKind.Retainer,
                        Container = Container.RetainerGil,
                        ItemId = ResourceCatalog.GilItemId,
                        Slot = -1,
                    };
                    var existingRetainerGil = store.Get(retainerGilKey);
                    if (existingRetainerGil == null && ret.Gil > 0)
                    {
                        obs.RecordObservation(new ResourceObservation
                        {
                            Key = retainerGilKey,
                            Quantity = ret.Gil,
                            UpdatedAt = DateTime.UtcNow,
                            ParentOwnerId = ch.CID,
                        });
                    }

                    retainerSeeded++;
                }
            }

            if (retainerIdsMissing > 0)
                LogService.Warning(LogCategory.Inventory, $"[AutoRetainerHydrator] {retainerIdsMissing} retainer(s) had no ID (AR IPC may not expose RetainerID/RetainerId/Id); retainer name/gil seeding skipped for those");

            LogService.Info(LogCategory.Inventory, $"[AutoRetainerHydrator] Seeded {charSeeded} characters and {retainerSeeded} retainers from AutoRetainer IPC");
        }
        catch (Exception ex)
        {
            LogService.Warning(LogCategory.Inventory, $"[AutoRetainerHydrator] Seed failed: {ex.Message}");
        }
    }
}
