namespace Kaleidoscope.Models.Resources;

/// <summary>
/// Where a Resource lives. Real-container values mirror FFXIVClientStructs
/// InventoryType / Dalamud GameInventoryType exactly so legacy
/// inventory_items.container_type values round-trip into our resources.container column
/// without translation. Synthetic values (40000+) cover hidden containers and game-
/// memory-only counters that have no corresponding GameInventoryType.
/// </summary>
public enum Container : int
{
    // Player main inventory
    Inventory1 = 0,
    Inventory2 = 1,
    Inventory3 = 2,
    Inventory4 = 3,

    // Player equipped / currency / crystals / key items
    EquippedItems = 1000,
    Currency      = 2000,
    Crystals      = 2001,
    KeyItems      = 2004,

    // Player armory slots
    ArmoryOffHand     = 3200,
    ArmoryHead        = 3201,
    ArmoryBody        = 3202,
    ArmoryHands       = 3203,
    ArmoryWaist       = 3204,
    ArmoryLegs        = 3205,
    ArmoryFeets       = 3206,
    ArmoryEar         = 3207,
    ArmoryNeck        = 3208,
    ArmoryWrist       = 3209,
    ArmoryRings       = 3300,
    ArmorySoulCrystal = 3400,
    ArmoryMainHand    = 3500,

    // Saddlebags
    SaddleBag1        = 4000,
    SaddleBag2        = 4001,
    PremiumSaddleBag1 = 4100,
    PremiumSaddleBag2 = 4101,

    // Retainer
    RetainerPage1         = 10000,
    RetainerPage2         = 10001,
    RetainerPage3         = 10002,
    RetainerPage4         = 10003,
    RetainerPage5         = 10004,
    RetainerPage6         = 10005,
    RetainerPage7         = 10006,
    RetainerEquippedItems = 11000,
    RetainerGil           = 12000,
    RetainerCrystals      = 12001,
    RetainerMarket        = 12002,

    // Free Company
    FreeCompanyPage1    = 20000,
    FreeCompanyPage2    = 20001,
    FreeCompanyPage3    = 20002,
    FreeCompanyPage4    = 20003,
    FreeCompanyPage5    = 20004,
    FreeCompanyGil      = 22000,
    FreeCompanyCrystals = 22001,

    // Hidden — read directly, no GameInventoryType
    GlamourChest = 40000,
    Armoire      = 40001,

    // Synthetic specials — no real container
    SpecialPlayer      = 90000,
    SpecialFreeCompany = 90001,
    PlayerAggregate    = 90100, // Migration only — preserves Item_{id} time-series
    RetainerAggregate  = 90101, // Migration only — preserves ItemRetainer_{id} time-series
}
