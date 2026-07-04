using System.Collections.Generic;
using Kaleidoscope.Models.Inventory;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources.Adapters;

/// <summary>
/// Translates a ResourceStore snapshot into the List&lt;InventoryCacheEntry&gt; shape that
/// legacy consumers expect. Lets every read consumer (DataTool, PriceTrackingService,
/// CacheSizeTool, etc.) keep its existing call site while the underlying storage moves
/// to the unified resources tables.
/// </summary>
public static class ResourceToLegacyAdapter
{
    /// <summary>
    /// Project a snapshot into one InventoryCacheEntry per (OwnerId, OwnerKind) pair.
    /// Items go into the entry's Items list as InventoryItemSnapshot. Gil is read from
    /// the synthetic SpecialPlayer/GilItemId resource.
    /// </summary>
    public static List<InventoryCacheEntry> Adapt(ResourceStore store)
    {
        var byOwner = new Dictionary<(ulong OwnerId, OwnerKind Kind), InventoryCacheEntry>();

        foreach (var r in store.Snapshot())
        {
            var key = (r.Key.OwnerId, r.Key.OwnerKind);
            if (!byOwner.TryGetValue(key, out var entry))
            {
                entry = new InventoryCacheEntry
                {
                    CharacterId = r.Key.OwnerKind == OwnerKind.Player ? r.Key.OwnerId : 0,
                    RetainerId = r.Key.OwnerKind == OwnerKind.Retainer ? r.Key.OwnerId : 0,
                    SourceType = r.Key.OwnerKind == OwnerKind.Player ? InventorySourceType.Player : InventorySourceType.Retainer,
                    UpdatedAt = r.UpdatedAt,
                };
                byOwner[key] = entry;
            }

            // Synthetic gil row → set Gil field, not Items.
            if (r.Key.Container == Container.SpecialPlayer && r.Key.ItemId == ResourceCatalog.GilItemId)
            {
                entry.Gil = r.Quantity;
                continue;
            }

            // Skip other synthetic rows (MGP, WolfMarks, aggregates, etc.) — the legacy schema didn't
            // carry them on InventoryCacheEntry. Synthetic specials start at Container.SpecialPlayer.
            if ((int)r.Key.Container >= (int)Container.SpecialPlayer) continue;
            // Skip Glamour/Armoire — not part of the legacy entry shape (and not captured by the new pipeline yet).
            if (r.Key.Container is Container.GlamourChest or Container.Armoire) continue;

            entry.Items.Add(new InventoryItemSnapshot
            {
                ItemId = r.Key.ItemId,
                Quantity = (int)r.Quantity,
                IsHq = (r.Flags & ResourceFlags.HQ) != 0,
                IsCollectable = (r.Flags & ResourceFlags.Collectable) != 0,
                Slot = r.Key.Slot,
                ContainerType = (uint)r.Key.Container,
                SpiritbondOrCollectability = (r.Flags & ResourceFlags.Collectable) != 0 ? r.Collectability : r.Spiritbond,
                Condition = r.Condition,
                GlamourId = r.GlamourId,
            });
        }

        return new List<InventoryCacheEntry>(byOwner.Values);
    }
}
