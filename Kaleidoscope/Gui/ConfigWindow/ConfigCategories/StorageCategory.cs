using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using MTGui.Widgets.DatePicker;
using OtterGui.Classes;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Data type mode for cleanup operations.
/// </summary>
public enum CleanupDataMode
{
    Currencies,
    Items
}

/// <summary>
/// Storage &amp; Cache configuration category in the config window.
/// Centralizes all database and memory cache size settings with detailed tooltips.
/// </summary>
public sealed class StorageCategory : IDisposable
{
    private readonly ConfigurationService _configService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ITextureProvider _textureProvider;
    private readonly IDataManager _dataManager;
    private readonly FavoritesService _favoritesService;
    private readonly MessageService _messageService;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    
    // Data Cleanup Section state
    private CleanupDataMode _cleanupDataMode = CleanupDataMode.Currencies;
    private MTCurrencyComboDropdown? _cleanupCurrencyCombo;
    private MTItemComboDropdown? _cleanupItemCombo;
    private MTCharacterCombo? _cleanupCharacterCombo;
    private MTDatePickerWidget? _startDatePicker;
    private MTDatePickerWidget? _endDatePicker;
    private List<(ulong characterId, DateTime timestamp, long value)> _previewPoints = new();
    private int _previewCurrentPage;
    private const int PreviewPageSize = 50;
    private bool _backupBeforeDelete;
    private bool _vacuumAfterDelete;
    private bool _deleteConfirmationOpen;
    private (int count, long estimatedBytes) _deleteStats;
    private string? _lastBackupPath;
    private int _lastDeletedCount;
    private bool _showDeleteResult;

