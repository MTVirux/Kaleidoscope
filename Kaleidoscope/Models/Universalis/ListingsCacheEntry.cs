namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Cached lowest listing data for an item in a specific world.
/// Stores up to 5 lowest prices for more accurate reference calculations.
/// </summary>
public sealed class ListingsCacheEntry
{
    /// <summary>Maximum number of prices to track per quality type.</summary>
    public const int MaxPricesPerType = 5;

    /// <summary>The item ID.</summary>
    public int ItemId { get; set; }

    /// <summary>The world ID.</summary>
    public int WorldId { get; set; }

    /// <summary>The lowest NQ listing prices (up to 5, sorted ascending). Empty if no NQ listings.</summary>
    public List<int> LowestPricesNq { get; set; } = new();

    /// <summary>The lowest HQ listing prices (up to 5, sorted ascending). Empty if no HQ listings.</summary>
    public List<int> LowestPricesHq { get; set; } = new();

    /// <summary>When this cache entry was last updated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>The lowest NQ listing price (0 if no NQ listings).</summary>
    public int MinPriceNq => LowestPricesNq.Count > 0 ? LowestPricesNq[0] : 0;

    /// <summary>The lowest HQ listing price (0 if no HQ listings).</summary>
    public int MinPriceHq => LowestPricesHq.Count > 0 ? LowestPricesHq[0] : 0;

    /// <summary>
    /// Gets the average of the lowest prices for NQ listings.
    /// Returns 0 if no listings exist.
    /// </summary>
    public double AveragePriceNq => LowestPricesNq.Count > 0 ? LowestPricesNq.Average() : 0;

    /// <summary>
    /// Gets the average of the lowest prices for HQ listings.
    /// Returns 0 if no listings exist.
    /// </summary>
    public double AveragePriceHq => LowestPricesHq.Count > 0 ? LowestPricesHq.Average() : 0;

    /// <summary>
    /// Gets the median of the lowest prices for NQ listings.
    /// More robust against outliers than average.
    /// </summary>
    public double MedianPriceNq => GetMedian(LowestPricesNq);

    /// <summary>
    /// Gets the median of the lowest prices for HQ listings.
    /// More robust against outliers than average.
    /// </summary>
    public double MedianPriceHq => GetMedian(LowestPricesHq);

    /// <summary>
    /// Calculates the median of a list of prices.
    /// </summary>
    private static double GetMedian(List<int> prices)
    {
        if (prices.Count == 0) return 0;
        // Prices are already sorted ascending
        int mid = prices.Count / 2;
        return prices.Count % 2 == 0
            ? (prices[mid - 1] + prices[mid]) / 2.0
            : prices[mid];
    }

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
    /// Adds a price to the appropriate list, maintaining sorted order and max size.
    /// </summary>
    /// <param name="price">The price to add.</param>
    /// <param name="isHq">Whether this is an HQ listing.</param>
    /// <returns>True if the price was added or updated the list.</returns>
    public bool AddPrice(int price, bool isHq)
    {
        if (price <= 0) return false;

        var list = isHq ? LowestPricesHq : LowestPricesNq;

        // If list is empty or price is lower than highest in list, add it
        if (list.Count < MaxPricesPerType || price < list[^1])
        {
            // Insert in sorted position
            var insertIndex = list.BinarySearch(price);
            if (insertIndex < 0) insertIndex = ~insertIndex;
            list.Insert(insertIndex, price);

            // Trim to max size
            while (list.Count > MaxPricesPerType)
            {
                list.RemoveAt(list.Count - 1);
            }

            LastUpdated = DateTime.UtcNow;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sets all prices from a collection (e.g., from API response).
    /// </summary>
    /// <param name="prices">The prices to set.</param>
    /// <param name="isHq">Whether these are HQ listings.</param>
    public void SetPrices(IEnumerable<int> prices, bool isHq)
    {
        var list = isHq ? LowestPricesHq : LowestPricesNq;
        list.Clear();
        list.AddRange(prices.Where(p => p > 0).OrderBy(p => p).Take(MaxPricesPerType));
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns whether this entry is stale (older than the specified threshold).
    /// </summary>
    public bool IsStale(TimeSpan threshold)
    {
        return DateTime.UtcNow - LastUpdated > threshold;
    }
}
