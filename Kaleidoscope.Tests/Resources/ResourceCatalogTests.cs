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
}
