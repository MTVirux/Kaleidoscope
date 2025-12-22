using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Market tax rates response from Universalis API.
/// GET /api/v2/tax-rates?world={world}
/// </summary>
public sealed class TaxRates
{
    /// <summary>The percent retainer tax in Limsa Lominsa.</summary>
    [JsonPropertyName("Limsa Lominsa")]
    public int LimsaLominsa { get; set; }

    /// <summary>The percent retainer tax in Gridania.</summary>
    [JsonPropertyName("Gridania")]
    public int Gridania { get; set; }

    /// <summary>The percent retainer tax in Ul'dah.</summary>
    [JsonPropertyName("Ul'dah")]
    public int Uldah { get; set; }

    /// <summary>The percent retainer tax in Ishgard.</summary>
    [JsonPropertyName("Ishgard")]
    public int Ishgard { get; set; }

    /// <summary>The percent retainer tax in Kugane.</summary>
    [JsonPropertyName("Kugane")]
    public int Kugane { get; set; }

    /// <summary>The percent retainer tax in the Crystarium.</summary>
    [JsonPropertyName("Crystarium")]
    public int Crystarium { get; set; }

    /// <summary>The percent retainer tax in Old Sharlayan.</summary>
    [JsonPropertyName("Old Sharlayan")]
    public int OldSharlayan { get; set; }

    /// <summary>The percent retainer tax in Tuliyollal.</summary>
    [JsonPropertyName("Tuliyollal")]
    public int Tuliyollal { get; set; }
}
