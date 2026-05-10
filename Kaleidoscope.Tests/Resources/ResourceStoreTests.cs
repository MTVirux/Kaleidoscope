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
    public void TimeSeriesCache_RecordsRecentPoints()
    {
        var store = new ResourceStore();
        var k = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.SpecialPlayer, ItemId = ResourceCatalog.GilItemId, Slot = -1 };

        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        store.AppendHistory(k, t0,                  100, 100, SourceKind.Unknown);
        store.AppendHistory(k, t0.AddSeconds(10),   150,  50, SourceKind.DutyReward);
        store.AppendHistory(k, t0.AddSeconds(20),   140, -10, SourceKind.Vendor);

        var pts = store.GetRecentHistory(1001, ResourceCatalog.GilItemId);

        Assert.Equal(3, pts.Count);
        Assert.Equal(100, pts[0].Quantity);
        Assert.Equal(SourceKind.DutyReward, pts[1].Source);
        Assert.Equal(-10, pts[2].ChangeAmount);
    }

    [Fact]
    public void TimeSeriesCache_BoundedAtCapacity()
    {
        var store = new ResourceStore();
        store.SetHistoryCapacityForTests(3);
        var k = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.SpecialPlayer, ItemId = ResourceCatalog.GilItemId, Slot = -1 };
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 5; i++)
            store.AppendHistory(k, t0.AddSeconds(i), i * 100, 100, SourceKind.Unknown);

        var pts = store.GetRecentHistory(1001, ResourceCatalog.GilItemId);
        Assert.Equal(3, pts.Count);
        Assert.Equal(200, pts[0].Quantity);   // 0,100 evicted
        Assert.Equal(400, pts[2].Quantity);
    }
}
