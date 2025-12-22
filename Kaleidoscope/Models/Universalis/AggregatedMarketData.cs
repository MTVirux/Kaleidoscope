using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Aggregated market board data response from Universalis API.
/// GET /api/v2/aggregated/{worldDcRegion}/{itemIds}
/// Uses cached values for better performance.
/// </summary>
public sealed class AggregatedMarketData
{
    /// <summary>The aggregated results for each item.</summary>
    [JsonPropertyName("results")]
    public List<AggregatedResult>? Results { get; set; }

    /// <summary>Item IDs that failed to resolve.</summary>
    [JsonPropertyName("failedItems")]
    public List<int>? FailedItems { get; set; }
}

/// <summary>
/// Aggregated result for a single item.
/// </summary>
public sealed class AggregatedResult
{
    /// <summary>The item ID.</summary>
    [JsonPropertyName("itemId")]
    public int ItemId { get; set; }

    /// <summary>Normal quality aggregated data.</summary>
    [JsonPropertyName("nq")]
    public AggregatedQualityData? Nq { get; set; }

    /// <summary>High quality aggregated data.</summary>
    [JsonPropertyName("hq")]
    public AggregatedQualityData? Hq { get; set; }

    /// <summary>Upload times for each world.</summary>
    [JsonPropertyName("worldUploadTimes")]
    public List<WorldUploadTime>? WorldUploadTimes { get; set; }
}

/// <summary>
/// Aggregated data for a specific quality level (NQ or HQ).
/// </summary>
public sealed class AggregatedQualityData
{
    /// <summary>Minimum listing price data.</summary>
    [JsonPropertyName("minListing")]
    public MinListingData? MinListing { get; set; }

    /// <summary>Median listing price data.</summary>
    [JsonPropertyName("medianListing")]
    public MedianListingData? MedianListing { get; set; }

    /// <summary>Recent purchase data.</summary>
    [JsonPropertyName("recentPurchase")]
    public RecentPurchaseData? RecentPurchase { get; set; }

    /// <summary>Average sale price data (last 4 days).</summary>
    [JsonPropertyName("averageSalePrice")]
    public AverageSalePriceData? AverageSalePrice { get; set; }

    /// <summary>Daily sale velocity data (last 4 days).</summary>
    [JsonPropertyName("dailySaleVelocity")]
    public DailySaleVelocityData? DailySaleVelocity { get; set; }
}

/// <summary>
/// Minimum listing price across different scopes.
/// </summary>
public sealed class MinListingData
{
    [JsonPropertyName("world")]
    public MinListingEntry? World { get; set; }

    [JsonPropertyName("dc")]
    public MinListingEntry? Dc { get; set; }

    [JsonPropertyName("region")]
    public MinListingEntry? Region { get; set; }
}

public sealed class MinListingEntry
{
    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("worldId")]
    public int? WorldId { get; set; }
}

/// <summary>
/// Median listing price across different scopes.
/// </summary>
public sealed class MedianListingData
{
    [JsonPropertyName("world")]
    public MedianListingEntry? World { get; set; }

    [JsonPropertyName("dc")]
    public MedianListingEntry? Dc { get; set; }

    [JsonPropertyName("region")]
    public MedianListingEntry? Region { get; set; }
}

public sealed class MedianListingEntry
{
    [JsonPropertyName("price")]
    public int Price { get; set; }
}

/// <summary>
/// Recent purchase data across different scopes.
/// </summary>
public sealed class RecentPurchaseData
{
    [JsonPropertyName("world")]
    public RecentPurchaseEntry? World { get; set; }

    [JsonPropertyName("dc")]
    public RecentPurchaseEntry? Dc { get; set; }

    [JsonPropertyName("region")]
    public RecentPurchaseEntry? Region { get; set; }
}

public sealed class RecentPurchaseEntry
{
    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("worldId")]
    public int? WorldId { get; set; }

    /// <summary>Gets the purchase timestamp as a DateTime.</summary>
    public DateTime PurchaseDateTime => DateTimeOffset.FromUnixTimeSeconds(Timestamp).LocalDateTime;
}

/// <summary>
/// Average sale price data across different scopes (last 4 days).
/// </summary>
public sealed class AverageSalePriceData
{
    [JsonPropertyName("world")]
    public AverageSalePriceEntry? World { get; set; }

    [JsonPropertyName("dc")]
    public AverageSalePriceEntry? Dc { get; set; }

    [JsonPropertyName("region")]
    public AverageSalePriceEntry? Region { get; set; }
}

public sealed class AverageSalePriceEntry
{
    [JsonPropertyName("price")]
    public float Price { get; set; }
}

/// <summary>
/// Daily sale velocity data across different scopes (last 4 days).
/// </summary>
public sealed class DailySaleVelocityData
{
    [JsonPropertyName("world")]
    public DailySaleVelocityEntry? World { get; set; }

    [JsonPropertyName("dc")]
    public DailySaleVelocityEntry? Dc { get; set; }

    [JsonPropertyName("region")]
    public DailySaleVelocityEntry? Region { get; set; }
}

public sealed class DailySaleVelocityEntry
{
    [JsonPropertyName("quantity")]
    public float Quantity { get; set; }
}

/// <summary>
/// World upload time information.
/// </summary>
public sealed class WorldUploadTime
{
    [JsonPropertyName("worldId")]
    public int WorldId { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>Gets the upload timestamp as a DateTime.</summary>
    public DateTime UploadDateTime => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).LocalDateTime;
}
