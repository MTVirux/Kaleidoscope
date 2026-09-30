using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Database;
using Kaleidoscope.Services.Inventory;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Full-container scans triggered on retainer-open. Catches drift from offline changes
/// (returned ventures, market sales) that occurred while the container wasn't loaded.
/// After a successful scan, the relevant (owner, container) entries are added to
/// LoadedContainerSet; on retainer close, they're removed but the cached snapshot is
/// preserved.
/// FC chest reconcile is deferred — InventoryEventCapture handles FC bag changes while
/// the chest is open. A future task can add an OnFreeCompanyChestReady analog.
/// </summary>
public sealed class ReconcileScanner : IDisposable, IRequiredService
{
    private readonly InventoryChangeService _changes;
    private readonly LoadedContainerSet _loaded;
    private readonly ResourceObservationService _service;
    private readonly KaleidoscopeDbService _db;
    private readonly GameStateService _gameState;

    public ReconcileScanner(InventoryChangeService changes, LoadedContainerSet loaded, ResourceObservationService service, KaleidoscopeDbService db, GameStateService gameState)
    {
        _changes = changes;
        _loaded = loaded;
        _service = service;
        _db = db;
        _gameState = gameState;
        _changes.OnRetainerInventoryReady += OnRetainerReady;
        _changes.OnRetainerClosed         += OnRetainerClosed;
    }

    private unsafe void OnRetainerReady()
    {
        var im = _gameState.InventoryManagerInstance();
        if (im == null) return;
        var rid = _gameState.GetActiveRetainerId();
        if (rid == 0) return;

        // Persist the retainer's name so the data table can display it. Off-thread: this runs
        // inside the framework tick and the write lock may be held by a background batch flush.
        var retainerName = _gameState.GetActiveRetainerName();
        if (!string.IsNullOrEmpty(retainerName))
            _ = Task.Run(() => _db.UpsertOwnerName(rid, OwnerKind.Retainer, retainerName));

        // Accumulate every slot across all containers into one batch so the whole retainer sweep
        // commits under a single observation-lock acquisition rather than ~350 of them.
        var batch = new List<ResourceObservation>();
        foreach (var type in InventoryConstants.RetainerScanContainers)
        {
            if (!ResourceCatalog.TryMapContainer((int)type, out var container)) continue;
            ScanContainer(im, type, container, rid, OwnerKind.Retainer, batch);
            _loaded.Add(rid, container);
        }

        _service.RecordObservations(batch);
    }

    private void OnRetainerClosed()
    {
        var rid = _gameState.GetActiveRetainerId();
        foreach (var type in InventoryConstants.RetainerScanContainers)
        {
            if (ResourceCatalog.TryMapContainer((int)type, out var container))
                _loaded.Remove(rid, container);
        }
    }

    private unsafe void ScanContainer(InventoryManager* im, InventoryType type, Container container, ulong ownerId, OwnerKind kind, List<ResourceObservation> batch)
    {
        var c = im->GetInventoryContainer(type);
        if (c == null || !c->IsLoaded) return;

        var parentOwnerId = kind == OwnerKind.Retainer ? _gameState.PlayerContentId : 0UL;

        var seen = new HashSet<(short Slot, uint ItemId)>();
        var live = new List<ResourceObservation>();
        for (int i = 0; i < c->GetSize(); i++)
        {
            var slot = c->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;

            var key = new ResourceKey { OwnerId = ownerId, OwnerKind = kind, Container = container, ItemId = slot->ItemId, Slot = slot->Slot };
            live.Add(InventorySlotMapper.FromInventorySlot(slot, key, parentOwnerId));
            seen.Add((slot->Slot, slot->ItemId));
        }

        // Zero out stored entries that are no longer in the container. Queued before the live rows
        // so the store's slot index ends up pointing at the current occupant.
        foreach (var (slot, itemId) in _service.Store.GetOccupiedSlots(ownerId, kind, container))
        {
            if (seen.Contains((slot, itemId))) continue;

            batch.Add(new ResourceObservation
            {
                Key           = new ResourceKey { OwnerId = ownerId, OwnerKind = kind, Container = container, ItemId = itemId, Slot = slot },
                Quantity      = 0,
                Flags         = ResourceFlags.None,
                UpdatedAt     = DateTime.UtcNow,
                ParentOwnerId = parentOwnerId,
            });
        }

        batch.AddRange(live);
    }

    public void Dispose()
    {
        _changes.OnRetainerInventoryReady -= OnRetainerReady;
        _changes.OnRetainerClosed         -= OnRetainerClosed;
    }
}
