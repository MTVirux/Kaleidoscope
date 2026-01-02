namespace Kaleidoscope;

/// <summary>
/// Layout type for windowed vs fullscreen mode.
/// </summary>
public enum LayoutType
{
    Windowed = 0,
    Fullscreen = 1
}

/// <summary>
/// Scope for Universalis market data queries.
/// </summary>
public enum UniversalisScope
{
    /// <summary>Query data for a specific world only.</summary>
    World = 0,
    /// <summary>Query data for the entire data center.</summary>
    DataCenter = 1,
    /// <summary>Query data for the entire region.</summary>
    Region = 2
}

/// <summary>
/// Format for displaying character names.
/// </summary>
public enum CharacterNameFormat
{
    /// <summary>Display full name (e.g., "John Smith").</summary>
    FullName = 0,
    /// <summary>Display first name only (e.g., "John").</summary>
    FirstNameOnly = 1,
    /// <summary>Display last name only (e.g., "Smith").</summary>
    LastNameOnly = 2,
    /// <summary>Display initials (e.g., "J.S.").</summary>
    Initials = 3
}

/// <summary>
/// Sort order for character lists.
/// </summary>
public enum CharacterSortOrder
{
    /// <summary>Sort characters alphabetically by name (A-Z).</summary>
    Alphabetical = 0,
    /// <summary>Sort characters in reverse alphabetical order (Z-A).</summary>
    ReverseAlphabetical = 1,
    /// <summary>Sort characters in the order returned by AutoRetainer.</summary>
    AutoRetainer = 2
}

/// <summary>
/// Crystal element types for filtering.
/// </summary>
public enum CrystalElement
{
    Fire = 0,
    Ice = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Water = 5
}

/// <summary>
/// Crystal tier types for filtering.
/// </summary>
public enum CrystalTier
{
    Shard = 0,
    Crystal = 1,
    Cluster = 2
}

/// <summary>
/// How to group crystal data in the display.
/// </summary>
public enum CrystalGrouping
{
    /// <summary>Show total crystals as a single value.</summary>
    None = 0,
    /// <summary>Show separate lines/values per character.</summary>
    ByCharacter = 1,
    /// <summary>Show separate lines/values per element (Fire, Ice, etc.).</summary>
    ByElement = 2,
    /// <summary>Show separate lines/values per element per character.</summary>
    ByCharacterAndElement = 3,
    /// <summary>Show separate lines/values per tier (Shard, Crystal, Cluster).</summary>
    ByTier = 4,
    /// <summary>Show separate lines/values per tier per character.</summary>
    ByCharacterAndTier = 5
}

/// <summary>
/// View mode for the unified Data Tool.
/// </summary>
public enum DataToolViewMode
{
    /// <summary>Display data as a table with characters as rows and items as columns.</summary>
    Table = 0,
    /// <summary>Display data as a time-series graph.</summary>
    Graph = 1
}

/// <summary>
/// Categories for filtering debug/verbose log output.
/// Each category corresponds to a major subsystem or service.
/// </summary>
[Flags]
public enum LogCategory
{
    /// <summary>No categories enabled.</summary>
    None = 0,

    /// <summary>Database operations (SQLite queries, migrations, etc.).</summary>
    Database = 1 << 0,

    /// <summary>Time-series and data cache operations.</summary>
    Cache = 1 << 1,

    /// <summary>Game state access (inventory, retainers, currencies).</summary>
    GameState = 1 << 2,

    /// <summary>Price tracking and Universalis data storage.</summary>
    PriceTracking = 1 << 3,

    /// <summary>Universalis API and WebSocket communication.</summary>
    Universalis = 1 << 4,

    /// <summary>AutoRetainer IPC integration.</summary>
    AutoRetainer = 1 << 5,

    /// <summary>Currency and data tracking service.</summary>
    CurrencyTracker = 1 << 6,

    /// <summary>Inventory caching and scanning.</summary>
    Inventory = 1 << 7,

    /// <summary>Character data and name resolution.</summary>
    Character = 1 << 8,

    /// <summary>Layout persistence and editing.</summary>
    Layout = 1 << 9,

    /// <summary>UI rendering and tool components.</summary>
    UI = 1 << 10,

    /// <summary>Market listings service.</summary>
    Listings = 1 << 11,

    /// <summary>Configuration and settings.</summary>
    Config = 1 << 12,

    /// <summary>All categories enabled.</summary>
    All = ~None
}
