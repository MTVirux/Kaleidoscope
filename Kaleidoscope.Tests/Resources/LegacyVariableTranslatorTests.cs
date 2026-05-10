using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Services.Resources.Adapters;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class LegacyVariableTranslatorTests
{
    [Fact]
    public void Translate_Gil_ReturnsSpecialPlayerWithGilId()
    {
        var q = LegacyVariableTranslator.Translate("Gil", characterId: 0xABCD);
        Assert.NotNull(q);
        Assert.Equal(OwnerKind.Player, q!.Value.OwnerKind);
        Assert.Equal(0xABCDul, q.Value.OwnerId);
        Assert.Equal(Container.SpecialPlayer, q.Value.Container);
        Assert.Equal(ResourceCatalog.GilItemId, q.Value.ItemId);
    }

    [Fact]
    public void Translate_ItemPlayer_ReturnsPlayerAggregate()
    {
        var q = LegacyVariableTranslator.Translate("Item_5057", characterId: 0xABCD);
        Assert.NotNull(q);
        Assert.Equal(Container.PlayerAggregate, q!.Value.Container);
        Assert.Equal(5057u, q.Value.ItemId);
    }

    [Fact]
    public void Translate_Unknown_ReturnsNull()
    {
        Assert.Null(LegacyVariableTranslator.Translate("Garbage", 0xABCD));
    }
}
