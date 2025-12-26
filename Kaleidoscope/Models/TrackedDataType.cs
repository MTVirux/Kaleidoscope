namespace Kaleidoscope.Models;

/// <summary>
/// Enumeration of all trackable data types in the plugin.
/// These represent currencies, resources, and inventory metrics that can be sampled and tracked over time.
/// </summary>
public enum TrackedDataType
{
    // === Core Currencies ===
    /// <summary>Gil - the primary currency.</summary>
    Gil = 0,

    // === Tomestones ===
    /// <summary>Allagan Tomestone of Poetics - uncapped endgame currency.</summary>
    TomestonePoetics = 100,
    /// <summary>Allagan Tomestone (Weekly Capped) - current expansion capped tomestone.</summary>
    TomestoneCapped = 101,
    /// <summary>Allagan Tomestone (Uncapped) - current expansion uncapped tomestone.</summary>
    TomestoneUncapped = 102,

    // === Scrips ===
    /// <summary>White Crafters' Scrip.</summary>
    WhiteCraftersScrip = 200,
    /// <summary>Purple Crafters' Scrip.</summary>
    PurpleCraftersScrip = 201,
    /// <summary>Orange Crafters' Scrip.</summary>
    OrangeCraftersScrip = 202,
    /// <summary>White Gatherers' Scrip.</summary>
    WhiteGatherersScrip = 210,
    /// <summary>Purple Gatherers' Scrip.</summary>
    PurpleGatherersScrip = 211,
    /// <summary>Orange Gatherers' Scrip.</summary>
    OrangeGatherersScrip = 212,
    /// <summary>Skybuilders' Scrip.</summary>
    SkybuildersScrip = 220,

    // === Grand Company Seals ===
    /// <summary>Grand Company Seals (Maelstrom).</summary>
    MaelstromSeals = 300,
    /// <summary>Grand Company Seals (Twin Adder).</summary>
    TwinAdderSeals = 301,
    /// <summary>Grand Company Seals (Immortal Flames).</summary>
    ImmortalFlamesSeals = 302,

    // === PvP Currencies ===
    /// <summary>Wolf Marks - PvP currency.</summary>
    WolfMarks = 400,
    /// <summary>Trophy Crystals - PvP currency.</summary>
    TrophyCrystals = 401,

    // === Hunt Currencies ===
    /// <summary>Allied Seals - ARR/HW hunt currency.</summary>
    AlliedSeals = 500,
    /// <summary>Centurio Seals - SB hunt currency.</summary>
    CenturioSeals = 501,
    /// <summary>Sack of Nuts - ShB/EW/DT hunt currency.</summary>
    SackOfNuts = 502,

    // === Gold Saucer ===
    /// <summary>Manderville Gold Saucer Points.</summary>
    MGP = 600,

    // === Tribal Currencies ===
    /// <summary>Bicolor Gemstones.</summary>
    BicolorGemstone = 700,

    // === Ventures ===
    /// <summary>Venture tokens for retainer ventures.</summary>
    Ventures = 800,

    // === Crystals (Aggregate) ===
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

    // === Inventory Space ===
    /// <summary>Number of free inventory slots.</summary>
    InventoryFreeSlots = 1000,

    // === FC/Retainer ===
    /// <summary>Free Company gil (if applicable).</summary>
    FreeCompanyGil = 1100,
    /// <summary>Retainer gil (aggregate).</summary>
    RetainerGil = 1101,
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
    Inventory, // Last - Free Inventory Slots appears at the end
}
