namespace Kaleidoscope;

/// <summary>
/// Compile-time constants for UI layout and configuration.
/// </summary>
public static class ConfigStatic
{
    // Window defaults
    public static readonly Vector2 DefaultWindowPosition = new(100, 100);
    public static readonly Vector2 DefaultWindowSize = new(800, 600);
    public static readonly Vector2 MinimumWindowSize = new(250, 180);

    // Currency tracker defaults
    public const int CurrencyTrackerMaxSamples = 200;
    public const float CurrencyTrackerStartingValue = 100000f;
    public const float CurrencyTrackerMaxGil = 999_999_999f;
    public static readonly Vector2 CurrencyTrackerToolSize = new(360, 220);
    public static readonly Vector2 CurrencyTrackerPointsPopupSize = new(700, 300);

    // Inventory data stabilization
    /// <summary>
    /// Delay in milliseconds to wait after player inventory changes before reading values.
    /// This allows multiple rapid changes (e.g., trades, purchases) to batch together.
    /// </summary>
    public const int PlayerInventoryStabilizationDelayMs = 500;
    
    /// <summary>
    /// Delay in milliseconds to wait after retainer state changes before reading inventory values.
    /// This allows the game client to fully load retainer data from the server.
    /// </summary>
    public const int RetainerStabilizationDelayMs = 500;

    // UI constants
    public const float FloatEpsilon = 0.0001f;
    public const int TextInputBufferSize = 128;
    public const float MaxDragDelta = 2000f;
    public const int MaxGridLines = 1024;
    
    // Tool component size constraints
    public const float MinToolWidth = 50f;
    // MinToolHeight is calculated dynamically based on text line height
    
    // Inventory change detection
    /// <summary>Debounce interval for inventory change events.</summary>
    public const int InventoryDebounceMs = 100;
    /// <summary>Interval between periodic value checks.</summary>
    public const int ValueCheckIntervalMs = 1000;
    
    // Crystal item ID calculation
    /// <summary>Base item ID for elemental crystals (Fire Shard = 2).</summary>
    public const int CrystalBaseItemId = 2;
    /// <summary>Offset between crystal tiers (Shard=0, Crystal=6, Cluster=12).</summary>
    public const int CrystalTierOffset = 6;
    
    // Grid layout
    /// <summary>Base number of columns for grid calculations.</summary>
    public const int BaseGridColumns = 16;
    /// <summary>Base number of rows for grid calculations.</summary>
    public const int BaseGridRows = 9;
    
    // Cache timing
    /// <summary>Cache validity period for time-series data helpers in seconds.</summary>
    public const int SeriesCacheExpirySeconds = 2;
    /// <summary>Cache validity period for inventory value calculations in seconds.</summary>
    public const int InventoryValueCacheSeconds = 30;
    /// <summary>Default cache duration for price listings in seconds.</summary>
    public const int ListingsCacheSeconds = 300;
}
