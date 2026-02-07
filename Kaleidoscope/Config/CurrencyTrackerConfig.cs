namespace Kaleidoscope.Config;

/// <summary>
/// Configuration for the currency tracking service.
/// </summary>
public class CurrencyTrackerConfig
{
    /// <summary>
    /// SQLite page cache size in megabytes.
    /// Higher values improve read performance at the cost of RAM usage.
    /// Each database connection uses this amount of cache.
    /// Default: 8 MB, Range: 1-512 MB
    /// </summary>
    public int DatabaseCacheSizeMb { get; set; } = 8;
}
