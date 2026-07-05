namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Scope mode for price tracking subscriptions.
/// </summary>
public enum PriceTrackingScopeMode
{
    /// <summary>Track all worlds/DCs automatically.</summary>
    All = 0,
    /// <summary>Track specific regions.</summary>
    ByRegion = 1,
    /// <summary>Track specific data centers.</summary>
    ByDataCenter = 2,
    /// <summary>Track specific worlds.</summary>
    ByWorld = 3
}

/// <summary>
/// Configuration for price tracking feature.
/// </summary>
public sealed class PriceTrackingSettings
{
    /// <summary>Whether price tracking is enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Number of days to retain inventory value history.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Scope mode for which worlds/DCs to track.</summary>
    public PriceTrackingScopeMode ScopeMode { get; set; } = PriceTrackingScopeMode.All;

    /// <summary>Selected region names when using ByRegion scope mode.</summary>
    public HashSet<string> SelectedRegions { get; set; } = new();

    /// <summary>Selected data center names when using ByDataCenter scope mode.</summary>
    public HashSet<string> SelectedDataCenters { get; set; } = new();

    /// <summary>Selected world IDs when using ByWorld scope mode.</summary>
    public HashSet<int> SelectedWorldIds { get; set; } = new();

    /// <summary>Item IDs to exclude from tracking.</summary>
    public HashSet<int> ExcludedItemIds { get; set; } = new();

    /// <summary>Whether to automatically fetch initial prices from API for items in inventory.</summary>
    public bool AutoFetchInventoryPrices { get; set; } = true;

    /// <summary>Interval in minutes for cleaning up old price data.</summary>
    public int CleanupIntervalMinutes { get; set; } = 60;

    // WebSocket Channel Subscriptions
    /// <summary>Whether to subscribe to listings/add events (new listings).</summary>
    public bool SubscribeListingsAdd { get; set; } = true;

    /// <summary>Whether to subscribe to listings/remove events (removed listings).</summary>
    public bool SubscribeListingsRemove { get; set; } = true;

    /// <summary>Whether to subscribe to sales/add events (completed sales).</summary>
    public bool SubscribeSalesAdd { get; set; } = true;

    /// <summary>Whether to filter out sales with large discrepancies from current listings.</summary>
    public bool FilterSalesByListingPrice { get; set; } = true;

    /// <summary>Maximum allowed discrepancy percentage (0-100) between sale price and listing price. Sales outside this range are ignored.</summary>
    public int SaleDiscrepancyThreshold { get; set; } = 50;

    /// <summary>Minimum unit price for sale filtering to apply. Sales below this price skip the discrepancy filter.</summary>
    public int SaleFilterMinimumPrice { get; set; } = 10000;

    /// <summary>Whether to use median instead of average for reference price calculation. More robust against outliers.</summary>
    public bool UseMedianForReference { get; set; } = true;

    /// <summary>Whether to use standard deviation-based filtering instead of fixed percentage threshold.</summary>
    public bool UseStdDevFilter { get; set; } = false;

    /// <summary>Number of standard deviations from mean to consider a price an outlier. Only used when UseStdDevFilter is true.</summary>
    public double StdDevThreshold { get; set; } = 2.0;

    /// <summary>Whether to adjust threshold for bulk/stack sales. Larger quantities get more lenient thresholds.</summary>
    public bool AdjustForBulkSales { get; set; } = true;

    /// <summary>Maximum leniency multiplier for bulk sales (e.g., 1.5 = 50% more lenient for large stacks).</summary>
    public double BulkSaleMaxLeniency { get; set; } = 1.5;

    /// <summary>
    /// Minimum interval in milliseconds between event-driven inventory value recalculations.
    /// Lower values provide more responsive updates but increase CPU/DB load.
    /// Ignored when <see cref="ValueRecalcOnEveryUpdate"/> is true.
    /// Minimum: 50ms. Default: 30000ms (30 seconds).
    /// </summary>
    public int ValueRecalcIntervalMs { get; set; } = 30000;

    /// <summary>
    /// When true, recalculate inventory values on every price update without throttling.
    /// This provides the most responsive updates but may impact performance on busy servers.
    /// </summary>
    public bool ValueRecalcOnEveryUpdate { get; set; } = false;
}
