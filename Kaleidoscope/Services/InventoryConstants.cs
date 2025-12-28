using FFXIVClientStructs.FFXIV.Client.Game;

namespace Kaleidoscope.Services;

/// <summary>
/// Shared constants for inventory container types used across the plugin.
/// Centralizes container type definitions to avoid duplication and ensure consistency.
/// </summary>
public static class InventoryConstants
{
    /// <summary>
    /// Player inventory containers to scan for items.
    /// Includes main inventory, equipped items, armory, crystals, currency, and saddlebags.
    /// </summary>
    public static readonly InventoryType[] PlayerInventoryContainers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.EquippedItems,
        InventoryType.Crystals,
        InventoryType.Currency,
        InventoryType.KeyItems,
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.ArmorySoulCrystal,
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
    ];

    /// <summary>
    /// Retainer inventory containers to scan for items.
    /// Includes all retainer inventory pages, equipped items, crystals, and market board listings.
    /// </summary>
    public static readonly InventoryType[] RetainerInventoryContainers =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
        InventoryType.RetainerEquippedItems,
        InventoryType.RetainerCrystals,
        InventoryType.RetainerMarket,
    ];

    /// <summary>
    /// Retainer inventory pages only (excludes equipped items, crystals, and market).
    /// Used for counting item quantities in retainer storage.
    /// </summary>
    public static readonly InventoryType[] RetainerStoragePages =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
    ];
}
