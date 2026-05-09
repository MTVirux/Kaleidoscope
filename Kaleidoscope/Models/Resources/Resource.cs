namespace Kaleidoscope.Models.Resources;

/// <summary>
/// Identity of a Resource — uniquely identifies a slot occupant or synthetic counter.
/// Equality is structural on all five components.
/// </summary>
public readonly record struct ResourceKey
{
    /// <summary>ContentId for player, RetainerId for retainer, FC ID for free company.</summary>
    public ulong OwnerId { get; init; }

    public OwnerKind OwnerKind { get; init; }

    public Container Container { get; init; }

    /// <summary>Game item ID (real items) or synthetic ID from ResourceCatalog (Gil, MGP, etc.).</summary>
    public uint ItemId { get; init; }

    /// <summary>Slot index within the container, or -1 for non-slotted resources (currencies in SpecialPlayer, etc.).</summary>
    public short Slot { get; init; }
}

/// <summary>
/// A snapshot of a single resource at a point in time.
/// Stored as a record struct so dictionary copies are cheap and there is no GC pressure on the hot path.
/// </summary>
public readonly record struct Resource
{
    public ResourceKey Key { get; init; }

    public long Quantity { get; init; }

    public ResourceFlags Flags { get; init; }

    /// <summary>Demuxed from game's SpiritbondOrCollectability when Flags has no Collectable bit.</summary>
    public ushort Spiritbond { get; init; }

    /// <summary>Demuxed from game's SpiritbondOrCollectability when Flags has the Collectable bit.</summary>
    public ushort Collectability { get; init; }

    public ushort Condition { get; init; }

    public uint GlamourId { get; init; }

    public DateTime UpdatedAt { get; init; }
}
