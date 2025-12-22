namespace Kaleidoscope.Models.Inventory;

/// <summary>
/// Type of inventory source (player or retainer).
/// </summary>
public enum InventorySourceType
{
    /// <summary>Player's own inventory.</summary>
    Player = 0,
    /// <summary>Retainer's inventory.</summary>
    Retainer = 1
}

/// <summary>
/// Represents a cached snapshot of all inventory items for a player or retainer.
/// Contains metadata about when and whose inventory this is, plus the actual items.
/// </summary>
public sealed class InventoryCacheEntry
{
    /// <summary>
    /// The content ID of the character who owns this inventory (or whose retainer it is).
    /// </summary>
    public ulong CharacterId { get; set; }

    /// <summary>
    /// The source type - player or retainer.
    /// </summary>
    public InventorySourceType SourceType { get; set; }

    /// <summary>
    /// For retainers, this is the retainer's unique ID. For players, this is 0.
    /// </summary>
    public ulong RetainerId { get; set; }

    /// <summary>
    /// Display name of the player or retainer.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// World name (for players) - helps identify characters across accounts.
    /// </summary>
    public string? World { get; set; }

    /// <summary>
    /// The UTC timestamp when this cache was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The cached inventory items.
    /// </summary>
    public List<InventoryItemSnapshot> Items { get; set; } = new();

    /// <summary>
    /// Total gil held (convenience field, can be derived from player state or retainer data).
    /// </summary>
    public long Gil { get; set; }

    /// <summary>
    /// Creates an empty cache entry.
    /// </summary>
    public InventoryCacheEntry() { }

    /// <summary>
    /// Creates a cache entry for a player.
    /// </summary>
    public static InventoryCacheEntry ForPlayer(ulong characterId, string? name, string? world)
    {
        return new InventoryCacheEntry
        {
            CharacterId = characterId,
            SourceType = InventorySourceType.Player,
            RetainerId = 0,
            Name = name,
            World = world,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a cache entry for a retainer.
    /// </summary>
    public static InventoryCacheEntry ForRetainer(ulong characterId, ulong retainerId, string? retainerName)
    {
        return new InventoryCacheEntry
        {
            CharacterId = characterId,
            SourceType = InventorySourceType.Retainer,
            RetainerId = retainerId,
            Name = retainerName,
            World = null,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets a unique key for this cache entry (used for dictionary lookups).
    /// </summary>
    public string GetCacheKey()
    {
        return SourceType == InventorySourceType.Player
            ? $"player_{CharacterId}"
            : $"retainer_{CharacterId}_{RetainerId}";
    }

    /// <summary>
    /// Gets the total number of items in this cache (including stack counts).
    /// </summary>
    public long GetTotalItemCount()
    {
        long total = 0;
        foreach (var item in Items)
        {
            total += item.Quantity;
        }
        return total;
    }

    /// <summary>
    /// Gets the count of a specific item (by item ID) including stacks.
    /// </summary>
    public long GetItemCount(uint itemId, bool? isHq = null)
    {
        long count = 0;
        foreach (var item in Items)
        {
            if (item.ItemId == itemId)
            {
                if (isHq == null || item.IsHq == isHq.Value)
                {
                    count += item.Quantity;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Gets all unique item IDs in this cache.
    /// </summary>
    public HashSet<uint> GetUniqueItemIds()
    {
        var ids = new HashSet<uint>();
        foreach (var item in Items)
        {
            ids.Add(item.ItemId);
        }
        return ids;
    }
}
