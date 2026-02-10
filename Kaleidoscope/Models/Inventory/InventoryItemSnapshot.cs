namespace Kaleidoscope.Models.Inventory;

/// <summary>
/// Represents an immutable snapshot of a single inventory item at a point in time.
/// Used for caching and tracking inventory contents across characters and retainers.
/// Stored contiguously in List&lt;T&gt; arrays for cache-friendly access and reduced GC pressure.
/// </summary>
public readonly record struct InventoryItemSnapshot
{
    /// <summary>
    /// The game's internal item ID.
    /// </summary>
    public uint ItemId { get; init; }

    /// <summary>
    /// The quantity/stack size of the item.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// Whether this is a high-quality item.
    /// </summary>
    public bool IsHq { get; init; }

    /// <summary>
    /// Whether this item is collectible.
    /// </summary>
    public bool IsCollectable { get; init; }

    /// <summary>
    /// The slot index within the container.
    /// </summary>
    public short Slot { get; init; }

    /// <summary>
    /// The inventory container type (e.g., Inventory1, RetainerPage1, etc.)
    /// Stored as uint to match FFXIVClientStructs InventoryType enum.
    /// </summary>
    public uint ContainerType { get; init; }

    /// <summary>
    /// Spiritbond level (0-10000) or collectability value if applicable.
    /// </summary>
    public ushort SpiritbondOrCollectability { get; init; }

    /// <summary>
    /// Item condition (0-30000, representing 0-100% in increments).
    /// </summary>
    public ushort Condition { get; init; }

    /// <summary>
    /// The glamour item ID applied to this item, if any.
    /// </summary>
    public uint GlamourId { get; init; }

    /// <summary>
    /// Whether this item is bound (spiritbond > 0) and therefore untradeable on the market board.
    /// Collectables are excluded since they reuse the SpiritbondOrCollectability field for collectability value.
    /// Covers both equipment (gear) and non-equipment items that gain spiritbond (e.g., submersible parts after voyages).
    /// </summary>
    public bool IsBound => SpiritbondOrCollectability > 0 && !IsCollectable;
}
