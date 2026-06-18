namespace Kaleidoscope;

public enum LayoutType
{
    Windowed = 0,
    Fullscreen = 1
}

/// <summary>
/// Determines how tools are automatically arranged within a layout.
/// Grid is the default manual grid-snap mode; other values trigger auto-layout.
/// </summary>
public enum LayoutArrangement
{
    /// <summary>Manual grid-snap placement (default, existing behavior).</summary>
    Grid = 0,
    /// <summary>Tools stacked vertically, each spanning full width.</summary>
    SingleColumn,
    /// <summary>Tools fill two equal-width columns left-to-right.</summary>
    TwoColumn,
    /// <summary>Tools fill three equal-width columns.</summary>
    ThreeColumn,
    /// <summary>First half of tools on top, second half on bottom.</summary>
    SplitHorizontal,
    /// <summary>First half of tools on left, second half on right.</summary>
    SplitVertical,
    /// <summary>First tool spans full width as header; remaining tools fill a grid below.</summary>
    Dashboard,
}

public enum UniversalisScope
{
    World = 0,
    DataCenter = 1,
    Region = 2
}

public enum CharacterNameFormat
{
    FullName = 0,
    FirstNameOnly = 1,
    LastNameOnly = 2,
    Initials = 3
}

public enum CharacterSortOrder
{
    Alphabetical = 0,
    ReverseAlphabetical = 1,
    AutoRetainer = 2
}

public enum CrystalElement
{
    Fire = 0,
    Ice = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Water = 5
}

public enum CrystalTier
{
    Shard = 0,
    Crystal = 1,
    Cluster = 2
}

public enum DataToolViewMode
{
    Table = 0,
    Graph = 1
}

/// <summary>Destination for profiler slow-operation log lines.</summary>
public enum ProfilerLogTarget
{
    /// <summary>Kaleidoscope-owned rotating file only (default; no dalamud.log contribution).</summary>
    File = 0,
    /// <summary>Dalamud's IPluginLog (dalamud.log + /xllog).</summary>
    PluginLog = 1,
    /// <summary>Both the Kaleidoscope file and IPluginLog.</summary>
    Both = 2,
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

    /// <summary>Lifestream IPC integration.</summary>
    Lifestream = 1 << 13,

    /// <summary>FFXIVMT API communication (Gilflux, etc.).</summary>
    FFXIVMT = 1 << 14,

    /// <summary>All categories enabled.</summary>
    All = ~None
}
