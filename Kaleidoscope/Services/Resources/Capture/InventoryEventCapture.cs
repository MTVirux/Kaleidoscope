using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Subscribes to IGameInventory.InventoryChangedRaw and forwards each per-slot change
/// to ResourceObservationService.RecordObservation. Cosmopouch1/Cosmopouch2 events
/// are skipped here — see CosmopouchCapture for the workaround until Dalamud #2329 lands.
/// </summary>
public sealed class InventoryEventCapture : IDisposable, IRequiredService
{
    private readonly IGameInventory _gameInventory;
    private readonly IClientState _clientState;
    private readonly ResourceObservationService _service;

    public InventoryEventCapture(IGameInventory gameInventory, IClientState clientState, ResourceObservationService service)
    {
        _gameInventory = gameInventory;
        _clientState = clientState;
        _service = service;
        _gameInventory.InventoryChangedRaw += OnInventoryChangedRaw;
    }

    private void OnInventoryChangedRaw(IReadOnlyCollection<InventoryEventArgs> events)
    {
        if (!_clientState.IsLoggedIn) return;

        foreach (var e in events)
        {
            // Skip Cosmopouch — handled by direct InventoryManager scan due to Dalamud #2329
            if (e.Item.ContainerType.ToString().StartsWith("Cosmopouch")) continue;

            if (!ResourceCatalog.TryMapContainer((int)e.Item.ContainerType, out var container)) continue;

            var ownerId = ResolveOwnerId(e.Item.ContainerType);
            var ownerKind = ResolveOwnerKind(e.Item.ContainerType);
            if (ownerId == 0) continue;

            var slot = (short)e.Item.InventorySlot;
            var parentId = ownerKind == OwnerKind.Player ? 0UL : GameStateService.PlayerContentId;

            // ItemId=0 means the slot was cleared (all items traded/consumed). Translate to a
            // zero-quantity observation on whatever real item was previously in that slot so the
            // store entry for the real item is zeroed out rather than leaving a stale quantity.
            if (e.Item.ItemId == 0)
            {
                var previousItemId = _service.Store.GetItemIdForSlot(ownerId, ownerKind, container, slot);
                if (previousItemId is null) continue;

                _service.RecordObservation(new ResourceObservation
                {
                    Key = new ResourceKey
                    {
                        OwnerId   = ownerId,
                        OwnerKind = ownerKind,
                        Container = container,
                        ItemId    = previousItemId.Value,
                        Slot      = slot,
                    },
                    Quantity      = 0,
                    Flags         = ResourceFlags.None,
                    UpdatedAt     = DateTime.UtcNow,
                    ParentOwnerId = parentId,
                });
                continue;
            }

            var flags = ResourceFlags.None;
            if (e.Item.IsHq)
                flags |= ResourceFlags.HQ;
            var isCollectable = e.Item.IsCollectable;
            if (isCollectable)
                flags |= ResourceFlags.Collectable;

            _service.RecordObservation(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = ownerId,
                    OwnerKind = ownerKind,
                    Container = container,
                    ItemId    = e.Item.ItemId,
                    Slot      = slot,
                },
                Quantity       = e.Item.Quantity,
                Flags          = flags,
                Spiritbond     = (ushort)(isCollectable ? 0 : e.Item.SpiritbondOrCollectability),
                Collectability = (ushort)(isCollectable ? e.Item.SpiritbondOrCollectability : 0),
                Condition      = (ushort)e.Item.Condition,
                GlamourId      = e.Item.GlamourId,
                UpdatedAt      = DateTime.UtcNow,
                ParentOwnerId  = parentId,
            });
        }
    }

    /// <summary>
    /// Resolve owner_id for a Dalamud GameInventoryType. Player containers → PlayerContentId.
    /// Retainer containers → active retainer id. FC containers → 0 (FC owner resolution
    /// is deferred; the FC chest reconcile-scan in ReconcileScanner Task 26 handles
    /// FC bag identity properly).
    /// </summary>
    private static ulong ResolveOwnerId(GameInventoryType type)
    {
        var typeInt = (int)type;
        // Retainer page / equipped / gil / crystals / market (10000-12999)
        if (typeInt >= 10000 && typeInt < 13000)
            return GameStateService.GetActiveRetainerId();

        // FC pages / crystals / gil (20000-22999) — FC owner id resolution deferred
        if (typeInt >= 20000 && typeInt < 23000)
            return 0;

        // Otherwise — player
        return GameStateService.PlayerContentId;
    }

    private static OwnerKind ResolveOwnerKind(GameInventoryType type)
    {
        var typeInt = (int)type;
        if (typeInt >= 10000 && typeInt < 13000) return OwnerKind.Retainer;
        if (typeInt >= 20000 && typeInt < 23000) return OwnerKind.FreeCompany;
        return OwnerKind.Player;
    }

    public void Dispose() => _gameInventory.InventoryChangedRaw -= OnInventoryChangedRaw;
}
