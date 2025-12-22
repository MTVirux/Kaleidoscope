using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Market board sale history response from Universalis API.
/// GET /api/v2/history/{worldDcRegion}/{itemIds}
/// </summary>
public sealed class MarketHistory
{
    /// <summary>The item ID.</summary>
    [JsonPropertyName("itemID")]
    public int ItemId { get; set; }

    /// <summary>The world ID, if applicable.</summary>
    [JsonPropertyName("worldID")]
    public int? WorldId { get; set; }

    /// <summary>The world name, if applicable.</summary>
    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    /// <summary>The DC name, if applicable.</summary>
    [JsonPropertyName("dcName")]
    public string? DcName { get; set; }

    /// <summary>The region name, if applicable.</summary>
    [JsonPropertyName("regionName")]
    public string? RegionName { get; set; }

    /// <summary>The last upload time for this endpoint, in milliseconds since the UNIX epoch.</summary>
    [JsonPropertyName("lastUploadTime")]
    public long LastUploadTime { get; set; }

    /// <summary>The historical sales.</summary>
    [JsonPropertyName("entries")]
    public List<HistorySale>? Entries { get; set; }

    /// <summary>The average number of sales per day, over the past seven days.</summary>
    [JsonPropertyName("regularSaleVelocity")]
    public float RegularSaleVelocity { get; set; }

    /// <summary>The average number of NQ sales per day, over the past seven days.</summary>
    [JsonPropertyName("nqSaleVelocity")]
    public float NqSaleVelocity { get; set; }

    /// <summary>The average number of HQ sales per day, over the past seven days.</summary>
    [JsonPropertyName("hqSaleVelocity")]
    public float HqSaleVelocity { get; set; }

    /// <summary>Gets the last upload time as a DateTime.</summary>
    public DateTime LastUploadDateTime => DateTimeOffset.FromUnixTimeMilliseconds(LastUploadTime).LocalDateTime;
}

/// <summary>
/// Minimized sale entry for history responses.
/// </summary>
public sealed class HistorySale
{
    /// <summary>Whether or not the item was high-quality.</summary>
    [JsonPropertyName("hq")]
    public bool IsHq { get; set; }

    /// <summary>The price per unit sold.</summary>
    [JsonPropertyName("pricePerUnit")]
    public int PricePerUnit { get; set; }

    /// <summary>The stack size sold.</summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    /// <summary>The buyer's character name. This may be null.</summary>
    [JsonPropertyName("buyerName")]
    public string? BuyerName { get; set; }

    /// <summary>Whether or not this was purchased from a mannequin.</summary>
    [JsonPropertyName("onMannequin")]
    public bool? OnMannequin { get; set; }

    /// <summary>The sale time, in seconds since the UNIX epoch.</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>The world name, if applicable.</summary>
    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    /// <summary>The world ID, if applicable.</summary>
    [JsonPropertyName("worldID")]
    public int? WorldId { get; set; }

    /// <summary>Gets the sale timestamp as a DateTime.</summary>
    public DateTime SaleDateTime => DateTimeOffset.FromUnixTimeSeconds(Timestamp).LocalDateTime;
}
