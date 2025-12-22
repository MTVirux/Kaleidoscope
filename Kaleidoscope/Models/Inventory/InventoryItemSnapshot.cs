namespace Kaleidoscope.Models.Inventory;

/// <summary>
/// Represents a snapshot of a single inventory item at a point in time.
/// Used for caching and tracking inventory contents across characters and retainers.
/// </summary>
public sealed class InventoryItemSnapshot
{
    /// <summary>
    /// The game's internal item ID.
    /// </summary>
    public uint ItemId { get; set; }

    /// <summary>
    /// The quantity/stack size of the item.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Whether this is a high-quality item.
    /// </summary>
    public bool IsHq { get; set; }

    /// <summary>
    /// Whether this item is collectible.
    /// </summary>
    public bool IsCollectable { get; set; }

    /// <summary>
    /// The slot index within the container.
    /// </summary>
    public short Slot { get; set; }

    /// <summary>
    /// The inventory container type (e.g., Inventory1, RetainerPage1, etc.)
    /// Stored as uint to match FFXIVClientStructs InventoryType enum.
    /// </summary>
    public uint ContainerType { get; set; }

    /// <summary>
    /// Spiritbond level (0-10000) or collectability value if applicable.
    /// </summary>
    public ushort SpiritbondOrCollectability { get; set; }

    /// <summary>
    /// Item condition (0-30000, representing 0-100% in increments).
    /// </summary>
    public ushort Condition { get; set; }

    /// <summary>
    /// The glamour item ID applied to this item, if any.
    /// </summary>
    public uint GlamourId { get; set; }

    /// <summary>
    /// Creates an empty snapshot.
    /// </summary>
    public InventoryItemSnapshot() { }

    /// <summary>
    /// Creates a snapshot with the specified values.
    /// </summary>
    public InventoryItemSnapshot(uint itemId, int quantity, bool isHq, bool isCollectable, short slot, uint containerType)
    {
        ItemId = itemId;
        Quantity = quantity;
        IsHq = isHq;
        IsCollectable = isCollectable;
        Slot = slot;
        ContainerType = containerType;
    }
}
