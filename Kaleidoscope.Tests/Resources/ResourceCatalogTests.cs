using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class ResourceCatalogTests
{
    [Fact]
    public void SyntheticIds_AreAboveGameItemIdRange()
    {
        const uint gameItemMax = 100_000;

        Assert.True(ResourceCatalog.GilItemId         > gameItemMax);
        Assert.True(ResourceCatalog.MGPItemId         > gameItemMax);
        Assert.True(ResourceCatalog.WolfMarksItemId   > gameItemMax);
        Assert.True(ResourceCatalog.AlliedSealsItemId > gameItemMax);
        Assert.True(ResourceCatalog.FCCreditsItemId   > gameItemMax);
    }

    [Fact]
    public void SyntheticIds_AreUniqueAcrossAllSpecials()
    {
        var ids = new[]
        {
            ResourceCatalog.GilItemId,
            ResourceCatalog.MGPItemId,
            ResourceCatalog.WolfMarksItemId,
            ResourceCatalog.AlliedSealsItemId,
            ResourceCatalog.FCCreditsItemId,
        };
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Theory]
    [InlineData(0,     Container.Inventory1)]
    [InlineData(1,     Container.Inventory2)]
    [InlineData(2,     Container.Inventory3)]
    [InlineData(3,     Container.Inventory4)]
    [InlineData(1000,  Container.EquippedItems)]
    [InlineData(2004,  Container.KeyItems)]
    [InlineData(3500,  Container.ArmoryMainHand)]
    [InlineData(10000, Container.RetainerPage1)]
    [InlineData(12002, Container.RetainerMarket)]
    [InlineData(20000, Container.FreeCompanyPage1)]
    public void TryMapContainer_KnownGameInventoryType_ReturnsExpectedContainer(int gameType, Container expected)
    {
        Assert.True(ResourceCatalog.TryMapContainer(gameType, out var c));
        Assert.Equal(expected, c);
    }

    [Fact]
    public void TryMapContainer_UnknownValue_ReturnsFalse()
    {
        Assert.False(ResourceCatalog.TryMapContainer(99999, out _));
    }

    [Fact]
    public void ParseLegacyVariableName_ItemPlayer_MapsToPlayerAggregate()
    {
        var r = ResourceCatalog.ParseLegacyVariableName("Item_5057", characterId: 0xABCD);
        Assert.NotNull(r);
        Assert.Equal(OwnerKind.Player, r!.Value.OwnerKind);
        Assert.Equal(0xABCDul, r.Value.OwnerId);
        Assert.Equal(Container.PlayerAggregate, r.Value.Container);
        Assert.Equal(5057u, r.Value.ItemId);
    }

    [Fact]
    public void ParseLegacyVariableName_ItemRetainerAggregate_MapsToRetainerAggregateOnPlayer()
    {
        var r = ResourceCatalog.ParseLegacyVariableName("ItemRetainer_5057", characterId: 0xABCD);
        Assert.NotNull(r);
        Assert.Equal(OwnerKind.Player, r!.Value.OwnerKind);
        Assert.Equal(0xABCDul, r.Value.OwnerId);
        Assert.Equal(Container.RetainerAggregate, r.Value.Container);
        Assert.Equal(5057u, r.Value.ItemId);
    }

    [Fact]
    public void ParseLegacyVariableName_PerRetainerSeries_ReassignsOwnerToRetainer()
    {
        var r = ResourceCatalog.ParseLegacyVariableName("ItemRetainerX_99887766_5057", characterId: 0xABCD);
        Assert.NotNull(r);
        Assert.Equal(OwnerKind.Retainer, r!.Value.OwnerKind);
        Assert.Equal(99887766ul, r.Value.OwnerId);
        Assert.Equal(Container.RetainerPage1, r.Value.Container);
        Assert.Equal(5057u, r.Value.ItemId);
    }

    [Fact]
    public void ParseLegacyVariableName_Gil_MapsToSpecialPlayerWithGilSyntheticId()
    {
        var r = ResourceCatalog.ParseLegacyVariableName("Gil", characterId: 0xABCD);
        Assert.NotNull(r);
        Assert.Equal(OwnerKind.Player, r!.Value.OwnerKind);
        Assert.Equal(Container.SpecialPlayer, r.Value.Container);
        Assert.Equal(ResourceCatalog.GilItemId, r.Value.ItemId);
    }

    [Fact]
    public void ParseLegacyVariableName_TomestonePoetics_MapsToCurrencyContainer()
    {
        var r = ResourceCatalog.ParseLegacyVariableName("TomestonePoetics", characterId: 0xABCD);
        Assert.NotNull(r);
        Assert.Equal(OwnerKind.Player, r!.Value.OwnerKind);
        Assert.Equal(Container.Currency, r.Value.Container);
        Assert.Equal(28u, r.Value.ItemId);
    }

    [Fact]
    public void ParseLegacyVariableName_Unknown_ReturnsNull()
    {
        Assert.Null(ResourceCatalog.ParseLegacyVariableName("",                 0xABCD));
        Assert.Null(ResourceCatalog.ParseLegacyVariableName("RandomGarbage",    0xABCD));
        Assert.Null(ResourceCatalog.ParseLegacyVariableName("Item_",            0xABCD));
        Assert.Null(ResourceCatalog.ParseLegacyVariableName("Item_NotANumber", 0xABCD));
        Assert.Null(ResourceCatalog.ParseLegacyVariableName("ItemRetainerX_5_", 0xABCD));
        Assert.Null(ResourceCatalog.ParseLegacyVariableName("ItemRetainerX__5", 0xABCD));
    }
}
