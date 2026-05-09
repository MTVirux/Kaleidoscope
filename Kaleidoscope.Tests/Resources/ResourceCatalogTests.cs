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
}
