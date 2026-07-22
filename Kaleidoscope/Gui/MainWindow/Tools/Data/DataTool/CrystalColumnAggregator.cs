using Kaleidoscope.Models.Inventory;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// Computes per-character sums for crystal/venture columns from the inventory snapshot the table
/// rebuild already fetched, replacing per-column full-table DB scans. Reproduces
/// KaleidoscopeDbService.GetItemSumPerCharacterIncludingRetainers semantics: retainer entries are
/// keyed by the owning character's id in the snapshot, entries without an owning character
/// (CharacterId 0) are dropped, and characters whose matching rows sum to zero keep a zero total.
/// </summary>
public static partial class CrystalColumnAggregator
{
    public static Dictionary<ulong, long> SumPerCharacter(
        IReadOnlyList<InventoryCacheEntry> inventories, HashSet<uint> itemIds)
    {
        var result = new Dictionary<ulong, long>();
        foreach (var cache in inventories)
        {
            if (cache.CharacterId == 0) continue;

            long sum = 0;
            var matched = false;
            foreach (var item in cache.Items)
            {
                if (itemIds.Contains(item.ItemId))
                {
                    sum += item.Quantity;
                    matched = true;
                }
            }
            if (!matched) continue;

            result.TryGetValue(cache.CharacterId, out var current);
            result[cache.CharacterId] = current + sum;
        }
        return result;
    }
}
