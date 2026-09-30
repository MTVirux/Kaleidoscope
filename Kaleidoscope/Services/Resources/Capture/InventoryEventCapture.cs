using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Subscribes to IGameInventory.InventoryChangedRaw and forwards each per-slot change
/// to ResourceObservationService.RecordObservation. Containers not tracked by the plugin
/// (e.g. Cosmopouch1/2) fail TryMapContainer and are dropped.
/// </summary>
public sealed class InventoryEventCapture : IDisposable, IRequiredService
{
    private readonly IGameInventory _gameInventory;
    private readonly IClientState _clientState;
    private readonly ResourceObservationService _service;
    private readonly GameStateService _gameState;

    public InventoryEventCapture(IGameInventory gameInventory, IClientState clientState, ResourceObservationService service, GameStateService gameState)
    {
        _gameInventory = gameInventory;
        _clientState = clientState;
        _service = service;
        _gameState = gameState;
        _gameInventory.InventoryChangedRaw += OnInventoryChangedRaw;
    }

    private void OnInventoryChangedRaw(IReadOnlyCollection<InventoryEventArgs> events)
    {
        if (!_clientState.IsLoggedIn) return;

        foreach (var e in events)
        {
            if (!ResourceCatalog.TryMapContainer((int)e.Item.ContainerType, out var container)) continue;

            var ownerId = ResolveOwnerId(e.Item.ContainerType);
            var ownerKind = ResolveOwnerKind(e.Item.ContainerType);
            if (ownerId == 0) continue;

            var slot = (short)e.Item.InventorySlot;
            var parentId = ownerKind == OwnerKind.Player ? 0UL : _gameState.PlayerContentId;

            // A Removed event carries the item that LEFT the slot (old id and quantity), not an empty
            // slot, so it must be recorded as zero rather than re-recorded as-is.
            if (e.Type == GameInventoryEvent.Removed || e.Item.ItemId == 0)
            {
                var removedItemId = e.Item.ItemId != 0
                    ? e.Item.ItemId
                    : _service.Store.GetItemIdForSlot(ownerId, ownerKind, container, slot);
                if (removedItemId is { } id)
                    RecordCleared(ownerId, ownerKind, container, slot, id, parentId);
                continue;
            }

            // A different item replaced the slot's previous occupant: zero the old one first.
            if (e is InventoryItemChangedArgs changed && changed.OldItemState.ItemId != e.Item.ItemId && changed.OldItemState.ItemId != 0)
                RecordCleared(ownerId, ownerKind, container, slot, changed.OldItemState.ItemId, parentId);

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

    private void RecordCleared(ulong ownerId, OwnerKind ownerKind, Container container, short slot, uint itemId, ulong parentId)
    {
        var key = new ResourceKey
        {
            OwnerId   = ownerId,
            OwnerKind = ownerKind,
            Container = container,
            ItemId    = itemId,
            Slot      = slot,
        };
        if (_service.Store.Get(key) is not { Quantity: > 0 }) return;

        _service.RecordObservation(new ResourceObservation
        {
            Key           = key,
            Quantity      = 0,
            Flags         = ResourceFlags.None,
            UpdatedAt     = DateTime.UtcNow,
            ParentOwnerId = parentId,
        });
    }

    /// <summary>
    /// Resolve owner_id for a Dalamud GameInventoryType. Player containers → PlayerContentId.
    /// Retainer containers → active retainer id. FC containers → 0 (FC owner resolution
    /// is deferred; the FC chest reconcile-scan in ReconcileScanner Task 26 handles
    /// FC bag identity properly).
    /// </summary>
    private ulong ResolveOwnerId(GameInventoryType type)
    {
        var typeInt = (int)type;
        // Retainer page / equipped / gil / crystals / market (10000-12999)
        if (typeInt >= 10000 && typeInt < 13000)
            return _gameState.GetActiveRetainerId();

        // FC pages / crystals / gil (20000-22999) — FC owner id resolution deferred
        if (typeInt >= 20000 && typeInt < 23000)
            return 0;

        // Otherwise — player
        return _gameState.PlayerContentId;
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
