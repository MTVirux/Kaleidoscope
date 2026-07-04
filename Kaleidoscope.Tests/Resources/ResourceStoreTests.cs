using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class ResourceStoreTests
{
    private static ResourceKey K(uint itemId = 5057, short slot = 0) => new()
    {
        OwnerId   = 1001,
        OwnerKind = OwnerKind.Player,
        Container = Container.Inventory1,
        ItemId    = itemId,
        Slot      = slot,
    };

    private static Resource R(ResourceKey k, long qty) => new()
    {
        Key       = k,
        Quantity  = qty,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void Apply_NewKey_StoresAndIncrementsVersion()
    {
        var store = new ResourceStore();
        var v0 = store.Version;

        var changed = store.Apply(R(K(), 10), out var oldQty);

        Assert.True(changed);
        Assert.Equal(0, oldQty);
        Assert.Equal(v0 + 1, store.Version);
        Assert.Equal(10, store.Get(K())!.Value.Quantity);
    }

    [Fact]
    public void Apply_SameQuantityAndFlags_ReturnsFalseAndDoesNotBumpVersion()
    {
        var store = new ResourceStore();
        store.Apply(R(K(), 10), out _);
        var v1 = store.Version;

        var changed = store.Apply(R(K(), 10), out var oldQty);

        Assert.False(changed);
        Assert.Equal(10, oldQty);
        Assert.Equal(v1, store.Version);
    }

    [Fact]
    public void Apply_DifferentQuantity_ReturnsTrueAndBumpsVersion()
    {
        var store = new ResourceStore();
        store.Apply(R(K(), 10), out _);
        var v1 = store.Version;

        var changed = store.Apply(R(K(), 25), out var oldQty);

        Assert.True(changed);
        Assert.Equal(10, oldQty);
        Assert.Equal(v1 + 1, store.Version);
        Assert.Equal(25, store.Get(K())!.Value.Quantity);
    }

    [Fact]
    public void Get_UnknownKey_ReturnsNull()
    {
        var store = new ResourceStore();
        Assert.Null(store.Get(K()));
    }

    [Fact]
    public void Aggregates_SumAcrossOwners_ReflectsAllApplications()
    {
        var store = new ResourceStore();

        store.ApplyWithAggregate(new Resource { Key = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 0 }, Quantity = 10, UpdatedAt = DateTime.UtcNow });
        store.ApplyWithAggregate(new Resource { Key = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 1 }, Quantity = 5,  UpdatedAt = DateTime.UtcNow });
        store.ApplyWithAggregate(new Resource { Key = new ResourceKey { OwnerId = 5001, OwnerKind = OwnerKind.Retainer, Container = Container.RetainerPage1, ItemId = 5057, Slot = 0 }, Quantity = 7, UpdatedAt = DateTime.UtcNow });

        Assert.Equal(22, store.GetAggregate(5057));
        Assert.Equal(15, store.GetAggregate(5057, OwnerKind.Player));
        Assert.Equal(7,  store.GetAggregate(5057, OwnerKind.Retainer));
    }

    [Fact]
    public void Aggregates_QuantityChange_DeltaApplied()
    {
        var store = new ResourceStore();
        var k = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 0 };
        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 10, UpdatedAt = DateTime.UtcNow });
        Assert.Equal(10, store.GetAggregate(5057));

        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 25, UpdatedAt = DateTime.UtcNow });
        Assert.Equal(25, store.GetAggregate(5057));

        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 0,  UpdatedAt = DateTime.UtcNow });
        Assert.Equal(0,  store.GetAggregate(5057));
    }

    [Fact]
    public void GetSumForOwner_IndexTracksOwnerScopedTotals()
    {
        var store = new ResourceStore();
        store.ApplyWithAggregate(new Resource { Key = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 0 }, Quantity = 10, UpdatedAt = DateTime.UtcNow });
        store.ApplyWithAggregate(new Resource { Key = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 1 }, Quantity = 5,  UpdatedAt = DateTime.UtcNow });
        store.ApplyWithAggregate(new Resource { Key = new ResourceKey { OwnerId = 2002, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 0 }, Quantity = 99, UpdatedAt = DateTime.UtcNow });
        store.ApplyWithAggregate(new Resource { Key = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Retainer, Container = Container.RetainerPage1, ItemId = 5057, Slot = 0 }, Quantity = 7, UpdatedAt = DateTime.UtcNow });

        Assert.Equal(15, store.GetSumForOwner(1001, OwnerKind.Player, 5057));
        Assert.Equal(99, store.GetSumForOwner(2002, OwnerKind.Player, 5057));
        Assert.Equal(7,  store.GetSumForOwner(1001, OwnerKind.Retainer, 5057));
        Assert.Equal(0,  store.GetSumForOwner(9999, OwnerKind.Player, 5057));
    }

    [Fact]
    public void GetSumForOwner_QuantityChange_DeltaApplied()
    {
        var store = new ResourceStore();
        var k = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 0 };
        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 10, UpdatedAt = DateTime.UtcNow });
        Assert.Equal(10, store.GetSumForOwner(1001, OwnerKind.Player, 5057));

        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 3, UpdatedAt = DateTime.UtcNow });
        Assert.Equal(3, store.GetSumForOwner(1001, OwnerKind.Player, 5057));

        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 0, UpdatedAt = DateTime.UtcNow });
        Assert.Equal(0, store.GetSumForOwner(1001, OwnerKind.Player, 5057));
    }

    [Fact]
    public void GetItemIdForSlot_ReturnsCurrentOccupant()
    {
        var store = new ResourceStore();
        var k = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 3 };
        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 12, UpdatedAt = DateTime.UtcNow });

        Assert.Equal(5057u, store.GetItemIdForSlot(1001, OwnerKind.Player, Container.Inventory1, 3));
        Assert.Null(store.GetItemIdForSlot(1001, OwnerKind.Player, Container.Inventory1, 4));
        Assert.Null(store.GetItemIdForSlot(1001, OwnerKind.Retainer, Container.Inventory1, 3));
    }

    [Fact]
    public void Clear_ResetsStateAggregatesAndIndexes()
    {
        var store = new ResourceStore();
        var k = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 0 };
        store.ApplyWithAggregate(new Resource { Key = k, Quantity = 10, UpdatedAt = DateTime.UtcNow });

        store.Clear();

        Assert.Null(store.Get(k));
        Assert.Equal(0, store.GetAggregate(5057));
        Assert.Equal(0, store.GetSumForOwner(1001, OwnerKind.Player, 5057));
        Assert.Null(store.GetItemIdForSlot(1001, OwnerKind.Player, Container.Inventory1, 0));
    }
}
