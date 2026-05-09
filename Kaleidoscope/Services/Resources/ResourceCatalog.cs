using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// Static catalog of resource identity helpers — synthetic item IDs for game-memory-only
/// counters, GameInventoryType ↔ Container mapping, and legacy variable-name parsing
/// for the schema 1→2 migration.
/// </summary>
public static class ResourceCatalog
{
    // Synthetic item IDs for values that don't have a real game item ID.
    // Chosen well above any real item ID (game's range is < 100,000) so no collision possible.
    public const uint GilItemId         = 1_000_001;
    public const uint MGPItemId         = 1_000_002;
    public const uint WolfMarksItemId   = 1_000_003;
    public const uint AlliedSealsItemId = 1_000_004;
    public const uint FCCreditsItemId   = 1_000_005;

    /// <summary>
    /// Map a Dalamud GameInventoryType numeric value to our Container enum.
    /// Caller passes the int value (cast from GameInventoryType) so this class
    /// stays free of Dalamud references and remains testable from xUnit.
    /// Numeric values verified against FFXIVClientStructs/FFXIV/Client/Game/InventoryType.cs.
    /// </summary>
    public static bool TryMapContainer(int gameInventoryType, out Container container)
    {
        container = gameInventoryType switch
        {
            0     => Container.Inventory1,
            1     => Container.Inventory2,
            2     => Container.Inventory3,
            3     => Container.Inventory4,
            1000  => Container.EquippedItems,
            2000  => Container.Currency,
            2001  => Container.Crystals,
            2004  => Container.KeyItems,
            3200  => Container.ArmoryOffHand,
            3201  => Container.ArmoryHead,
            3202  => Container.ArmoryBody,
            3203  => Container.ArmoryHands,
            3205  => Container.ArmoryLegs,
            3206  => Container.ArmoryFeets,
            3207  => Container.ArmoryEar,
            3208  => Container.ArmoryNeck,
            3209  => Container.ArmoryWrist,
            3300  => Container.ArmoryRings,
            3400  => Container.ArmorySoulCrystal,
            3500  => Container.ArmoryMainHand,
            4000  => Container.SaddleBag1,
            4001  => Container.SaddleBag2,
            4100  => Container.PremiumSaddleBag1,
            4101  => Container.PremiumSaddleBag2,
            5000  => Container.Cosmopouch1,
            5001  => Container.Cosmopouch2,
            10000 => Container.RetainerPage1,
            10001 => Container.RetainerPage2,
            10002 => Container.RetainerPage3,
            10003 => Container.RetainerPage4,
            10004 => Container.RetainerPage5,
            10005 => Container.RetainerPage6,
            10006 => Container.RetainerPage7,
            11000 => Container.RetainerEquippedItems,
            12001 => Container.RetainerCrystals,
            12002 => Container.RetainerMarket,
            20000 => Container.FreeCompanyPage1,
            20001 => Container.FreeCompanyPage2,
            20002 => Container.FreeCompanyPage3,
            20003 => Container.FreeCompanyPage4,
            20004 => Container.FreeCompanyPage5,
            22000 => Container.FreeCompanyGil,
            22001 => Container.FreeCompanyCrystals,
            _ => (Container)(-1),
        };
        return (int)container != -1;
    }
}
