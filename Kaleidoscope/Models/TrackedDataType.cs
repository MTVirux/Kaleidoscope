namespace Kaleidoscope.Models;

/// <summary>
/// Enumeration of all trackable data types in the plugin.
/// These represent currencies, resources, and inventory metrics that can be sampled and tracked over time.
/// </summary>
public enum TrackedDataType
{
    /// <summary>Gil - the primary currency.</summary>
    Gil = 0,

    /// <summary>Allagan Tomestone of Poetics - uncapped endgame currency.</summary>
    TomestonePoetics = 100,
    /// <summary>Allagan Tomestone (Weekly Capped) - current expansion capped tomestone.</summary>
    TomestoneCapped = 101,
    /// <summary>Allagan Tomestone (Uncapped) - current expansion uncapped tomestone.</summary>
    TomestoneUncapped = 102,

    WhiteCraftersScrip = 200,
    PurpleCraftersScrip = 201,
    OrangeCraftersScrip = 202,
    WhiteGatherersScrip = 210,
    PurpleGatherersScrip = 211,
    OrangeGatherersScrip = 212,
    SkybuildersScrip = 220,

    MaelstromSeals = 300,
    TwinAdderSeals = 301,
    ImmortalFlamesSeals = 302,

    /// <summary>Wolf Marks - PvP currency.</summary>
    WolfMarks = 400,
    /// <summary>Trophy Crystals - PvP currency.</summary>
    TrophyCrystals = 401,

    /// <summary>Allied Seals - ARR/HW hunt currency.</summary>
    AlliedSeals = 500,
    /// <summary>Centurio Seals - SB hunt currency.</summary>
    CenturioSeals = 501,
    /// <summary>Sack of Nuts - ShB/EW/DT hunt currency.</summary>
    SackOfNuts = 502,

    /// <summary>Manderville Gold Saucer Points.</summary>
    MGP = 600,

    BicolorGemstone = 700,

    /// <summary>Venture tokens for retainer ventures.</summary>
    Ventures = 800,

    /// <summary>Total crystal count across all types.</summary>
    CrystalsTotal = 900,
    /// <summary>Fire Crystals/Clusters/Shards total.</summary>
    FireCrystals = 901,
    /// <summary>Ice Crystals/Clusters/Shards total.</summary>
    IceCrystals = 902,
    /// <summary>Wind Crystals/Clusters/Shards total.</summary>
    WindCrystals = 903,
    /// <summary>Earth Crystals/Clusters/Shards total.</summary>
    EarthCrystals = 904,
    /// <summary>Lightning Crystals/Clusters/Shards total.</summary>
    LightningCrystals = 905,
    /// <summary>Water Crystals/Clusters/Shards total.</summary>
    WaterCrystals = 906,

    InventoryFreeSlots = 1000,

    FreeCompanyGil = 1100,
    /// <summary>Retainer gil (aggregate).</summary>
    RetainerGil = 1101,
    /// <summary>Free Company Credits (points for FC actions).</summary>
    FreeCompanyCredits = 1102,

    /// <summary>Market value of inventory items via Universalis prices.</summary>
    InventoryValueItems = 1200,
}

/// <summary>
/// Category for grouping tracked data types in UI.
/// Order determines display order in dropdowns and config UI.
/// </summary>
public enum TrackedDataCategory
{
    Gil,
    Tomestone,
    Scrip,
    GrandCompany,
    PvP,
    Hunt,
    GoldSaucer,
    Tribal,
    Crafting,
    Retainer,
    Inventory,
    Universalis, // Last - Inventory Value appears at the end
}