    public StorageCategory(
        ConfigurationService configService,
        CurrencyTrackerService currencyTrackerService,
        ITextureProvider textureProvider,
        IDataManager dataManager,
        FavoritesService favoritesService,
        MessageService messageService,
        AutoRetainerIpcService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
    {
        _configService = configService;
        _currencyTrackerService = currencyTrackerService;
        _textureProvider = textureProvider;
        _dataManager = dataManager;
        _favoritesService = favoritesService;
        _messageService = messageService;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        
        InitializeCleanupWidgets();
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Storage & Cache Settings");
        ImGui.Separator();
        ImGui.TextWrapped("Configure database and memory cache sizes. Larger caches improve performance but use more RAM.");
        ImGui.Spacing();

        DrawDatabaseSection();
        DrawMemoryCacheSection();
        DrawPriceDataRetentionSection();
        DrawLiveFeedSection();
        DrawDataCleanupSection();
    }
    
    public void Dispose()
    {
        _cleanupCurrencyCombo?.Dispose();
        _cleanupItemCombo?.Dispose();
        _cleanupCharacterCombo?.Dispose();
    }
    
    private void InitializeCleanupWidgets()
    {
        // Initialize currency combo
        _cleanupCurrencyCombo = new MTCurrencyComboDropdown(
            _textureProvider,
            _currencyTrackerService.Registry,
            _favoritesService,
            "##cleanupCurrency",
            null,
            false);
        
        // Initialize item combo
        _cleanupItemCombo = new MTItemComboDropdown(
            _textureProvider,
            _dataManager,
            _favoritesService,
            null,
            "##cleanupItem",
            marketableOnly: false,
            configService: _configService,
            trackedDataRegistry: _currencyTrackerService.Registry,
            excludeCurrencies: true,
            multiSelect: false);
        
        // Initialize character combo
        _cleanupCharacterCombo = new MTCharacterCombo(
            _currencyTrackerService,
            _favoritesService,
            _configService,
            "##cleanupCharacter",
            false,
            _autoRetainerService,
            _priceTrackingService);
        
        // Initialize date pickers with default range (last 30 days to now)
        var now = DateTime.Now;
        _startDatePicker = new MTDatePickerWidget("##cleanupStartDate", true, now.AddDays(-30));
        _endDatePicker = new MTDatePickerWidget("##cleanupEndDate", true, now);
        
        // Subscribe to selection changes to refresh preview
        _cleanupCurrencyCombo.SelectionChanged += _ => RefreshPreview();
        _cleanupItemCombo.SelectionChanged += _ => RefreshPreview();
        _cleanupCharacterCombo.SelectionChanged += (_, _) => RefreshPreview();
        _startDatePicker.DateTimeChanged += _ => RefreshPreview();
        _endDatePicker.DateTimeChanged += _ => RefreshPreview();
    }
    
    /// <summary>
    /// Gets the variable name for the currently selected data type based on mode.
    /// For items, only returns the player inventory variable (use GetSelectedVariableNames for all).
    /// </summary>
    private string? GetSelectedVariableName()
    {
        return _cleanupDataMode switch
        {
            CleanupDataMode.Currencies when _cleanupCurrencyCombo?.SelectedType != default
                => _cleanupCurrencyCombo.SelectedType.ToString(),
            CleanupDataMode.Items when _cleanupItemCombo?.SelectedItemId > 0
                => InventoryCacheService.GetItemVariableName(_cleanupItemCombo.SelectedItemId),
            _ => null
        };
    }
    
    /// <summary>
    /// Gets all variable names for the currently selected data type.
    /// For items, this includes both player inventory and retainer inventory variables.
    /// </summary>
    private List<string> GetSelectedVariableNames()
    {
        var result = new List<string>();
        
        switch (_cleanupDataMode)
        {
            case CleanupDataMode.Currencies when _cleanupCurrencyCombo?.SelectedType != default:
                result.Add(_cleanupCurrencyCombo.SelectedType.ToString());
                break;
                
            case CleanupDataMode.Items when _cleanupItemCombo?.SelectedItemId > 0:
                var itemId = _cleanupItemCombo.SelectedItemId;
                result.Add(InventoryCacheService.GetItemVariableName(itemId));
                result.Add(InventoryCacheService.GetRetainerItemVariableName(itemId));
                break;
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets a display-friendly name for the currently selected data type.
    /// </summary>
    private string GetSelectedDisplayName()
    {
        return _cleanupDataMode switch
        {
            CleanupDataMode.Currencies when _cleanupCurrencyCombo?.SelectedType != default
                => _cleanupCurrencyCombo.SelectedType.ToString(),
            CleanupDataMode.Items when _cleanupItemCombo?.SelectedItemId > 0
                => _cleanupItemCombo.SelectedItem?.Name ?? $"Item {_cleanupItemCombo.SelectedItemId}",
            _ => "None"
        };
    }
    
    /// <summary>
    /// Checks if a valid data type is selected based on current mode.
    /// </summary>
    private bool HasValidSelection()
    {
        return _cleanupDataMode switch
        {
            CleanupDataMode.Currencies => _cleanupCurrencyCombo?.SelectedType != default,
            CleanupDataMode.Items => _cleanupItemCombo?.SelectedItemId > 0,
            _ => false
        };
    }

    private void DrawDatabaseSection()
    {
        if (!ImGui.CollapsingHeader("SQLite Database##StorageDb", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();

        var currencyTrackerConfig = _configService.CurrencyTrackerConfig;
        var cacheSizeMb = currencyTrackerConfig.DatabaseCacheSizeMb;

        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("Page Cache Size (MB)##DbCache", ref cacheSizeMb, 1, 512))
        {
            currencyTrackerConfig.DatabaseCacheSizeMb = cacheSizeMb;
            _configService.Save();
        }
        DrawHelpMarker(
            "SQLite page cache stored in RAM per database connection.\n\n" +
            "• Higher values: Faster database reads, more RAM usage\n" +
            "• Lower values: Slower reads, less RAM usage\n\n" +
            "The plugin uses 2 database connections, so total RAM usage\n" +
            "is approximately this value × 2.\n\n" +
            "Recommended: 4-16 MB for most users.\n" +
            "High-end: 32-64 MB for large datasets.\n\n" +
            "⚠ Requires plugin reload to take effect.");

        var totalCacheMb = cacheSizeMb * 2;
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
            $"Estimated RAM: ~{totalCacheMb} MB (2 connections × {cacheSizeMb} MB)");

        ImGui.Unindent();
        ImGui.Spacing();
    }

    private void DrawMemoryCacheSection()
    {
        if (!ImGui.CollapsingHeader("In-Memory Time Series Cache##StorageCache", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();
        var cacheConfig = _configService.Config.TimeSeriesCacheConfig;

        // Max points per series
        var maxPoints = cacheConfig.MaxPointsPerSeries;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Max Points per Series##CachePoints", ref maxPoints, 1000, 5000))
        {
            cacheConfig.MaxPointsPerSeries = Math.Max(100, Math.Min(100000, maxPoints));
            _configService.Save();
        }
        DrawHelpMarker(
            "Maximum data points cached in memory for each time series.\n\n" +
            "Each point uses ~16 bytes (timestamp + value).\n" +
            "Memory per series ≈ MaxPoints × 16 bytes.\n\n" +
            "• 1,000 points ≈ 16 KB per series\n" +
            "• 10,000 points ≈ 160 KB per series (default)\n" +
            "• 100,000 points ≈ 1.6 MB per series\n\n" +
            "Higher values: More historical data in graphs without DB queries.\n" +
            "Lower values: Reduced RAM usage, more frequent DB reads.\n\n" +
            "Recommended: 5,000-20,000 for most users.");

        // Max cache hours
        var maxHours = cacheConfig.MaxCacheHours;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Max Cache Duration (hours)##CacheHours", ref maxHours, 24, 168))
        {
            cacheConfig.MaxCacheHours = Math.Max(1, Math.Min(720, maxHours));
            _configService.Save();
        }
        DrawHelpMarker(
            "Maximum age of data kept in memory cache.\n\n" +
            "Data older than this is trimmed during maintenance\n" +
            "but remains available in the database.\n\n" +
            "• 24 hours: Minimal RAM, only recent data cached\n" +
            "• 168 hours (7 days): Good balance (default)\n" +
            "• 720 hours (30 days): Maximum history in cache\n\n" +
            "Higher values improve responsiveness for historical graphs\n" +
            "but increase RAM usage.");

        ImGui.Spacing();

        // Pre-populate on startup
        var prePopulate = cacheConfig.PrePopulateOnStartup;
        if (ImGui.Checkbox("Pre-populate Cache on Startup##PrePopulate", ref prePopulate))
        {
            cacheConfig.PrePopulateOnStartup = prePopulate;
            _configService.Save();
        }
        DrawHelpMarker(
            "When enabled, loads recent data from database into cache at startup.\n\n" +
            "Enabled: Graphs display immediately, slightly longer startup.\n" +
            "Disabled: Faster startup, data loads on-demand (slower first graph).\n\n" +
            "Recommended: Enabled for best user experience.");

        if (prePopulate)
        {
            ImGui.Indent();
            var startupHours = cacheConfig.StartupLoadHours;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("Startup Load Hours##StartupHours", ref startupHours, 6, 24))
            {
                cacheConfig.StartupLoadHours = Math.Max(1, Math.Min(168, startupHours));
                _configService.Save();
            }
            DrawHelpMarker(
                "Hours of historical data to load from database at startup.\n\n" +
                "• 6 hours: Fast startup, limited initial history\n" +
                "• 24 hours: Good balance (default)\n" +
                "• 168 hours: Full week of history, slower startup\n\n" +
                "Additional history loads on-demand when scrolling graphs.");
            ImGui.Unindent();
        }

        // Show current cache stats if available
        // Constants for memory estimation
        const int bytesPerPoint = 16; // timestamp (8) + value (8)
        const int seriesOverhead = 200; // Dictionary entry, list overhead, etc.

        if (_currencyTrackerService.HasDb)
        {
            ImGui.Spacing();
            var stats = _currencyTrackerService.CacheService.GetStatistics();
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
                $"Current: {stats.SeriesCount} series, {stats.TotalPoints:N0} points, {stats.HitRate:P1} hit rate");

            // Estimate current memory usage based on actual tracked data
            var currentMemEstimate = (stats.TotalPoints * bytesPerPoint) + (stats.SeriesCount * seriesOverhead);
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
                $"Estimated current usage: ~{FormatBytes(currentMemEstimate)}");
        }

        // Show tracked items count
        var trackedItemsCount = _configService.Config.ItemsWithHistoricalTracking.Count;
        if (trackedItemsCount > 0)
        {
            var bytesPerSeriesTracked = cacheConfig.MaxPointsPerSeries * bytesPerPoint + seriesOverhead;
            var estimatedMemForTracked = (long)trackedItemsCount * bytesPerSeriesTracked;
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1.0f, 0.5f, 1f),
                $"Items with tracking enabled: {trackedItemsCount}");
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
                $"Max memory if all filled: ~{FormatBytes(estimatedMemForTracked)} per character");
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
                "No items with historical tracking enabled.");
        }

        // Theoretical maximum memory estimate
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.8f, 0.4f, 1.0f), "Theoretical Maximum Memory");
        DrawHelpMarker(
            "Estimates if ALL marketable items were tracked across multiple characters.\n\n" +
            "In practice, only items you own and track will use memory.\n" +
            "Most users track <100 items, using minimal RAM.");

        // FFXIV has ~15,000-20,000 marketable items
        // Assume worst case: all items tracked per character
        const int estimatedMarketableItems = 18000;
        var bytesPerSeries = cacheConfig.MaxPointsPerSeries * bytesPerPoint;
        var totalBytesPerSeries = bytesPerSeries + seriesOverhead;

        // Show estimates for different character counts
        var memFor1Char = (long)estimatedMarketableItems * totalBytesPerSeries;
        var memFor5Chars = memFor1Char * 5;
        var memFor10Chars = memFor1Char * 10;

        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f),
            $"If ALL ~{estimatedMarketableItems:N0} items tracked per character:");
        ImGui.BulletText($"1 character:   ~{FormatBytes(memFor1Char)}");
        ImGui.BulletText($"5 characters:  ~{FormatBytes(memFor5Chars)}");
        ImGui.BulletText($"10 characters: ~{FormatBytes(memFor10Chars)}");
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f),
            "(Actual usage is much lower - only tracked items use memory)");

        ImGui.Unindent();
        ImGui.Spacing();
    }

    private void DrawPriceDataRetentionSection()
    {
        if (!ImGui.CollapsingHeader("Price Data Retention##StoragePrice", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();
        var settings = _configService.Config.PriceTracking;

        if (!settings.Enabled)
        {
            ImGui.TextDisabled("Price tracking is disabled. Enable it in the Universalis category.");
            ImGui.Unindent();
            return;
        }

        // Retention type
        var retentionType = (int)settings.RetentionType;
        string[] retentionTypeNames = { "By Time (days)", "By Size (MB)" };
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Retention Policy##PriceRetentionStorage", ref retentionType, retentionTypeNames, retentionTypeNames.Length))
        {
            settings.RetentionType = (PriceRetentionType)retentionType;
            _configService.Save();
        }
        DrawHelpMarker(
            "How to limit stored price history data.\n\n" +
            "By Time: Deletes records older than N days.\n" +
            "  • Predictable retention period\n" +
            "  • Database size varies with data volume\n\n" +
            "By Size: Deletes oldest records when database exceeds N MB.\n" +
            "  • Predictable storage usage\n" +
            "  • Retention period varies with data volume\n\n" +
            "Recommended: 'By Time' for most users.");

        if (settings.RetentionType == PriceRetentionType.ByTime)
        {
            var retentionDays = settings.RetentionDays;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Days to Retain##RetentionDaysStorage", ref retentionDays, 1, 7))
            {
                settings.RetentionDays = Math.Max(1, Math.Min(365, retentionDays));
                _configService.Save();
            }
            DrawHelpMarker(
                "Number of days to keep price history in database.\n\n" +
                "• 1-3 days: Minimal storage, limited trend analysis\n" +
                "• 7 days: Good balance (default)\n" +
                "• 30+ days: Extended history, larger database\n\n" +
                "Older data is automatically deleted during cleanup.");
        }
        else
        {
            var retentionMb = settings.RetentionSizeMb;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Max Size (MB)##RetentionSizeStorage", ref retentionMb, 10, 50))
            {
                settings.RetentionSizeMb = Math.Max(10, Math.Min(1000, retentionMb));
                _configService.Save();
            }
            DrawHelpMarker(
                "Maximum database size for price history data.\n\n" +
                "• 50 MB: Conservative, suitable for limited tracking\n" +
                "• 100 MB: Good balance (default)\n" +
                "• 500+ MB: Extended history, more storage\n\n" +
                "Oldest records are deleted when limit is exceeded.");
        }

        // Cleanup interval
        var cleanupInterval = settings.CleanupIntervalMinutes;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Cleanup Interval (minutes)##CleanupInterval", ref cleanupInterval, 15, 60))
        {
            settings.CleanupIntervalMinutes = Math.Max(5, Math.Min(1440, cleanupInterval));
            _configService.Save();
        }
        DrawHelpMarker(
            "How often to run automatic database cleanup.\n\n" +
            "• 15 minutes: Aggressive, keeps size consistent\n" +
            "• 60 minutes: Good balance (default)\n" +
            "• 240+ minutes: Less overhead, temporary size growth\n\n" +
            "Lower values may impact performance during cleanup.\n" +
            "Higher values allow database to grow between cleanups.");

        ImGui.Unindent();
        ImGui.Spacing();
    }

    private void DrawLiveFeedSection()
    {
        if (!ImGui.CollapsingHeader("Websocket Feed Buffer##StorageFeed", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();
        var settings = _configService.Config.WebsocketFeed;

        var maxEntries = settings.MaxEntries;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Max Display Entries##FeedEntries", ref maxEntries, 25, 100))
        {
            settings.MaxEntries = Math.Max(10, Math.Min(1000, maxEntries));
            _configService.Save();
        }
        DrawHelpMarker(
            "Maximum entries shown in the Websocket Feed tool.\n\n" +
            "This is an in-memory UI buffer, not database storage.\n" +
            "Older entries are discarded as new ones arrive.\n\n" +
            "• 50 entries: Minimal RAM, recent events only\n" +
            "• 100 entries: Good balance (default)\n" +
            "• 500+ entries: Extended history, more RAM\n\n" +
            "Each entry uses approximately 100-200 bytes.");

        // Memory estimate for live feed buffer
        // Each entry contains: item name (~50 bytes), world name (~20 bytes), prices, quantities, timestamps, etc.
        // Estimated average: ~150 bytes per entry
        const int bytesPerEntry = 150;
        var estimatedBytes = (long)settings.MaxEntries * bytesPerEntry;
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
            $"Estimated buffer size: ~{FormatBytes(estimatedBytes)}");

        ImGui.Unindent();
    }

    private static void DrawHelpMarker(string description)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25.0f);
            ImGui.TextUnformatted(description);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    #region Data Cleanup Section

    private void DrawDataCleanupSection()
    {
        if (!ImGui.CollapsingHeader("Data Cleanup##StorageCleanup"))
            return;

        ImGui.Indent();
        
        if (!_currencyTrackerService.HasDb)
        {
            ImGui.TextDisabled("No database available. Start tracking data to use this feature.");
            ImGui.Unindent();
            return;
        }

        ImGui.TextWrapped("Delete data points within a specific date range for a tracked currency or item. Useful for cleaning up erroneous data or freeing storage.");
        ImGui.Spacing();

        // Mode Selection (Currencies vs Items)
        ImGui.TextUnformatted("Data Category:");
        ImGui.SameLine();
        var mode = (int)_cleanupDataMode;
        if (ImGui.RadioButton("Currencies##CleanupModeCurrency", ref mode, 0))
        {
            _cleanupDataMode = CleanupDataMode.Currencies;
            RefreshPreview();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Items##CleanupModeItem", ref mode, 1))
        {
            _cleanupDataMode = CleanupDataMode.Items;
            RefreshPreview();
        }
        DrawHelpMarker("Choose between currencies (Gil, Tomestones, etc.) or tracked game items.");

        // Data Type/Item Selection based on mode
        if (_cleanupDataMode == CleanupDataMode.Currencies)
        {
            ImGui.TextUnformatted("Currency:");
            ImGui.SameLine();
            if (_cleanupCurrencyCombo != null)
            {
                _cleanupCurrencyCombo.Draw(200);
            }
            DrawHelpMarker("Select the currency to delete data for (Gil, Tomestones, etc.)");
        }
        else
        {
            ImGui.TextUnformatted("Item:");
            ImGui.SameLine();
            if (_cleanupItemCombo != null)
            {
                _cleanupItemCombo.Draw(250);
            }
            DrawHelpMarker("Select the game item to delete tracking data for. Only items with historical tracking enabled will have data.");
        }

        // Character Selection
        ImGui.TextUnformatted("Character:");
        ImGui.SameLine();
        if (_cleanupCharacterCombo != null)
        {
            _cleanupCharacterCombo.Draw(200);
        }
        DrawHelpMarker("Select a specific character or 'All Characters' to delete data across all tracked characters.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Date Range Selection
        DrawDateRangeSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Preview Section
        DrawPreviewSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Delete Options
        DrawDeleteOptionsSection();

        // Confirmation Modal
        DrawDeleteConfirmationModal();

        // Result notification
        if (_showDeleteResult)
        {
            ImGui.Spacing();
            var resultColor = _lastDeletedCount > 0
                ? new System.Numerics.Vector4(0.4f, 1.0f, 0.4f, 1.0f)
                : new System.Numerics.Vector4(1.0f, 0.8f, 0.4f, 1.0f);
            ImGui.TextColored(resultColor, $"Deleted {_lastDeletedCount:N0} data points.");
            if (!string.IsNullOrEmpty(_lastBackupPath))
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    $"Backup saved to: {_lastBackupPath}");
            }
        }

        ImGui.Unindent();
        ImGui.Spacing();
    }

    private void DrawDateRangeSection()
    {
        ImGui.TextUnformatted("Date Range");
        DrawHelpMarker("Select the start and end date/time for the data to delete. Only points within this range will be affected.");

        // Try to get combined data range for all selected variables (player + retainer)
        var variableNames = GetSelectedVariableNames();
        if (variableNames.Count > 0)
        {
            DateTime? earliest = null;
            DateTime? latest = null;
            
            foreach (var varName in variableNames)
            {
                var dataRange = _currencyTrackerService.DbService.GetDataTimeRange(varName);
                if (dataRange.HasValue)
                {
                    if (!earliest.HasValue || dataRange.Value.earliest < earliest.Value)
                        earliest = dataRange.Value.earliest;
                    if (!latest.HasValue || dataRange.Value.latest > latest.Value)
                        latest = dataRange.Value.latest;
                }
            }
            
            if (earliest.HasValue && latest.HasValue)
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    $"Available data: {earliest.Value:yyyy-MM-dd HH:mm} to {latest.Value:yyyy-MM-dd HH:mm}");
                
                ImGui.SameLine();
                var earliestCopy = earliest.Value;
                var latestCopy = latest.Value;
                if (ImGui.SmallButton("Use Full Range##UseFullRange"))
                {
                    _startDatePicker?.Select(earliestCopy);
                    _endDatePicker?.Select(latestCopy);
                    RefreshPreview();
                }
            }
        }

        ImGui.Spacing();

        // Start Date
        ImGui.TextUnformatted("From:");
        ImGui.SameLine();
        ImGui.SetCursorPosX(80);
        _startDatePicker?.Draw(180);

        // End Date
        ImGui.TextUnformatted("To:");
        ImGui.SameLine();
        ImGui.SetCursorPosX(80);
        _endDatePicker?.Draw(180);

        // Quick range buttons
        ImGui.Spacing();
        if (ImGui.SmallButton("Last 24h"))
        {
            var now = DateTime.Now;
            _startDatePicker?.Select(now.AddHours(-24));
            _endDatePicker?.Select(now);
            RefreshPreview();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Last 7d"))
        {
            var now = DateTime.Now;
            _startDatePicker?.Select(now.AddDays(-7));
            _endDatePicker?.Select(now);
            RefreshPreview();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Last 30d"))
        {
            var now = DateTime.Now;
            _startDatePicker?.Select(now.AddDays(-30));
            _endDatePicker?.Select(now);
            RefreshPreview();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh Preview"))
        {
            RefreshPreview();
        }
    }

    private void DrawPreviewSection()
    {
        var variableNames = GetSelectedVariableNames();
        if (variableNames.Count == 0)
        {
            ImGui.TextDisabled("Select a data type to preview affected points.");
            return;
        }

        // Update stats - aggregate across all variables (player + retainer)
        var characterId = _cleanupCharacterCombo?.IsAllSelected == true ? (ulong?)null : _cleanupCharacterCombo?.SelectedCharacterId;
        var start = _startDatePicker?.SelectedDateTime ?? DateTime.MinValue;
        var end = _endDatePicker?.SelectedDateTime ?? DateTime.MaxValue;
        
        int totalCount = 0;
        long totalBytes = 0;
        foreach (var varName in variableNames)
        {
            var stats = _currencyTrackerService.CountPointsInRangeByVariable(varName, characterId, start, end);
            totalCount += stats.count;
            totalBytes += stats.estimatedBytes;
        }
        _deleteStats = (totalCount, totalBytes);

        ImGui.TextUnformatted($"Points to Delete: {_deleteStats.count:N0}");
        ImGui.SameLine();
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f),
            $"(~{FormatBytes(_deleteStats.estimatedBytes)} storage)");

        if (_deleteStats.count == 0)
        {
            ImGui.TextDisabled("No data points found in the selected range.");
            return;
        }

        // Collapsible preview table
        if (ImGui.CollapsingHeader($"Preview ({_previewPoints.Count:N0} points loaded)##PreviewHeader"))
        {
            DrawPreviewTable();
        }
    }

    private void DrawPreviewTable()
    {
        if (_previewPoints.Count == 0)
        {
            ImGui.TextDisabled("Click 'Refresh Preview' to load data points.");
            return;
        }

        // Pagination
        var totalPages = (_previewPoints.Count + PreviewPageSize - 1) / PreviewPageSize;
        if (totalPages > 1)
        {
            ImGui.TextUnformatted($"Page {_previewCurrentPage + 1} of {totalPages}");
            ImGui.SameLine();
            if (ImGui.SmallButton("<<##First") && _previewCurrentPage > 0)
                _previewCurrentPage = 0;
            ImGui.SameLine();
            if (ImGui.SmallButton("<##Prev") && _previewCurrentPage > 0)
                _previewCurrentPage--;
            ImGui.SameLine();
            if (ImGui.SmallButton(">##Next") && _previewCurrentPage < totalPages - 1)
                _previewCurrentPage++;
            ImGui.SameLine();
            if (ImGui.SmallButton(">>##Last") && _previewCurrentPage < totalPages - 1)
                _previewCurrentPage = totalPages - 1;
        }

        // Table
        if (ImGui.BeginTable("##PreviewTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Timestamp", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            var startIndex = _previewCurrentPage * PreviewPageSize;
            var endIndex = Math.Min(startIndex + PreviewPageSize, _previewPoints.Count);

            for (var i = startIndex; i < endIndex; i++)
            {
                var point = _previewPoints[i];
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(point.timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(point.value.ToString("N0"));

                ImGui.TableNextColumn();
                var charName = _currencyTrackerService.CharacterDataCache.GetCharacterName(point.characterId);
                ImGui.TextUnformatted(charName ?? point.characterId.ToString());
            }

            ImGui.EndTable();
        }
    }

    private void DrawDeleteOptionsSection()
    {
        // Backup option
        if (ImGui.Checkbox("Backup data before deleting##BackupBeforeDelete", ref _backupBeforeDelete))
        {
            // Update size estimate
        }
        if (_backupBeforeDelete && _deleteStats.count > 0)
        {
            // Estimate CSV file size (roughly 50 bytes per row for CSV format)
            var estimatedCsvBytes = _deleteStats.count * 50L;
            ImGui.Indent();
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.8f, 0.4f, 1.0f),
                $"⚠ Backup will create a ~{FormatBytes(estimatedCsvBytes)} CSV file");
            ImGui.Unindent();
        }
        DrawHelpMarker("Export the data to a CSV file before deleting. The backup will be saved in the plugin's data folder.");

        // Vacuum option
        if (ImGui.Checkbox("Reclaim disk space after deletion (VACUUM)##VacuumAfterDelete", ref _vacuumAfterDelete))
        {
            // Just toggle
        }
        if (_vacuumAfterDelete)
        {
            ImGui.Indent();
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.8f, 0.4f, 1.0f),
                "⚠ VACUUM can take several seconds on large databases and may cause brief UI freezes.");
            ImGui.Unindent();
        }
        DrawHelpMarker(
            "SQLite does not automatically shrink the database file after deletions.\n" +
            "VACUUM rewrites the database to reclaim unused space.\n\n" +
            "• Recommended after large deletions\n" +
            "• Can be slow on large databases (100MB+)\n" +
            "• May cause brief UI freezes during execution");

        ImGui.Spacing();

        // Delete button
        var canDelete = _deleteStats.count > 0 && HasValidSelection();
        if (!canDelete)
            ImGui.BeginDisabled();

        var deleteButtonColor = new System.Numerics.Vector4(0.8f, 0.2f, 0.2f, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.Button, deleteButtonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, deleteButtonColor with { X = 0.9f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, deleteButtonColor with { X = 1.0f });

        if (ImGui.Button($"Delete {_deleteStats.count:N0} Data Points##DeleteButton", new System.Numerics.Vector2(200, 30)))
        {
            _deleteConfirmationOpen = true;
            ImGui.OpenPopup("##DeleteConfirmModal");
        }

        ImGui.PopStyleColor(3);

        if (!canDelete)
            ImGui.EndDisabled();
    }

    private void DrawDeleteConfirmationModal()
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("##DeleteConfirmModal", ref _deleteConfirmationOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.4f, 0.4f, 1.0f), "⚠ Confirm Deletion");
            ImGui.Separator();
            ImGui.Spacing();

            var selectedDisplayName = GetSelectedDisplayName();
            var characterText = _cleanupCharacterCombo?.IsAllSelected == true
                ? "all characters"
                : _cleanupCharacterCombo?.SelectedCharacter?.Name ?? "selected character";

            ImGui.TextWrapped($"You are about to delete {_deleteStats.count:N0} data points for {selectedDisplayName} from {characterText}.");
            ImGui.Spacing();
            ImGui.TextWrapped($"Date range: {_startDatePicker?.SelectedDateTime:yyyy-MM-dd HH:mm} to {_endDatePicker?.SelectedDateTime:yyyy-MM-dd HH:mm}");
            ImGui.Spacing();
            ImGui.TextWrapped($"This will free approximately {FormatBytes(_deleteStats.estimatedBytes)} of storage.");

            if (_backupBeforeDelete)
            {
                ImGui.Spacing();
                ImGui.TextColored(new System.Numerics.Vector4(0.4f, 1.0f, 0.4f, 1.0f), "✓ Data will be backed up before deletion.");
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.6f, 0.4f, 1.0f), "⚠ No backup will be created. This action cannot be undone!");
            }

            if (_vacuumAfterDelete)
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Database will be vacuumed after deletion.");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var buttonWidth = 120f;
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var totalWidth = buttonWidth * 2 + spacing;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalWidth) / 2);

            var deleteColor = new System.Numerics.Vector4(0.8f, 0.2f, 0.2f, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.Button, deleteColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, deleteColor with { X = 0.9f });
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, deleteColor with { X = 1.0f });

            if (ImGui.Button("Delete", new System.Numerics.Vector2(buttonWidth, 0)))
            {
                ExecuteDelete();
                ImGui.CloseCurrentPopup();
                _deleteConfirmationOpen = false;
            }

            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new System.Numerics.Vector2(buttonWidth, 0)))
            {
                ImGui.CloseCurrentPopup();
                _deleteConfirmationOpen = false;
            }

            ImGui.EndPopup();
        }
    }

    private void RefreshPreview()
    {
        var variableNames = GetSelectedVariableNames();
        if (variableNames.Count == 0)
        {
            _previewPoints.Clear();
            _previewCurrentPage = 0;
            return;
        }

        var characterId = _cleanupCharacterCombo?.IsAllSelected == true ? (ulong?)null : _cleanupCharacterCombo?.SelectedCharacterId;
        var start = _startDatePicker?.SelectedDateTime ?? DateTime.MinValue;
        var end = _endDatePicker?.SelectedDateTime ?? DateTime.MaxValue;

        // Combine points from all variables (player + retainer)
        _previewPoints.Clear();
        foreach (var varName in variableNames)
        {
            var points = _currencyTrackerService.GetPointsInRangeByVariable(varName, characterId, start, end);
            _previewPoints.AddRange(points);
        }
        
        // Sort by timestamp descending
        _previewPoints.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));
        
        _previewCurrentPage = 0;
        _showDeleteResult = false;
    }

    private void ExecuteDelete()
    {
        var variableNames = GetSelectedVariableNames();
        if (variableNames.Count == 0) return;

        var displayName = GetSelectedDisplayName();
        var characterId = _cleanupCharacterCombo?.IsAllSelected == true ? (ulong?)null : _cleanupCharacterCombo?.SelectedCharacterId;
        var start = _startDatePicker?.SelectedDateTime ?? DateTime.MinValue;
        var end = _endDatePicker?.SelectedDateTime ?? DateTime.MaxValue;

        _lastBackupPath = null;
        _lastDeletedCount = 0;

        try
        {
            // Backup if requested - export all variables to a single CSV
            if (_backupBeforeDelete)
            {
                // Use the first variable name for the backup filename, but include all data
                _lastBackupPath = _currencyTrackerService.ExportPointsInRangeByVariablesToCsv(variableNames, characterId, start, end);
                if (string.IsNullOrEmpty(_lastBackupPath))
                {
                    _messageService.NotificationMessage("Failed to create backup. Deletion cancelled.", NotificationType.Error);
                    return;
                }
            }

            // Delete from all variables (player + retainer)
            foreach (var varName in variableNames)
            {
                _lastDeletedCount += _currencyTrackerService.DeletePointsInRangeByVariable(varName, characterId, start, end);
            }

            // Vacuum if requested
            if (_vacuumAfterDelete && _lastDeletedCount > 0)
            {
                _currencyTrackerService.Vacuum();
            }

            // Show notification
            var message = $"Deleted {_lastDeletedCount:N0} data points for {displayName}.";
            if (!string.IsNullOrEmpty(_lastBackupPath))
            {
                message += $" Backup saved.";
            }
            _messageService.NotificationMessage(message, NotificationType.Success);

            // Refresh preview
            RefreshPreview();
            _showDeleteResult = true;
        }
        catch (Exception ex)
        {
            _messageService.NotificationMessage(ex, "Failed to delete data points.", NotificationType.Error);
        }
    }

    #endregion

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
