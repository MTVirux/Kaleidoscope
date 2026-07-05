using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.FFXIVMT;

/// <summary>
/// Top-level response from the FFXIVMT Gilflux API endpoint.
/// </summary>
public sealed class GilfluxResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>One entry per (item, world) pair; aggregation across worlds is client-side.</summary>
    [JsonPropertyName("data")]
    public List<GilfluxItem>? Data { get; set; }

    /// <summary>
    /// Timeframe labels mapped to their duration in ms.
    /// e.g. {"7d":604800000,"3d":259200000,"1d":86400000,"12h":43200000,"6h":21600000,"3h":10800000,"1h":3600000}
    /// The keys define which ranking columns should be displayed.
    /// </summary>
    [JsonPropertyName("gilflux_timeframe_in_ms")]
    public Dictionary<string, long>? GilfluxTimeframeInMs { get; set; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }
}

/// <summary>
/// Processed result from the Gilflux API, containing both items and the dynamic timeframe definitions.
/// </summary>
public sealed class GilfluxResult
{
    /// <summary>The ranked items returned by the API.</summary>
    public List<GilfluxItem> Items { get; init; } = new();

    /// <summary>
    /// Ordered list of timeframe labels (e.g. ["1h", "3h", "6h", "12h", "1d", "3d", "7d"]).
    /// Derived from the gilflux_timeframe_in_ms field. Defines which columns to show.
    /// </summary>
    public List<string> TimeframeLabels { get; init; } = new();

    /// <summary>
    /// Maps timeframe labels to their durations, parsed from gilflux_timeframe_in_ms.
    /// Used for bucket-aware assignment of live sales.
    /// </summary>
    public Dictionary<string, TimeSpan> TimeframeDurations { get; init; } = new();
}

/// <summary>
/// A single item entry from the Gilflux ranking data.
/// </summary>
public sealed class GilfluxItem
{
    [JsonPropertyName("item_id")]
    public int ItemId { get; set; }

    [JsonPropertyName("item_name")]
    public string? ItemName { get; set; }

    [JsonPropertyName("world_id")]
    public int? WorldId { get; set; }

    [JsonPropertyName("world_name")]
    public string? WorldName { get; set; }

    [JsonPropertyName("datacenter")]
    public string? Datacenter { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    /// <summary>Gil moved per timeframe, keyed by timeframe label (e.g. "1h", "7d").</summary>
    [JsonPropertyName("rankings")]
    public Dictionary<string, long> Rankings { get; set; } = new();

    /// <summary>Epoch millis of the last ranking refresh; null if never refreshed.</summary>
    [JsonPropertyName("updated_at")]
    public long? UpdatedAt { get; set; }

    /// <summary>Epoch millis of the most recent sale used for the ranking; null if no sales yet.</summary>
    [JsonPropertyName("last_sale_time")]
    public long? LastSaleTime { get; set; }

    /// <summary>Retrieves the ranking value for a given timeframe label, or 0 if absent.</summary>
    public long GetRanking(string label)
        => Rankings.TryGetValue(label, out var value) ? value : 0;
}
