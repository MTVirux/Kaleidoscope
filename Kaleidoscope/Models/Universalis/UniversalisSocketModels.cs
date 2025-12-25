using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// WebSocket event types from Universalis.
/// </summary>
public static class UniversalisSocketEvents
{
    public const string ListingsAdd = "listings/add";
    public const string ListingsRemove = "listings/remove";
    public const string SalesAdd = "sales/add";
}

/// <summary>
/// Base class for WebSocket messages from Universalis.
/// Messages are received in BSON format.
/// </summary>
public abstract class UniversalisSocketMessage
{
    /// <summary>The event type (e.g., "listings/add", "sales/add").</summary>
    [JsonPropertyName("event")]
    public string? Event { get; set; }
}

/// <summary>
/// WebSocket message for new listings.
/// </summary>
public sealed class ListingsAddMessage : UniversalisSocketMessage
{
    [JsonPropertyName("item")]
    public int ItemId { get; set; }

    [JsonPropertyName("world")]
    public int WorldId { get; set; }

    [JsonPropertyName("listings")]
    public List<WebSocketListing>? Listings { get; set; }
}

/// <summary>
/// WebSocket message for removed listings.
/// </summary>
public sealed class ListingsRemoveMessage : UniversalisSocketMessage
{
    [JsonPropertyName("item")]
    public int ItemId { get; set; }

    [JsonPropertyName("world")]
    public int WorldId { get; set; }

    [JsonPropertyName("listings")]
    public List<WebSocketListing>? Listings { get; set; }
}

/// <summary>
/// WebSocket message for new sales.
/// </summary>
public sealed class SalesAddMessage : UniversalisSocketMessage
{
    [JsonPropertyName("item")]
    public int ItemId { get; set; }

    [JsonPropertyName("world")]
    public int WorldId { get; set; }

    [JsonPropertyName("sales")]
    public List<WebSocketSale>? Sales { get; set; }
}

/// <summary>
/// Listing data from WebSocket messages.
/// </summary>
public sealed class WebSocketListing
{
    [JsonPropertyName("listingID")]
    public string? ListingId { get; set; }

    [JsonPropertyName("pricePerUnit")]
    public int PricePerUnit { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("hq")]
    public bool Hq { get; set; }

    [JsonPropertyName("retainerName")]
    public string? RetainerName { get; set; }

    [JsonPropertyName("retainerCity")]
    public int RetainerCity { get; set; }

    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    [JsonPropertyName("worldID")]
    public int? WorldId { get; set; }

    [JsonPropertyName("lastReviewTime")]
    public long LastReviewTime { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("tax")]
    public int Tax { get; set; }
}

/// <summary>
/// Sale data from WebSocket messages.
/// </summary>
public sealed class WebSocketSale
{
    [JsonPropertyName("pricePerUnit")]
    public int PricePerUnit { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("hq")]
    public bool Hq { get; set; }

    [JsonPropertyName("buyerName")]
    public string? BuyerName { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    [JsonPropertyName("worldID")]
    public int? WorldId { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// Represents a live price feed entry for display.
/// </summary>
public sealed class PriceFeedEntry
{
    /// <summary>When this entry was received.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>The event type.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The item ID.</summary>
    public int ItemId { get; set; }

    /// <summary>The world ID.</summary>
    public int WorldId { get; set; }

    /// <summary>The world name.</summary>
    public string? WorldName { get; set; }

    /// <summary>Price per unit.</summary>
    public int PricePerUnit { get; set; }

    /// <summary>Quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Whether the item is HQ.</summary>
    public bool IsHq { get; set; }

    /// <summary>Total price.</summary>
    public int Total { get; set; }

    /// <summary>Buyer name (for sales).</summary>
    public string? BuyerName { get; set; }

    /// <summary>Retainer name (for listings).</summary>
    public string? RetainerName { get; set; }

    /// <summary>Whether this sale was from a mannequin.</summary>
    public bool OnMannequin { get; set; }
}
