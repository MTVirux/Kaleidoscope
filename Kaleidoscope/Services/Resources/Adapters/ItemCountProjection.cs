using System.Collections.Generic;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources.Adapters;

/// <summary>
/// Pure projection of ResourceStore state onto the legacy item-count series
/// ("Item_{id}" / "ItemRetainer_{id}"). The runtime pipeline stopped writing those
/// series when the unified-resources capture replaced the legacy inventory sampler,
/// so ItemCountHistoryService recomputes them from the store on every tracked change
/// and this class holds the store-math so it stays testable without Dalamud.
/// </summary>
public static class ItemCountProjection
{
    /// <summary>
    /// Whether a committed observation can change an item-count series: a real game item
    /// in a real (non-synthetic) container, owned by a player or one of their retainers.
    /// </summary>
    /// <remarks>
    /// Synthetic item ids (≥ 1,000,000) are game-memory counters projected elsewhere via
    /// TrackedDataTypes; synthetic containers (≥ GlamourChest) include the migration-only
    /// aggregate containers this projection itself writes, so both are excluded.
    /// </remarks>
    public static bool AffectsItemCounts(ResourceKey key)
    {
        if (key.ItemId == 0 || key.ItemId >= 1_000_000) return false;
        if (key.OwnerKind != OwnerKind.Player && key.OwnerKind != OwnerKind.Retainer) return false;
        return (int)key.Container < (int)Container.GlamourChest;
    }

    /// <summary>
    /// Builds the "Item_{id}" (player total) and "ItemRetainer_{id}" (sum across the
    /// character's retainers) samples for each item, both attributed to the character.
    /// </summary>
    public static List<(string Variable, ulong CharacterId, long Value)> BuildSamples(
        ResourceStore store,
        ulong characterId,
        IReadOnlyCollection<ulong> retainerIds,
        IReadOnlyCollection<uint> itemIds)
    {
        var samples = new List<(string, ulong, long)>(itemIds.Count * 2);

        foreach (var itemId in itemIds)
        {
            var playerTotal = store.GetSumForOwner(characterId, OwnerKind.Player, itemId);

            long retainerTotal = 0;
            foreach (var retainerId in retainerIds)
                retainerTotal += store.GetSumForOwner(retainerId, OwnerKind.Retainer, itemId);

            samples.Add(($"Item_{itemId}", characterId, playerTotal));
            samples.Add(($"ItemRetainer_{itemId}", characterId, retainerTotal));
        }

        return samples;
    }
}
