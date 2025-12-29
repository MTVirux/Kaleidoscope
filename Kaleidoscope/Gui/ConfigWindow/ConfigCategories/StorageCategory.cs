using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Storage &amp; Cache configuration category in the config window.
/// Centralizes all database and memory cache size settings with detailed tooltips.
/// </summary>
public sealed class StorageCategory
{
    private readonly ConfigurationService _configService;
    private readonly CurrencyTrackerService _currencyTrackerService;

    public StorageCategory(ConfigurationService configService, CurrencyTrackerService CurrencyTrackerService)
    {
        _configService = configService;
        _currencyTrackerService = CurrencyTrackerService;
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
        if (!ImGui.CollapsingHeader("Live Price Feed Buffer##StorageFeed", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.Indent();
        var settings = _configService.Config.LivePriceFeed;

        var maxEntries = settings.MaxEntries;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Max Display Entries##FeedEntries", ref maxEntries, 25, 100))
        {
            settings.MaxEntries = Math.Max(10, Math.Min(1000, maxEntries));
            _configService.Save();
        }
        DrawHelpMarker(
            "Maximum entries shown in the Live Price Feed tool.\n\n" +
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
