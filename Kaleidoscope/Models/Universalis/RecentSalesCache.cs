namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Cached recent sale prices for an item in a specific world.
/// Stores up to 5 most recent sale prices for more accurate reference calculations.
/// Thread-safe: mutations and statistic computations are guarded by an internal lock so the
/// entry can be written on the DB worker thread while being read on the WebSocket thread.
/// </summary>
public sealed class RecentSalesCacheEntry
{
    /// <summary>Maximum number of sales to track per quality type.</summary>
    public const int MaxSalesPerType = 5;

    private readonly object _lock = new();
    private readonly List<int> _recentPricesNq = new();
    private readonly List<int> _recentPricesHq = new();
    private DateTime _lastUpdated;

    /// <summary>The item ID.</summary>
    public int ItemId { get; set; }

    /// <summary>The world ID.</summary>
    public int WorldId { get; set; }

    /// <summary>The most recent NQ sale prices (up to 5, most recent first). Empty if no NQ sales.</summary>
    public List<int> RecentPricesNq
    {
        get { lock (_lock) { return new List<int>(_recentPricesNq); } }
        set { lock (_lock) { _recentPricesNq.Clear(); if (value != null) _recentPricesNq.AddRange(value); } }
    }

    /// <summary>The most recent HQ sale prices (up to 5, most recent first). Empty if no HQ sales.</summary>
    public List<int> RecentPricesHq
    {
        get { lock (_lock) { return new List<int>(_recentPricesHq); } }
        set { lock (_lock) { _recentPricesHq.Clear(); if (value != null) _recentPricesHq.AddRange(value); } }
    }

    /// <summary>When this cache entry was last updated.</summary>
    public DateTime LastUpdated
    {
        get { lock (_lock) { return _lastUpdated; } }
        set { lock (_lock) { _lastUpdated = value; } }
    }

    /// <summary>The most recent NQ sale price (0 if no NQ sales).</summary>
    public int LastPriceNq { get { lock (_lock) { return _recentPricesNq.Count > 0 ? _recentPricesNq[0] : 0; } } }

    /// <summary>The most recent HQ sale price (0 if no HQ sales).</summary>
    public int LastPriceHq { get { lock (_lock) { return _recentPricesHq.Count > 0 ? _recentPricesHq[0] : 0; } } }

    /// <summary>
    /// Gets the average of the recent sale prices for NQ.
    /// Returns 0 if no sales exist.
    /// </summary>
    public double AveragePriceNq { get { lock (_lock) { return _recentPricesNq.Count > 0 ? _recentPricesNq.Average() : 0; } } }

    /// <summary>
    /// Gets the average of the recent sale prices for HQ.
    /// Returns 0 if no sales exist.
    /// </summary>
    public double AveragePriceHq { get { lock (_lock) { return _recentPricesHq.Count > 0 ? _recentPricesHq.Average() : 0; } } }

    /// <summary>
    /// Gets the median of the recent sale prices for NQ.
    /// More robust against outliers than average.
    /// </summary>
    public double MedianPriceNq { get { lock (_lock) { return GetMedian(_recentPricesNq); } } }

    /// <summary>
    /// Gets the median of the recent sale prices for HQ.
    /// More robust against outliers than average.
    /// </summary>
    public double MedianPriceHq { get { lock (_lock) { return GetMedian(_recentPricesHq); } } }

    /// <summary>
    /// Gets the standard deviation of recent NQ sale prices.
    /// Returns 0 if fewer than 2 sales exist.
    /// </summary>
    public double StdDevNq { get { lock (_lock) { return GetStdDev(_recentPricesNq); } } }

    /// <summary>
    /// Gets the standard deviation of recent HQ sale prices.
    /// Returns 0 if fewer than 2 sales exist.
    /// </summary>
    public double StdDevHq { get { lock (_lock) { return GetStdDev(_recentPricesHq); } } }

    /// <summary>
    /// Calculates the median of a list of prices. Callers must hold the lock.
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
    /// Calculates the standard deviation of a list of prices. Callers must hold the lock.
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

        lock (_lock)
        {
            var list = isHq ? _recentPricesHq : _recentPricesNq;

            // Insert at front (most recent)
            list.Insert(0, price);

            // Trim to max size
            while (list.Count > MaxSalesPerType)
            {
                list.RemoveAt(list.Count - 1);
            }

            _lastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Sets all prices from a collection (e.g., from DB or API response).
    /// Assumes prices are already in most-recent-first order.
    /// </summary>
    /// <param name="prices">The prices to set.</param>
    /// <param name="isHq">Whether these are HQ sales.</param>
    public void SetPrices(IEnumerable<int> prices, bool isHq)
    {
        lock (_lock)
        {
            var list = isHq ? _recentPricesHq : _recentPricesNq;
            list.Clear();
            list.AddRange(prices.Where(p => p > 0).Take(MaxSalesPerType));
            _lastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns whether this entry is stale (older than the specified threshold).
    /// </summary>
    public bool IsStale(TimeSpan threshold)
    {
        lock (_lock)
        {
            return DateTime.UtcNow - _lastUpdated > threshold;
        }
    }
}
