using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.FFXIVMT;

/// <summary>
/// Top-level response from the FFXIVMT Gilflux API endpoint.
/// Note: The "data" field may be either a JSON-encoded string or a raw JSON object,
/// so it is captured as a JsonElement for flexible parsing.
/// </summary>
public sealed class GilfluxResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Contains a dictionary of GilfluxItem objects.
    /// May be a JSON object or a JSON-encoded string — handled as JsonElement for flexibility.
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    /// <summary>
    /// Timeframe labels mapped to their duration in ms.
    /// e.g. {"7d":604800000,"3d":259200000,"1d":86400000,"12h":43200000,"6h":21600000,"3h":10800000,"1h":3600000}
    /// May be a JSON object or a JSON-encoded string — handled as JsonElement for flexibility.
    /// The keys define which ranking columns should be displayed.
    /// </summary>
    [JsonPropertyName("gilflux_timeframe_in_ms")]
    public JsonElement? GilfluxTimeframeInMs { get; set; }

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
    /// Ordered list of timeframe labels (e.g. ["7d", "3d", "1d", "12h", "6h", "3h", "1h"]).
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
    public int WorldId { get; set; }

    [JsonPropertyName("world_name")]
    public string? WorldName { get; set; }

    [JsonPropertyName("datacenter")]
    public string? Datacenter { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("ranking_alltime")]
    public long RankingAllTime { get; set; }

    [JsonPropertyName("ranking_1h")]
    public long Ranking1h { get; set; }

    [JsonPropertyName("ranking_3h")]
    public long Ranking3h { get; set; }

    [JsonPropertyName("ranking_6h")]
    public long Ranking6h { get; set; }

    [JsonPropertyName("ranking_12h")]
    public long Ranking12h { get; set; }

    [JsonPropertyName("ranking_1d")]
    public long Ranking1d { get; set; }

    [JsonPropertyName("ranking_3d")]
    public long Ranking3d { get; set; }

    [JsonPropertyName("ranking_7d")]
    public long Ranking7d { get; set; }

    [JsonPropertyName("updated_at")]
    public long UpdatedAt { get; set; }

    [JsonPropertyName("last_sale_time")]
    public long LastSaleTime { get; set; }

    /// <summary>
    /// Additional ranking values keyed by timeframe label (e.g. "7d", "1h").
    /// Populated from the JSON via the overflow extension data handler.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Retrieves the ranking value for a given timeframe label.
    /// Checks the typed properties first, then falls back to extension data.
    /// </summary>
    public long GetRanking(string label)
    {
        // Fast path: match known typed properties
        return label switch
        {
            "1h" => Ranking1h,
            "3h" => Ranking3h,
            "6h" => Ranking6h,
            "12h" => Ranking12h,
            "1d" => Ranking1d,
            "3d" => Ranking3d,
            "7d" => Ranking7d,
            "alltime" => RankingAllTime,
            _ => GetRankingFromExtensionData(label)
        };
    }

    private long GetRankingFromExtensionData(string label)
    {
        // Try "ranking_<label>" key in extension data (captures any new timeframes the backend adds)
        if (ExtensionData != null && ExtensionData.TryGetValue($"ranking_{label}", out var element))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var val))
                return val;
        }
        return 0;
    }
}
