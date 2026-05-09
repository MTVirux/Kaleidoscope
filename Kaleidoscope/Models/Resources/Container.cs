namespace Kaleidoscope.Models.Resources;

/// <summary>
/// Where a Resource lives. Real values mirror Dalamud's GameInventoryType for documentation;
/// synthetic values (40000+) cover hidden containers and game-memory-only counters that have
/// no corresponding GameInventoryType.
/// </summary>
public enum Container : int
{
    // Player main inventory (0-999)
    Inventory1 = 0,
    Inventory2 = 1,
    Inventory3 = 2,
    Inventory4 = 3,

    // Player equipped / currency / crystals / key items (1000-2999)
    EquippedItems = 1000,
    Crystals      = 2000,
    Currency      = 2001,
    KeyItems      = 2002,

    // Player armory slots (3000-3999)
    ArmoryMainHand    = 3000,
    ArmoryOffHand     = 3001,
    ArmoryHead        = 3002,
    ArmoryBody        = 3003,
    ArmoryHands       = 3004,
    ArmoryLegs        = 3005,
    ArmoryFeets       = 3006,
    ArmoryEar         = 3007,
    ArmoryNeck        = 3008,
    ArmoryWrist       = 3009,
    ArmoryRings       = 3010,
    ArmorySoulCrystal = 3011,

    // Retainer (10000-11999)
    RetainerPage1         = 10000,
    RetainerPage2         = 10001,
    RetainerPage3         = 10002,
    RetainerPage4         = 10003,
    RetainerPage5         = 10004,
    RetainerPage6         = 10005,
    RetainerPage7         = 10006,
    RetainerEquippedItems = 11000,
    RetainerCrystals      = 11001,
    RetainerMarket        = 11002,

    // Saddlebags + cosmopouch (20000-20999)
    SaddleBag1        = 20000,
    SaddleBag2        = 20001,
    PremiumSaddleBag1 = 20002,
    PremiumSaddleBag2 = 20003,
    Cosmopouch1       = 20100,
    Cosmopouch2       = 20101,

    // Free Company (30000-30999)
    FreeCompanyPage1    = 30000,
    FreeCompanyPage2    = 30001,
    FreeCompanyPage3    = 30002,
    FreeCompanyPage4    = 30003,
    FreeCompanyPage5    = 30004,
    FreeCompanyCrystals = 30100,
    FreeCompanyGil      = 30101,

    // Hidden — read directly, no GameInventoryType (40000-49999)
    GlamourChest = 40000,
    Armoire      = 40001,

    // Synthetic specials — no real container (90000-99999)
    SpecialPlayer      = 90000,
    SpecialFreeCompany = 90001,
    PlayerAggregate    = 90100, // Migration only — preserves Item_{id} time-series
    RetainerAggregate  = 90101, // Migration only — preserves ItemRetainer_{id} time-series
}
