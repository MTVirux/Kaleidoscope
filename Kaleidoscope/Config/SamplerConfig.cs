namespace Kaleidoscope.Config;

/// <summary>
/// Configuration for the data sampling service.
/// </summary>
public class SamplerConfig
{
    public bool SamplerEnabled { get; set; } = false;
    public int SamplerIntervalMs { get; set; } = 1000;
    
    /// <summary>
    /// SQLite page cache size in megabytes.
    /// Higher values improve read performance at the cost of RAM usage.
    /// Each database connection uses this amount of cache.
    /// Default: 8 MB, Range: 1-64 MB
    /// </summary>
    public int DatabaseCacheSizeMb { get; set; } = 8;
}
