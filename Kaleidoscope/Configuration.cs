using Dalamud.Configuration;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using MTGui.Common;
using MTGui.Graph;

namespace Kaleidoscope;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ShowOnStart { get; set; } = true;
    public bool ExclusiveFullscreen { get; set; } = false;
    public bool PinMainWindow { get; set; } = false;
    public bool PinConfigWindow { get; set; } = false;

    /// <summary>
    /// When enabled, the Kaleidoscope window stays visible during cutscenes and GPose.
    /// This overrides Dalamud's "Hide plugin UI during cutscenes" setting for this plugin.
    /// </summary>
    public bool ShowDuringCutscenes { get; set; } = true;

    public bool ProfilerEnabled { get; set; } = false;

    public bool ProfilerLogSlowOperations { get; set; } = true;

    /// <summary>
    /// Slow operation threshold in milliseconds. Operations exceeding this will be logged.
    /// </summary>
    public double ProfilerSlowOperationThresholdMs { get; set; } = 5.0;

    /// <summary>
    /// Which stats view to show in the profiler (0=Basic, 1=Percentiles, 2=Rolling, 3=All).
    /// </summary>
    public int ProfilerStatsView { get; set; } = 0;

    public bool ProfilerShowHistogram { get; set; } = false;

    public bool ProfilerShowChildScopes { get; set; } = true;

    public bool FrameLimiterEnabled { get; set; } = false;

    public int FrameLimiterTargetFps { get; set; } = 60;

    /// <summary>
    /// Whether the user has explicitly selected Custom mode for frame limiting.
    /// When true, the dropdown shows Custom even if the FPS matches a preset.
    /// </summary>
    public bool FrameLimiterUseCustom { get; set; } = false;

    /// <summary>
    /// Whether developer mode stays visible without holding CTRL+ALT.
    /// </summary>
    public bool DeveloperModeEnabled { get; set; } = false;

    /// <summary>
    /// Enabled log categories for verbose/debug output filtering.
    /// Only logs matching enabled categories will be emitted.
    /// </summary>
    public LogCategory EnabledLogCategories { get; set; } = LogCategory.None;

    /// <summary>
    /// Whether category-based log filtering is active.
    /// When false, all logs pass through regardless of category.
    /// </summary>
    public bool LogCategoryFilteringEnabled { get; set; } = false;

    /// <summary>
    /// Whether to write logs to an external file in the plugin directory.
    /// </summary>
    public bool FileLoggingEnabled { get; set; } = false;

    /// <summary>
    /// Maximum size of the log file in megabytes before rotation.
    /// </summary>
    public int FileLoggingMaxSizeMB { get; set; } = 10;

    public bool FileLoggingIncludeTimestamps { get; set; } = true;

    /// <summary>
    /// Custom directory for log files. If empty, uses the default plugin config directory.
    /// </summary>
    public string FileLoggingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Whether to split log files by category (e.g., kaleidoscope_database.log, kaleidoscope_ui.log).
    /// When enabled, each log category writes to its own file.
    /// </summary>
    public bool FileLoggingSplitByCategory { get; set; } = false;

    /// <summary>
    /// Whether to split log files by character (e.g., logs/FirstName LastName/kaleidoscope.log).
    /// When enabled, logs are organized into character-specific subdirectories where applicable.
    /// </summary>
    public bool FileLoggingSplitByCharacter { get; set; } = false;

    /// <summary>
    /// Format for displaying character names throughout the UI.
    /// </summary>
    public CharacterNameFormat CharacterNameFormat { get; set; } = CharacterNameFormat.FullName;

    public CharacterSortOrder CharacterSortOrder { get; set; } = CharacterSortOrder.Alphabetical;
    
    /// <summary>
    /// Sort order for item lists in item pickers (alphabetical or by ID).
    /// </summary>
    public Gui.Widgets.ItemSortOrder ItemPickerSortOrder { get; set; } = Gui.Widgets.ItemSortOrder.Alphabetical;

    public Vector2 MainWindowPos { get; set; } = new(100, 100);
    public Vector2 MainWindowSize { get; set; } = new(600, 400);
    public Vector2 ConfigWindowPos { get; set; } = new(100, 100);
    public Vector2 ConfigWindowSize { get; set; } = new(600, 400);

    public Vector4 MainWindowBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    public Vector4 FullscreenBackgroundColor { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);

    public UIColors UIColors { get; set; } = new();

    public float ContentGridCellWidthPercent { get; set; } = 25f;
    public float ContentGridCellHeightPercent { get; set; } = 25f;
    public int GridSubdivisions { get; set; } = 8;
    public bool EditMode { get; set; } = false;

    public List<ContentLayoutState> Layouts { get; set; } = new();
    public string ActiveWindowedLayoutName { get; set; } = string.Empty;
    public string ActiveFullscreenLayoutName { get; set; } = string.Empty;

    /// <summary>
    /// When enabled, layout changes are automatically saved without requiring manual save.
    /// </summary>
    public bool AutoSaveLayoutChanges { get; set; } = false;

    /// <summary>
    /// The scope for Universalis market data queries (World, DataCenter, or Region).
    /// </summary>
    public UniversalisScope UniversalisQueryScope { get; set; } = UniversalisScope.DataCenter;

    /// <summary>
    /// Override world name for Universalis queries. If empty, uses current character's world.
    /// </summary>
    public string UniversalisWorldOverride { get; set; } = string.Empty;

    /// <summary>
    /// Override data center name for Universalis queries. If empty, uses current character's DC.
    /// </summary>
    public string UniversalisDataCenterOverride { get; set; } = string.Empty;

    /// <summary>
    /// Override region name for Universalis queries. If empty, uses current character's region.
    /// </summary>
    public string UniversalisRegionOverride { get; set; } = string.Empty;

    /// <summary>
    /// Set of item IDs that have historical time-series tracking enabled.
    /// When an item is in this set, its quantities are sampled and stored for graphing over time.
    /// This is a global setting - enabling tracking for an item affects all tools.
    /// </summary>
    public HashSet<uint> ItemsWithHistoricalTracking { get; set; } = new();

    /// <summary>
    /// Set of enabled data types for tracking. If null or empty, defaults will be used.
    /// </summary>
    public HashSet<TrackedDataType> EnabledTrackedDataTypes { get; set; } = new()
    {
        TrackedDataType.Gil,
        TrackedDataType.TomestonePoetics,
        TrackedDataType.TomestoneCapped,
        TrackedDataType.OrangeCraftersScrip,
        TrackedDataType.OrangeGatherersScrip,
        TrackedDataType.SackOfNuts,
        TrackedDataType.Ventures
    };

    /// <summary>
    /// Custom colors for each tracked data type. Used across all tools for consistent coloring.
    /// Stored as ABGR uint format.
    /// </summary>
    public Dictionary<TrackedDataType, uint> ItemColors { get; set; } = new();

    /// <summary>
    /// Custom colors for game items (keyed by item ID). Used in Item Table and other tools.
    /// Stored as ABGR uint format.
    /// </summary>
    public Dictionary<uint, uint> GameItemColors { get; set; } = new();

    /// <summary>
    /// Favorite item IDs for quick access in item selectors.
    /// </summary>
    public HashSet<uint> FavoriteItems { get; set; } = new();

    /// <summary>
    /// Favorite currency types (TrackedDataType) for quick access.
    /// </summary>
    public HashSet<TrackedDataType> FavoriteCurrencies { get; set; } = new();

    /// <summary>
    /// Favorite character IDs for quick access in character selectors.
    /// </summary>
    public HashSet<ulong> FavoriteCharacters { get; set; } = new();

    public PriceTrackingSettings PriceTracking { get; set; } = new();

    public WebsocketFeedSettings WebsocketFeed { get; set; } = new();

    public InventoryValueSettings InventoryValue { get; set; } = new();

    public ItemTableSettings ItemTable { get; set; } = new();
    
    public ItemGraphSettings ItemGraph { get; set; } = new();

    public List<UserToolPreset> UserToolPresets { get; set; } = new();

    public TimeSeriesCacheConfig TimeSeriesCacheConfig { get; set; } = new();

    public MTGraphStyleConfig GraphStyle { get; set; } = new();
    
    /// <summary>
    /// Default number format for table widgets.
    /// New tables will inherit this setting.
    /// </summary>
    public NumberFormatConfig DefaultTableNumberFormat { get; set; } = new();
    
    /// <summary>
    /// Default number format for graph widgets.
    /// New graphs will inherit this setting.
    /// </summary>
    public NumberFormatConfig DefaultGraphNumberFormat { get; set; } = new();
}
