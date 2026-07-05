using FFXIVClientStructs.FFXIV.Client.Game;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Shared mapping from a raw <see cref="InventoryItem"/> slot to a <see cref="ResourceObservation"/>.
/// Single home for the HQ/Collectable flag demux and spiritbond-vs-collectability split so the direct
/// InventoryManager scanners (ReconcileScanner, TradeReconcileCapture) don't each
/// copy it. The caller supplies the resource key (so empty-slot reconciles can carry the previous
/// item id) and the parent owner id.
/// </summary>
internal static class InventorySlotMapper
{
    public static unsafe ResourceObservation FromInventorySlot(InventoryItem* slot, ResourceKey key, ulong parentOwnerId)
    {
        var isHq  = (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
        var isCol = (slot->Flags & InventoryItem.ItemFlags.Collectable) != 0;

        var flags = ResourceFlags.None;
        if (isHq)  flags |= ResourceFlags.HQ;
        if (isCol) flags |= ResourceFlags.Collectable;

        return new ResourceObservation
        {
            Key            = key,
            Quantity       = slot->Quantity,
            Flags          = flags,
            Spiritbond     = (ushort)(isCol ? 0 : slot->SpiritbondOrCollectability),
            Collectability = (ushort)(isCol ? slot->SpiritbondOrCollectability : 0),
            Condition      = slot->Condition,
            GlamourId      = slot->GlamourId,
            UpdatedAt      = DateTime.UtcNow,
            ParentOwnerId  = parentOwnerId,
        };
    }
}
