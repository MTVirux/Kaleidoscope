using FFXIVClientStructs.FFXIV.Client.Game;

namespace Kaleidoscope.Services.Inventory;

/// <summary>
/// Shared constants for inventory container types used across the plugin.
/// Centralizes container type definitions to avoid duplication and ensure consistency.
/// </summary>
public static class InventoryConstants
{
    /// <summary>
    /// Retainer inventory pages only (excludes equipped items, crystals, and market).
    /// Used for counting item quantities in retainer storage.
    /// For the full retainer reconcile/readiness set see <see cref="RetainerScanContainers"/>.
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

    /// <summary>
    /// Every retainer container touched by a retainer-open reconcile: the seven storage pages plus
    /// equipped items, crystals and market. Single source shared by ReconcileScanner (the full scan)
    /// and GameStateService.AreRetainerContainersLoaded (the readiness gate) so the readiness check
    /// and the scan operate on exactly the same set.
    /// </summary>
    public static readonly InventoryType[] RetainerScanContainers =
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
}
