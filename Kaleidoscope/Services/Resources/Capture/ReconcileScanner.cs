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

    private static readonly InventoryType[] RetainerContainers =
    {
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3,
        InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
        InventoryType.RetainerEquippedItems, InventoryType.RetainerCrystals, InventoryType.RetainerMarket,
    };

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

        // Persist the retainer's name so the data table can display it.
        var retainerName = _gameState.GetActiveRetainerName();
        if (!string.IsNullOrEmpty(retainerName))
            _db.UpsertOwnerName(rid, OwnerKind.Retainer, retainerName);

        foreach (var type in RetainerContainers)
        {
            if (!ResourceCatalog.TryMapContainer((int)type, out var container)) continue;
            ScanContainer(im, type, container, rid, OwnerKind.Retainer);
            _loaded.Add(rid, container);
        }
    }

    private void OnRetainerClosed()
    {
        var rid = _gameState.GetActiveRetainerId();
        foreach (var type in RetainerContainers)
        {
            if (ResourceCatalog.TryMapContainer((int)type, out var container))
                _loaded.Remove(rid, container);
        }
    }

    private unsafe void ScanContainer(InventoryManager* im, InventoryType type, Container container, ulong ownerId, OwnerKind kind)
    {
        var c = im->GetInventoryContainer(type);
        if (c == null || !c->IsLoaded) return;

        var parentOwnerId = kind == OwnerKind.Retainer ? _gameState.PlayerContentId : 0UL;

        for (int i = 0; i < c->GetSize(); i++)
        {
            var slot = c->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;

            var key = new ResourceKey { OwnerId = ownerId, OwnerKind = kind, Container = container, ItemId = slot->ItemId, Slot = slot->Slot };
            _service.RecordObservation(InventorySlotMapper.FromInventorySlot(slot, key, parentOwnerId));
        }
    }

    public void Dispose()
    {
        _changes.OnRetainerInventoryReady -= OnRetainerReady;
        _changes.OnRetainerClosed         -= OnRetainerClosed;
    }
}
