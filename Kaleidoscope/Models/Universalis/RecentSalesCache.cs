namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Cached recent sale prices for an item in a specific world.
/// Stores up to 5 most recent sale prices for more accurate reference calculations.
/// </summary>
public sealed class RecentSalesCacheEntry
{
    /// <summary>Maximum number of sales to track per quality type.</summary>
    public const int MaxSalesPerType = 5;

    /// <summary>The item ID.</summary>
    public int ItemId { get; set; }

    /// <summary>The world ID.</summary>
    public int WorldId { get; set; }

    /// <summary>The most recent NQ sale prices (up to 5, most recent first). Empty if no NQ sales.</summary>
    public List<int> RecentPricesNq { get; set; } = new();

    /// <summary>The most recent HQ sale prices (up to 5, most recent first). Empty if no HQ sales.</summary>
    public List<int> RecentPricesHq { get; set; } = new();

    /// <summary>When this cache entry was last updated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>The most recent NQ sale price (0 if no NQ sales).</summary>
    public int LastPriceNq => RecentPricesNq.Count > 0 ? RecentPricesNq[0] : 0;

    /// <summary>The most recent HQ sale price (0 if no HQ sales).</summary>
    public int LastPriceHq => RecentPricesHq.Count > 0 ? RecentPricesHq[0] : 0;

    /// <summary>
    /// Gets the average of the recent sale prices for NQ.
    /// Returns 0 if no sales exist.
    /// </summary>
    public double AveragePriceNq => RecentPricesNq.Count > 0 ? RecentPricesNq.Average() : 0;

    /// <summary>
    /// Gets the average of the recent sale prices for HQ.
    /// Returns 0 if no sales exist.
    /// </summary>
    public double AveragePriceHq => RecentPricesHq.Count > 0 ? RecentPricesHq.Average() : 0;

    /// <summary>
    /// Gets the median of the recent sale prices for NQ.
    /// More robust against outliers than average.
    /// </summary>
    public double MedianPriceNq => GetMedian(RecentPricesNq);

    /// <summary>
    /// Gets the median of the recent sale prices for HQ.
    /// More robust against outliers than average.
    /// </summary>
    public double MedianPriceHq => GetMedian(RecentPricesHq);

    /// <summary>
    /// Gets the standard deviation of recent NQ sale prices.
    /// Returns 0 if fewer than 2 sales exist.
    /// </summary>
    public double StdDevNq => GetStdDev(RecentPricesNq);

    /// <summary>
    /// Gets the standard deviation of recent HQ sale prices.
    /// Returns 0 if fewer than 2 sales exist.
    /// </summary>
    public double StdDevHq => GetStdDev(RecentPricesHq);

    /// <summary>
    /// Calculates the median of a list of prices.
    /// </summary>
    private static double GetMedian(List<int> prices)
    {
        if (prices.Count == 0) return 0;
        var sorted = prices.OrderBy(p => p).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    /// <summary>
    /// Calculates the standard deviation of a list of prices.
    /// </summary>
    private static double GetStdDev(List<int> prices)
    {
        if (prices.Count < 2) return 0;
        var mean = prices.Average();
        var sumOfSquares = prices.Sum(p => Math.Pow(p - mean, 2));
        return Math.Sqrt(sumOfSquares / prices.Count);
    }

    /// <summary>
    /// Adds a sale price to the front of the appropriate list (most recent first).
    /// </summary>
    /// <param name="price">The price to add.</param>
    /// <param name="isHq">Whether this is an HQ sale.</param>
    public void AddSale(int price, bool isHq)
    {
        if (price <= 0) return;

        var list = isHq ? RecentPricesHq : RecentPricesNq;

        // Insert at front (most recent)
        list.Insert(0, price);

        // Trim to max size
        while (list.Count > MaxSalesPerType)
        {
            list.RemoveAt(list.Count - 1);
        }

        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets all prices from a collection (e.g., from DB or API response).
    /// Assumes prices are already in most-recent-first order.
    /// </summary>
    /// <param name="prices">The prices to set.</param>
    /// <param name="isHq">Whether these are HQ sales.</param>
    public void SetPrices(IEnumerable<int> prices, bool isHq)
    {
        var list = isHq ? RecentPricesHq : RecentPricesNq;
        list.Clear();
        list.AddRange(prices.Where(p => p > 0).Take(MaxSalesPerType));
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
