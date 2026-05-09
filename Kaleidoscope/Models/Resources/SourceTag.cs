namespace Kaleidoscope.Models.Resources;

/// <summary>
/// Origin classification for a resource_history row. Detector services stamp the next
/// observation(s) with one of these values; RecordObservation consumes & clears the tag.
/// </summary>
public enum SourceKind : int
{
    Unknown          = 0,
    DutyReward       = 100,
    RetainerVenture  = 200,
    Trade            = 300,
    GoldSaucer       = 400,
    MobDrop          = 500,
    LetterAttachment = 600,
    MarketSale       = 700,
    Vendor           = 800,
    Quest            = 900,
    Crafting         = 1000,
}

/// <summary>
/// Pending source attribution. Set by detectors with a TTL; consumed by RecordObservation.
/// </summary>
public readonly record struct SourceTag
{
    public SourceKind Kind { get; init; }

    /// <summary>Free-form context — duty name, retainer venture id, trade partner name, etc.</summary>
    public string? Detail { get; init; }

    /// <summary>UTC timestamp at which this tag was stamped. Used for TTL expiry.</summary>
    public DateTime StampedAt { get; init; }
}
