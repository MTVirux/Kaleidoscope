namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Cached lowest listing data for an item in a specific world.
/// </summary>
public sealed class ListingsCacheEntry
{
    /// <summary>The item ID.</summary>
    public int ItemId { get; set; }

    /// <summary>The world ID.</summary>
    public int WorldId { get; set; }

    /// <summary>The lowest NQ listing price (0 if no NQ listings).</summary>
    public int MinPriceNq { get; set; }

    /// <summary>The lowest HQ listing price (0 if no HQ listings).</summary>
    public int MinPriceHq { get; set; }

    /// <summary>When this cache entry was last updated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Gets the absolute lowest price (either NQ or HQ).
    /// Returns 0 if no listings exist.
    /// </summary>
    public int LowestPrice
    {
        get
        {
            if (MinPriceNq == 0 && MinPriceHq == 0) return 0;
            if (MinPriceNq == 0) return MinPriceHq;
            if (MinPriceHq == 0) return MinPriceNq;
            return Math.Min(MinPriceNq, MinPriceHq);
        }
    }

    /// <summary>
    /// Returns whether this entry is stale (older than the specified threshold).
    /// </summary>
    public bool IsStale(TimeSpan threshold)
    {
        return DateTime.UtcNow - LastUpdated > threshold;
    }
}
