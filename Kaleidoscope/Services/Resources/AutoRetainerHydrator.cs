using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Database;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// On a FRESH DB (empty resources table — first install or after the user deletes the
/// DB), pulls character + retainer name metadata from AutoRetainer's IPC and populates
/// owner_names. The seeded names let the data table show meaningful identifiers for
/// owners we haven't observed yet through our own capture pipeline.
///
/// Does NOT seed gil or any quantity data — those flow exclusively from our own capture
/// (MemoryPoller, ReconcileScanner, InventoryEventCapture).
///
/// Skips entirely if the DB already has resources rows — on normal startups this is a
/// no-op. The hydrator's only job is initial-state seeding from AR's offline knowledge.
/// </summary>
public sealed class AutoRetainerHydrator : IRequiredService
{
    private readonly AutoRetainerService _ar;
    private readonly KaleidoscopeDbService _db;
    private readonly ResourceStore _store;

    public AutoRetainerHydrator(
        AutoRetainerService ar,
        KaleidoscopeDbService db,
        ResourceStore store,
        ResourceStoreHydrator _ /* DI ordering: ensure ResourceStore is hydrated from DB before we check it */)
    {
        _ar = ar;
        _db = db;
        _store = store;

        // Skip on non-fresh DB. ResourceStore is hydrated from `resources` before we run, so
        // an empty snapshot is the canonical signal for "fresh DB or freshly-deleted DB".
        if (_store.Snapshot().Count > 0)
        {
            LogService.Debug(LogCategory.Inventory, "[AutoRetainerHydrator] resources table not empty; skipping initial AR seed");
            return;
        }

        SeedNamesFromAutoRetainer("initial");
    }

    /// <summary>
    /// Public entry point — call after Clear DB to re-seed names from AutoRetainer without
    /// requiring a plugin reload. Caller is responsible for ensuring the DB has been cleared
    /// and ResourceStore has been wiped before calling this.
    /// </summary>
    public void Reseed()
    {
        SeedNamesFromAutoRetainer("clear-db reseed");
    }

    private void SeedNamesFromAutoRetainer(string reason)
    {
        if (!_ar.IsAvailable)
        {
            LogService.Debug(LogCategory.Inventory, $"[AutoRetainerHydrator] AutoRetainer IPC not available; skipping {reason} seed");
            return;
        }

        try
        {
            var characters = _ar.GetAllFullCharacterData();
            int charSeeded = 0;
            int retainerSeeded = 0;
            int retainersWithoutId = 0;

            foreach (var ch in characters)
            {
                if (ch.CID == 0 || string.IsNullOrEmpty(ch.Name)) continue;

                _db.UpsertOwnerName(ch.CID, OwnerKind.Player, ch.Name, ch.World);
                charSeeded++;

                foreach (var ret in ch.Retainers)
                {
                    if (string.IsNullOrEmpty(ret.Name)) continue;
                    if (ret.RetainerId == 0)
                    {
                        retainersWithoutId++;
                        continue;
                    }

                    _db.UpsertOwnerName(ret.RetainerId, OwnerKind.Retainer, ret.Name);
                    retainerSeeded++;
                }
            }

            LogService.Info(LogCategory.Inventory, $"[AutoRetainerHydrator] {reason}: seeded {charSeeded} characters and {retainerSeeded} retainers from AutoRetainer IPC");
            if (retainersWithoutId > 0)
            {
                LogService.Warning(LogCategory.Inventory, $"[AutoRetainerHydrator] {retainersWithoutId} retainers from AR had no RetainerId — name not seeded");
            }
        }
        catch (Exception ex)
        {
            LogService.Warning(LogCategory.Inventory, $"[AutoRetainerHydrator] {reason} seed failed: {ex.Message}");
        }
    }
}
