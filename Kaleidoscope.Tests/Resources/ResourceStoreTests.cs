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
}
