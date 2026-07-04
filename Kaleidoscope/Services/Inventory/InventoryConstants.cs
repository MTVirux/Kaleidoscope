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
    /// The retainer full-scan list lives at ReconcileScanner.RetainerContainers (single source).
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
