using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Market board current data response from Universalis API.
/// GET /api/v2/{worldDcRegion}/{itemIds}
/// </summary>
public sealed class MarketBoardData
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

    /// <summary>The currently-shown listings.</summary>
    [JsonPropertyName("listings")]
    public List<MarketListing>? Listings { get; set; }

    /// <summary>The currently-shown sales.</summary>
    [JsonPropertyName("recentHistory")]
    public List<MarketSale>? RecentHistory { get; set; }

    /// <summary>The average listing price.</summary>
    [JsonPropertyName("currentAveragePrice")]
    public float CurrentAveragePrice { get; set; }

    /// <summary>The average NQ listing price.</summary>
    [JsonPropertyName("currentAveragePriceNQ")]
    public float CurrentAveragePriceNQ { get; set; }

    /// <summary>The average HQ listing price.</summary>
    [JsonPropertyName("currentAveragePriceHQ")]
    public float CurrentAveragePriceHQ { get; set; }

    /// <summary>The average number of sales per day, over the past seven days.</summary>
    [JsonPropertyName("regularSaleVelocity")]
    public float RegularSaleVelocity { get; set; }

    /// <summary>The average number of NQ sales per day, over the past seven days.</summary>
    [JsonPropertyName("nqSaleVelocity")]
    public float NqSaleVelocity { get; set; }

    /// <summary>The average number of HQ sales per day, over the past seven days.</summary>
    [JsonPropertyName("hqSaleVelocity")]
    public float HqSaleVelocity { get; set; }

    /// <summary>The average sale price.</summary>
    [JsonPropertyName("averagePrice")]
    public float AveragePrice { get; set; }

    /// <summary>The average NQ sale price.</summary>
    [JsonPropertyName("averagePriceNQ")]
    public float AveragePriceNQ { get; set; }

    /// <summary>The average HQ sale price.</summary>
    [JsonPropertyName("averagePriceHQ")]
    public float AveragePriceHQ { get; set; }

    /// <summary>The minimum listing price.</summary>
    [JsonPropertyName("minPrice")]
    public int MinPrice { get; set; }

    /// <summary>The minimum NQ listing price.</summary>
    [JsonPropertyName("minPriceNQ")]
    public int MinPriceNQ { get; set; }

    /// <summary>The minimum HQ listing price.</summary>
    [JsonPropertyName("minPriceHQ")]
    public int MinPriceHQ { get; set; }

    /// <summary>The maximum listing price.</summary>
    [JsonPropertyName("maxPrice")]
    public int MaxPrice { get; set; }

    /// <summary>The maximum NQ listing price.</summary>
    [JsonPropertyName("maxPriceNQ")]
    public int MaxPriceNQ { get; set; }

    /// <summary>The maximum HQ listing price.</summary>
    [JsonPropertyName("maxPriceHQ")]
    public int MaxPriceHQ { get; set; }

    /// <summary>The number of listings retrieved for the request.</summary>
    [JsonPropertyName("listingsCount")]
    public int ListingsCount { get; set; }

    /// <summary>The number of sale entries retrieved for the request.</summary>
    [JsonPropertyName("recentHistoryCount")]
    public int RecentHistoryCount { get; set; }

    /// <summary>The number of items (not listings) up for sale.</summary>
    [JsonPropertyName("unitsForSale")]
    public int UnitsForSale { get; set; }

    /// <summary>The number of items (not sale entries) sold over the retrieved sales.</summary>
    [JsonPropertyName("unitsSold")]
    public int UnitsSold { get; set; }

    /// <summary>Whether this item has ever been updated.</summary>
    [JsonPropertyName("hasData")]
    public bool HasData { get; set; }

    /// <summary>Gets the last upload time as a DateTime.</summary>
    public DateTime LastUploadDateTime => DateTimeOffset.FromUnixTimeMilliseconds(LastUploadTime).LocalDateTime;
}

/// <summary>
/// Individual market listing from Universalis.
/// </summary>
public sealed class MarketListing
{
    /// <summary>The time that this listing was posted, in seconds since the UNIX epoch.</summary>
    [JsonPropertyName("lastReviewTime")]
    public long LastReviewTime { get; set; }

    /// <summary>The price per unit sold.</summary>
    [JsonPropertyName("pricePerUnit")]
    public int PricePerUnit { get; set; }

    /// <summary>The stack size sold.</summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    /// <summary>The ID of the dye on this item.</summary>
    [JsonPropertyName("stainID")]
    public int StainId { get; set; }

    /// <summary>The world name, if applicable.</summary>
    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    /// <summary>The world ID, if applicable.</summary>
    [JsonPropertyName("worldID")]
    public int? WorldId { get; set; }

    /// <summary>The creator's character name.</summary>
    [JsonPropertyName("creatorName")]
    public string? CreatorName { get; set; }

    /// <summary>Whether or not the item is high-quality.</summary>
    [JsonPropertyName("hq")]
    public bool IsHq { get; set; }

    /// <summary>Whether or not the item is crafted.</summary>
    [JsonPropertyName("isCrafted")]
    public bool IsCrafted { get; set; }

    /// <summary>The ID of this listing.</summary>
    [JsonPropertyName("listingID")]
    public string? ListingId { get; set; }

    /// <summary>The materia on this item.</summary>
    [JsonPropertyName("materia")]
    public List<MateriaInfo>? Materia { get; set; }

    /// <summary>The city ID of the retainer.</summary>
    [JsonPropertyName("retainerCity")]
    public int RetainerCity { get; set; }

    /// <summary>The retainer's name.</summary>
    [JsonPropertyName("retainerName")]
    public string? RetainerName { get; set; }

    /// <summary>The total price.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>The Gil sales tax (GST) to be added to the total price during purchase.</summary>
    [JsonPropertyName("tax")]
    public int Tax { get; set; }

    /// <summary>Gets the last review time as a DateTime.</summary>
    public DateTime LastReviewDateTime => DateTimeOffset.FromUnixTimeSeconds(LastReviewTime).LocalDateTime;
}

/// <summary>
/// Sale history entry from Universalis.
/// </summary>
public sealed class MarketSale
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

    /// <summary>The sale time, in seconds since the UNIX epoch.</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>Whether or not this was purchased from a mannequin.</summary>
    [JsonPropertyName("onMannequin")]
    public bool? OnMannequin { get; set; }

    /// <summary>The world name, if applicable.</summary>
    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    /// <summary>The world ID, if applicable.</summary>
    [JsonPropertyName("worldID")]
    public int? WorldId { get; set; }

    /// <summary>The buyer name.</summary>
    [JsonPropertyName("buyerName")]
    public string? BuyerName { get; set; }

    /// <summary>The total price.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>Gets the sale timestamp as a DateTime.</summary>
    public DateTime SaleDateTime => DateTimeOffset.FromUnixTimeSeconds(Timestamp).LocalDateTime;
}

/// <summary>
/// Materia information for a listing.
/// </summary>
public sealed class MateriaInfo
{
    /// <summary>The materia slot.</summary>
    [JsonPropertyName("slotID")]
    public int SlotId { get; set; }

    /// <summary>The materia item ID.</summary>
    [JsonPropertyName("materiaID")]
    public int MateriaId { get; set; }
}
