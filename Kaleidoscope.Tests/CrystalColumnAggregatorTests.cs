using Kaleidoscope.Gui.MainWindow.Tools.Data;
using Kaleidoscope.Models.Inventory;
using Xunit;

namespace Kaleidoscope.Tests;

public class CrystalColumnAggregatorTests
{
    private static InventoryCacheEntry Entry(ulong charId, InventorySourceType source, params (uint ItemId, int Quantity)[] items)
    {
        var entry = new InventoryCacheEntry
        {
            CharacterId = charId,
            SourceType = source,
        };
        foreach (var (itemId, quantity) in items)
            entry.Items.Add(new InventoryItemSnapshot { ItemId = itemId, Quantity = quantity });
        return entry;
    }

    [Fact]
    public void SumPerCharacter_SumsPlayerAndRetainerIntoOwningCharacter()
    {
        var fireShard = 2u;
        var inventories = new List<InventoryCacheEntry>
        {
            Entry(100, InventorySourceType.Player, (fireShard, 50), (999u, 10)),
            Entry(100, InventorySourceType.Retainer, (fireShard, 25)),
            Entry(200, InventorySourceType.Player, (fireShard, 7)),
        };

        var sums = CrystalColumnAggregator.SumPerCharacter(inventories, new HashSet<uint> { fireShard });

        Assert.Equal(75, sums[100]);
        Assert.Equal(7, sums[200]);
        Assert.Equal(2, sums.Count);
    }

    [Fact]
    public void SumPerCharacter_IgnoresItemsOutsideTheSet()
    {
        var inventories = new List<InventoryCacheEntry>
        {
            Entry(100, InventorySourceType.Player, (999u, 10)),
        };

        var sums = CrystalColumnAggregator.SumPerCharacter(inventories, new HashSet<uint> { 2u });

        Assert.Empty(sums);
    }

    [Fact]
    public void SumPerCharacter_KeepsZeroTotalForCharactersWithMatchingRows()
    {
        // The DB aggregate this replaces groups zero-quantity rows into a zero total
        // (e.g. a zeroed-out crystal slot), so the character must still appear with 0.
        var inventories = new List<InventoryCacheEntry>
        {
            Entry(100, InventorySourceType.Player, (2u, 0)),
            Entry(200, InventorySourceType.Player, (999u, 10)),
        };

        var sums = CrystalColumnAggregator.SumPerCharacter(inventories, new HashSet<uint> { 2u });

        Assert.Equal(0, sums[100]);
        Assert.Single(sums);
    }

    [Fact]
    public void SumPerCharacter_SkipsEntriesWithoutAnOwningCharacter()
    {
        // Mirrors the SQL's parent_owner_id != 0 and character_id != 0 filters: entries whose
        // owning character is unknown surface with CharacterId 0 and must not be counted.
        var inventories = new List<InventoryCacheEntry>
        {
            Entry(0, InventorySourceType.Retainer, (2u, 40)),
            Entry(100, InventorySourceType.Player, (2u, 5)),
        };

        var sums = CrystalColumnAggregator.SumPerCharacter(inventories, new HashSet<uint> { 2u });

        Assert.Equal(5, sums[100]);
        Assert.Single(sums);
    }
}
