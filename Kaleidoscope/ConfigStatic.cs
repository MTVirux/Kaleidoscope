namespace Kaleidoscope;

public static class ConfigStatic
{
    public static readonly Vector2 DefaultWindowPosition = new(100, 100);
    public static readonly Vector2 DefaultWindowSize = new(800, 600);
    public static readonly Vector2 MinimumWindowSize = new(250, 180);

    public const int CurrencyTrackerMaxSamples = 200;
    public const float CurrencyTrackerStartingValue = 100000f;
    public const float CurrencyTrackerMaxGil = 999_999_999f;
    public static readonly Vector2 CurrencyTrackerToolSize = new(360, 220);
    public static readonly Vector2 CurrencyTrackerPointsPopupSize = new(700, 300);

    /// <summary>
    /// Delay (ms) after player inventory changes before reading values.
    /// Allows multiple rapid changes (trades, purchases) to batch together.
    /// </summary>
    public const int PlayerInventoryStabilizationDelayMs = 500;
    
    /// <summary>
    /// Delay (ms) after retainer state changes before reading inventory values.
    /// Allows the game client to fully load retainer data from the server.
    /// </summary>
    public const int RetainerStabilizationDelayMs = 500;

    public const float ComparisonEpsilon = 0.0001f;
    public const int TextInputBufferSize = 128;
    public const float MaxDragDelta = 2000f;
    public const int MaxGridLines = 1024;
    
    public const float MinToolWidth = 50f;
    
    public const int InventoryDebounceMs = 100;
    public const int ValueCheckIntervalMs = 1000;
    
    /// <summary>Base item ID for elemental crystals (Fire Shard = 2).</summary>
    public const int CrystalBaseItemId = 2;
    /// <summary>Offset between crystal tiers (Shard=0, Crystal=6, Cluster=12).</summary>
    public const int CrystalTierOffset = 6;
    
    public const int BaseGridColumns = 16;
    public const int BaseGridRows = 9;
    
    public const int SeriesCacheExpirySeconds = 2;
    public const int InventoryValueCacheSeconds = 30;
    public const int ListingsCacheSeconds = 300;
}
