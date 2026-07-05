using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Services.Resources.Adapters;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class ItemCountProjectionTests
{
    private const ulong CharId = 1001;
    private const ulong RetainerA = 5001;
    private const ulong RetainerB = 5002;

    private static ResourceKey K(
        uint itemId,
        ulong ownerId = CharId,
        OwnerKind kind = OwnerKind.Player,
        Container container = Container.Inventory1,
        short slot = 0) => new()
    {
        OwnerId   = ownerId,
        OwnerKind = kind,
        Container = container,
        ItemId    = itemId,
        Slot      = slot,
    };

    private static void Apply(ResourceStore store, ResourceKey key, long qty)
        => store.ApplyWithAggregate(new Resource { Key = key, Quantity = qty, UpdatedAt = DateTime.UtcNow });

    // ── AffectsItemCounts ──────────────────────────────────────────────────────

    [Fact]
    public void AffectsItemCounts_PlayerBagItem_True()
        => Assert.True(ItemCountProjection.AffectsItemCounts(K(5057)));

    [Fact]
    public void AffectsItemCounts_RetainerPageItem_True()
        => Assert.True(ItemCountProjection.AffectsItemCounts(
            K(5057, RetainerA, OwnerKind.Retainer, Container.RetainerPage3)));

    [Fact]
    public void AffectsItemCounts_SyntheticGil_False()
        => Assert.False(ItemCountProjection.AffectsItemCounts(
            K(ResourceCatalog.GilItemId, container: Container.SpecialPlayer)));

    [Fact]
    public void AffectsItemCounts_AggregateContainer_False()
        => Assert.False(ItemCountProjection.AffectsItemCounts(
            K(5057, container: Container.PlayerAggregate)));

    [Fact]
    public void AffectsItemCounts_FreeCompanyOwner_False()
        => Assert.False(ItemCountProjection.AffectsItemCounts(
            K(5057, 9001, OwnerKind.FreeCompany, Container.FreeCompanyPage1)));

    [Fact]
    public void AffectsItemCounts_ZeroItemId_False()
        => Assert.False(ItemCountProjection.AffectsItemCounts(K(0)));

    // ── BuildSamples ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildSamples_SumsPlayerQuantityAcrossContainersAndSlots()
    {
        var store = new ResourceStore();
        Apply(store, K(5057, slot: 0), 10);
        Apply(store, K(5057, slot: 1), 15);
        Apply(store, K(5057, container: Container.SaddleBag1), 5);

        var samples = ItemCountProjection.BuildSamples(store, CharId, Array.Empty<ulong>(), new uint[] { 5057 });

        Assert.Contains(("Item_5057", CharId, 30L), samples);
        Assert.Contains(("ItemRetainer_5057", CharId, 0L), samples);
    }

    [Fact]
    public void BuildSamples_SumsRetainersSeparatelyFromPlayer()
    {
        var store = new ResourceStore();
        Apply(store, K(5057), 10);
        Apply(store, K(5057, RetainerA, OwnerKind.Retainer, Container.RetainerPage1), 7);
        Apply(store, K(5057, RetainerB, OwnerKind.Retainer, Container.RetainerPage2), 3);

        var samples = ItemCountProjection.BuildSamples(
            store, CharId, new[] { RetainerA, RetainerB }, new uint[] { 5057 });

        Assert.Contains(("Item_5057", CharId, 10L), samples);
        Assert.Contains(("ItemRetainer_5057", CharId, 10L), samples);
    }

    [Fact]
    public void BuildSamples_ExcludesRetainersNotInGivenSet()
    {
        var store = new ResourceStore();
        Apply(store, K(5057, RetainerA, OwnerKind.Retainer, Container.RetainerPage1), 7);
        Apply(store, K(5057, RetainerB, OwnerKind.Retainer, Container.RetainerPage1), 3);

        var samples = ItemCountProjection.BuildSamples(store, CharId, new[] { RetainerA }, new uint[] { 5057 });

        Assert.Contains(("ItemRetainer_5057", CharId, 7L), samples);
    }

    [Fact]
    public void BuildSamples_UnknownItem_EmitsZeroes()
    {
        var store = new ResourceStore();

        var samples = ItemCountProjection.BuildSamples(store, CharId, Array.Empty<ulong>(), new uint[] { 4444 });

        Assert.Contains(("Item_4444", CharId, 0L), samples);
        Assert.Contains(("ItemRetainer_4444", CharId, 0L), samples);
    }

    [Fact]
    public void BuildSamples_MultipleItems_EmitsPairPerItem()
    {
        var store = new ResourceStore();
        Apply(store, K(5057), 1);
        Apply(store, K(4444, slot: 1), 2);

        var samples = ItemCountProjection.BuildSamples(store, CharId, Array.Empty<ulong>(), new uint[] { 5057, 4444 });

        Assert.Equal(4, samples.Count);
    }
}
