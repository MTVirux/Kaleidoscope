using Kaleidoscope.Models.Inventory;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Services.Resources.Adapters;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class ResourceToLegacyAdapterTests
{
    private static Resource Item(ulong owner, OwnerKind kind, Container container, uint itemId, short slot, long qty, ResourceFlags flags = ResourceFlags.None)
        => new()
        {
            Key = new ResourceKey { OwnerId = owner, OwnerKind = kind, Container = container, ItemId = itemId, Slot = slot },
            Quantity = qty,
            Flags = flags,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public void Adapt_PlayerWithItemsAndGil_ProducesOnePlayerEntry()
    {
        var store = new ResourceStore();
        store.ApplyWithAggregate(Item(1001, OwnerKind.Player, Container.Inventory1, 5057, 0, 42));
        store.ApplyWithAggregate(Item(1001, OwnerKind.Player, Container.SpecialPlayer, ResourceCatalog.GilItemId, -1, 12345));

        var entries = ResourceToLegacyAdapter.Adapt(store);

        var player = Assert.Single(entries, e => e.SourceType == InventorySourceType.Player && e.CharacterId == 1001);
        Assert.Equal(12345, player.Gil);
        var item = Assert.Single(player.Items, i => i.ItemId == 5057);
        Assert.Equal(42, item.Quantity);
        Assert.Equal(0, item.Slot);
        Assert.Equal((uint)Container.Inventory1, item.ContainerType);
    }

    [Fact]
    public void Adapt_RetainerItems_GroupedByRetainerId()
    {
        var store = new ResourceStore();
        store.ApplyWithAggregate(Item(5001, OwnerKind.Retainer, Container.RetainerPage1, 5057, 0, 10));
        store.ApplyWithAggregate(Item(5002, OwnerKind.Retainer, Container.RetainerPage1, 5057, 0, 5));

        var entries = ResourceToLegacyAdapter.Adapt(store);

        Assert.Contains(entries, e => e.SourceType == InventorySourceType.Retainer && e.RetainerId == 5001);
        Assert.Contains(entries, e => e.SourceType == InventorySourceType.Retainer && e.RetainerId == 5002);
    }

    [Fact]
    public void Adapt_HQAndCollectableFlags_RoundTrip()
    {
        var store = new ResourceStore();
        store.ApplyWithAggregate(Item(1001, OwnerKind.Player, Container.Inventory1, 5057, 0, 1, ResourceFlags.HQ));
        store.ApplyWithAggregate(Item(1001, OwnerKind.Player, Container.Inventory1, 5058, 1, 1, ResourceFlags.Collectable));

        var entries = ResourceToLegacyAdapter.Adapt(store);
        var player = Assert.Single(entries);
        Assert.True(player.Items.Single(i => i.ItemId == 5057).IsHq);
        Assert.True(player.Items.Single(i => i.ItemId == 5058).IsCollectable);
    }
}
