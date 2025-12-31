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
