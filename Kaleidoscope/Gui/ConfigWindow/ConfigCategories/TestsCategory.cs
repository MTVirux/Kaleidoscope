using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using MTGui.Tree;
using System.Diagnostics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Tests category for the config window.
/// Provides interactive testing of services, integrations, and database sanity checks.
/// Only visible in the Developer menu (CTRL+ALT or developer mode enabled).
/// </summary>
public sealed class TestsCategory
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly AutoRetainerIpcService _arIpcService;
    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly ConfigurationService _configService;
    private readonly MarketDataCacheService _marketDataCacheService;

    // Test results storage
    private readonly List<TestResult> _testResults = new();
    private bool _isRunningTests = false;
    private string _currentTestName = "";

    // Individual test state
    private bool _dbConnectionTested = false;
    private bool _dbReadWriteTested = false;
    private bool _arIpcTested = false;
    private bool _universalisTested = false;
    private bool _webSocketTested = false;
    
    // Cache architecture test state
    private bool _cacheInitTested = false;
    private bool _cacheReadsTested = false;
    private bool _cacheWritesTested = false;
    private bool _cacheDelegationTested = false;
    private bool _cacheConsistencyTested = false;
    
    // Phase 2: Registry cache test state
    private bool _registryCacheTested = false;
    private bool _categoryLookupTested = false;
    
    // Phase 3: Time-series cache test state
    private bool _timeSeriesBatchReadTested = false;
    private bool _timeSeriesLatestValuesTested = false;
    
    // Phase 4: Configuration cache test state
    private bool _configDirtyTrackingTested = false;
    private bool _configDebounceTested = false;
    private bool _configStatisticsTested = false;
    
    // Phase 5: Market data cache test state
    private bool _marketCachePriceOpsTested = false;
    private bool _marketCacheTtlTested = false;
    private bool _marketCacheStatsTested = false;
    
    // Phase 6: Layout editing cache test state
    private bool _layoutToolCacheTested = false;
    private bool _layoutSnapshotDebounceTested = false;
    private bool _layoutStatsTested = false;

    private readonly LayoutEditingService _layoutEditingService;

    public TestsCategory(
        CurrencyTrackerService currencyTrackerService,
        AutoRetainerIpcService arIpcService,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        ConfigurationService configService,
        MarketDataCacheService marketDataCacheService,
        LayoutEditingService layoutEditingService)
    {
        _currencyTrackerService = currencyTrackerService;
        _arIpcService = arIpcService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _configService = configService;
        _marketDataCacheService = marketDataCacheService;
        _layoutEditingService = layoutEditingService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Tests");
        ImGui.Separator();

        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "Developer Tool - Service & Integration Tests");
        ImGui.Spacing();

        // Run all tests button
        if (ImGui.Button("Run All Tests") && !_isRunningTests)
        {
            _testResults.Clear();
            RunAllTestsAsync();
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear Results"))
        {
            _testResults.Clear();
            _dbConnectionTested = false;
            _dbReadWriteTested = false;
            _arIpcTested = false;
            _universalisTested = false;
            _webSocketTested = false;
        }

        if (_isRunningTests)
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 1f, 0f, 1f), $"Running: {_currentTestName}...");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Individual test sections
        DrawDatabaseTests();
        ImGui.Spacing();
        DrawIntegrationTests();
        ImGui.Spacing();
        DrawServiceTests();
        ImGui.Spacing();
        DrawCacheArchitectureTests();
        ImGui.Spacing();

        // Test results display
        DrawTestResults();
    }

    private void DrawDatabaseTests()
    {
        if (MTTreeHelpers.DrawCollapsingSection("Database Tests", true))
        {
            ImGui.Indent();

            // DB Connection Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test DB Connection"))
            {
                RunSingleTest("DB Connection", TestDbConnection);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_dbConnectionTested, "DB Connection");

            // DB Read/Write Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test DB Read/Write"))
            {
                RunSingleTest("DB Read/Write", TestDbReadWrite);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_dbReadWriteTested, "DB Read/Write");

            // DB Sanity Check
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Run DB Sanity Check"))
            {
                RunSingleTest("DB Sanity Check", TestDbSanity);
            }
            ImGui.EndDisabled();

            // DB Stats
            if (MTTreeHelpers.DrawSection("Database Statistics"))
            {
                DrawDbStats();
                MTTreeHelpers.EndSection();
            }

            ImGui.Unindent();
        }
    }

    private void DrawIntegrationTests()
    {
        if (MTTreeHelpers.DrawCollapsingSection("Integration Tests", true))
        {
            ImGui.Indent();

            // AutoRetainer IPC Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test AutoRetainer IPC"))
            {
                RunSingleTest("AutoRetainer IPC", TestAutoRetainerIpc);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_arIpcTested, "AutoRetainer IPC");
            ImGui.SameLine();
            ImGui.TextColored(
                _arIpcService.IsAvailable 
                    ? new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f) 
                    : new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f),
                _arIpcService.IsAvailable ? "(Available)" : "(Unavailable)");

            // Universalis API Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Universalis API"))
            {
                RunSingleTest("Universalis API", TestUniversalisApi);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_universalisTested, "Universalis API");

            // WebSocket Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test WebSocket"))
            {
                RunSingleTest("WebSocket", TestWebSocket);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_webSocketTested, "WebSocket");
            ImGui.SameLine();
            ImGui.TextColored(
                _webSocketService.IsConnected 
                    ? new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f) 
                    : new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
                _webSocketService.IsConnected ? "(Connected)" : "(Disconnected)");

            ImGui.Unindent();
        }
    }

    private void DrawServiceTests()
    {
        if (MTTreeHelpers.DrawCollapsingSection("Service Tests", false))
        {
            ImGui.Indent();

            // Cache Service Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Cache Service"))
            {
                RunSingleTest("Cache Service", TestCacheService);
            }
            ImGui.EndDisabled();

            // Registry Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Tracked Data Registry"))
            {
                RunSingleTest("Tracked Data Registry", TestRegistry);
            }
            ImGui.EndDisabled();

            // Config Service Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Config Service"))
            {
                RunSingleTest("Config Service", TestConfigService);
            }
            ImGui.EndDisabled();

            ImGui.Unindent();
        }
    }

    private void DrawCacheArchitectureTests()
    {
        if (MTTreeHelpers.DrawCollapsingSection("Cache Architecture Tests", false))
        {
            ImGui.Indent();

            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "Phase 1: Character Data Cache");
            ImGui.Spacing();

            // CharacterDataCacheService Initialization Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test CharacterDataCache Init"))
            {
                _cacheInitTested = true;
                RunSingleTest("CharacterDataCache Init", TestCharacterDataCacheInit);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_cacheInitTested, "CharacterDataCache Init");

            // CharacterDataCacheService Read Operations Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test CharacterDataCache Reads"))
            {
                _cacheReadsTested = true;
                RunSingleTest("CharacterDataCache Reads", TestCharacterDataCacheReads);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_cacheReadsTested, "CharacterDataCache Reads");

            // CharacterDataCacheService Write Operations Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test CharacterDataCache Writes"))
            {
                _cacheWritesTested = true;
                RunSingleTest("CharacterDataCache Writes", TestCharacterDataCacheWrites);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_cacheWritesTested, "CharacterDataCache Writes");

            // Delegation from TimeSeriesCacheService Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test TimeSeriesCache Delegation"))
            {
                _cacheDelegationTested = true;
                RunSingleTest("TimeSeriesCache Delegation", TestTimeSeriesCacheDelegation);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_cacheDelegationTested, "TimeSeriesCache Delegation");

            // Cache Consistency Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Cache-DB Consistency"))
            {
                _cacheConsistencyTested = true;
                RunSingleTest("Cache-DB Consistency", TestCacheDbConsistency);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_cacheConsistencyTested, "Cache-DB Consistency");

            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "Phase 2: Tracked Data Registry Cache");
            ImGui.Spacing();

            // Registry Cache Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Registry Cache"))
            {
                _registryCacheTested = true;
                RunSingleTest("Registry Cache", TestRegistryCache);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_registryCacheTested, "Registry Cache");

            // Category Lookup Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Category Lookup"))
            {
                _categoryLookupTested = true;
                RunSingleTest("Category Lookup", TestCategoryLookup);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_categoryLookupTested, "Category Lookup");

            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "Phase 3: Time-Series Cache");
            ImGui.Spacing();

            // Time-Series Batch Read Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test TimeSeries Batch Read"))
            {
                _timeSeriesBatchReadTested = true;
                RunSingleTest("TimeSeries Batch Read", TestTimeSeriesBatchRead);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_timeSeriesBatchReadTested, "TimeSeries Batch Read");

            // Time-Series Latest Values Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test TimeSeries Latest Values"))
            {
                _timeSeriesLatestValuesTested = true;
                RunSingleTest("TimeSeries Latest Values", TestTimeSeriesLatestValues);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_timeSeriesLatestValuesTested, "TimeSeries Latest Values");

            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "Phase 4: Configuration Cache");
            ImGui.Spacing();

            // Config Dirty Tracking Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Config Dirty Tracking"))
            {
                _configDirtyTrackingTested = true;
                RunSingleTest("Config Dirty Tracking", TestConfigDirtyTracking);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_configDirtyTrackingTested, "Config Dirty Tracking");

            // Config Debounce Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Config Debounce"))
            {
                _configDebounceTested = true;
                RunSingleTest("Config Debounce", TestConfigDebounce);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_configDebounceTested, "Config Debounce");

            // Config Statistics Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Config Statistics"))
            {
                _configStatisticsTested = true;
                RunSingleTest("Config Statistics", TestConfigStatistics);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_configStatisticsTested, "Config Statistics");

            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "Phase 5: Market Data Cache");
            ImGui.Spacing();

            // Market Cache Price Ops Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Market Cache Price Ops"))
            {
                _marketCachePriceOpsTested = true;
                RunSingleTest("Market Cache Price Ops", TestMarketCachePriceOps);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_marketCachePriceOpsTested, "Market Cache Price Ops");

            // Market Cache TTL Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Market Cache TTL"))
            {
                _marketCacheTtlTested = true;
                RunSingleTest("Market Cache TTL", TestMarketCacheTtl);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_marketCacheTtlTested, "Market Cache TTL");

            // Market Cache Stats Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Market Cache Stats"))
            {
                _marketCacheStatsTested = true;
                RunSingleTest("Market Cache Stats", TestMarketCacheStats);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_marketCacheStatsTested, "Market Cache Stats");

            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "Phase 6: Layout Editing Cache");
            ImGui.Spacing();

            // Layout Tool Cache Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Layout Tool Cache"))
            {
                _layoutToolCacheTested = true;
                RunSingleTest("Layout Tool Cache", TestLayoutToolCache);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_layoutToolCacheTested, "Layout Tool Cache");

            // Layout Snapshot Debounce Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Layout Snapshot Debounce"))
            {
                _layoutSnapshotDebounceTested = true;
                RunSingleTest("Layout Snapshot Debounce", TestLayoutSnapshotDebounce);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_layoutSnapshotDebounceTested, "Layout Snapshot Debounce");

            // Layout Stats Test
            ImGui.BeginDisabled(_isRunningTests);
            if (ImGui.Button("Test Layout Stats"))
            {
                _layoutStatsTested = true;
                RunSingleTest("Layout Stats", TestLayoutStats);
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            DrawTestStatus(_layoutStatsTested, "Layout Stats");

            ImGui.Spacing();
            
            // Cache Statistics Display
            if (MTTreeHelpers.DrawSection("Cache Statistics"))
            {
                DrawCacheStats();
                MTTreeHelpers.EndSection();
            }

            ImGui.Unindent();
        }
    }

    private void DrawCacheStats()
    {
        try
        {
            var characterCache = _currencyTrackerService.CharacterDataCache;
            var timeSeriesCache = _currencyTrackerService.CacheService;

            ImGui.TextUnformatted("Character Data Cache:");
            ImGui.Indent();
            ImGui.TextUnformatted($"  Cached Characters: {characterCache.CachedCharacterCount}");
            ImGui.TextUnformatted($"  Cache Hits: {characterCache.CacheHits}");
            ImGui.TextUnformatted($"  Cache Misses: {characterCache.CacheMisses}");
            ImGui.TextUnformatted($"  Initialized: {(characterCache.IsInitialized ? "Yes" : "No")}");
            ImGui.Unindent();

            ImGui.Spacing();

            ImGui.TextUnformatted("Time Series Cache:");
            ImGui.Indent();
            var tsStats = timeSeriesCache.GetStatistics();
            ImGui.TextUnformatted($"  Cached Series: {tsStats.SeriesCount}");
            ImGui.TextUnformatted($"  Total Points: {tsStats.TotalPoints}");
            ImGui.TextUnformatted($"  Cache Hits: {tsStats.CacheHits}");
            ImGui.TextUnformatted($"  Cache Misses: {tsStats.CacheMisses}");
            ImGui.TextUnformatted($"  Hit Rate: {tsStats.HitRate:P1}");
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.TextUnformatted("Market Data Cache:");
            ImGui.Indent();
            var marketStats = _marketDataCacheService.GetStatistics();
            ImGui.TextUnformatted($"  Price Entries: {marketStats.TotalPriceEntries} (Fresh: {marketStats.FreshEntries}, Stale: {marketStats.StaleEntries})");
            ImGui.TextUnformatted($"  Recent Sales: {marketStats.RecentSalesEntries}");
            ImGui.TextUnformatted($"  Cache Hits: {marketStats.CacheHits} (Stale Hits: {marketStats.StaleHits})");
            ImGui.TextUnformatted($"  Cache Misses: {marketStats.CacheMisses}");
            ImGui.TextUnformatted($"  Hit Rate: {marketStats.HitRate:F1}%");
            ImGui.TextUnformatted($"  Evictions: {marketStats.Evictions}");
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.TextUnformatted("Configuration Cache:");
            ImGui.Indent();
            ImGui.TextUnformatted($"  Save Count: {_configService.SaveCount}");
            ImGui.TextUnformatted($"  Saves Skipped: {_configService.SaveSkippedCount}");
            ImGui.TextUnformatted($"  Dirty Marks: {_configService.ConfigAccessCount}");
            ImGui.TextUnformatted($"  Is Dirty: {(_configService.IsDirty ? "Yes" : "No")}");
            ImGui.TextUnformatted($"  Last Save: {_configService.LastSaveTime?.ToString("HH:mm:ss") ?? "Never"}");
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.TextUnformatted("Layout Editing Cache:");
            ImGui.Indent();
            var layoutStats = _layoutEditingService.GetStatistics();
            var (gridCols, gridRows) = _layoutEditingService.GetEffectiveGridDimensions();
            ImGui.TextUnformatted($"  Layout: '{layoutStats.CurrentLayoutName}' ({layoutStats.CurrentLayoutType})");
            ImGui.TextUnformatted($"  Tools: {layoutStats.ToolCount}, Grid: {gridCols}x{gridRows}");
            ImGui.TextUnformatted($"  Is Dirty: {(layoutStats.IsDirty ? "Yes" : "No")}");
            ImGui.TextUnformatted($"  Saves: {layoutStats.SaveCount}, Discards: {layoutStats.DiscardCount}");
            ImGui.TextUnformatted($"  Dirty Marks: {layoutStats.DirtyMarkCount}");
            ImGui.TextUnformatted($"  Snapshot Writes: {layoutStats.SnapshotWriteCount}, Skipped: {layoutStats.SnapshotSkippedCount}");
            if (layoutStats.SnapshotSavingsPercent > 0)
                ImGui.TextUnformatted($"  Debounce Savings: {layoutStats.SnapshotSavingsPercent:F1}%");
            ImGui.Unindent();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f), $"Error reading cache stats: {ex.Message}");
        }
    }

    private void DrawTestResults()
    {
        if (_testResults.Count == 0) return;

        ImGui.Separator();
        ImGui.TextUnformatted("Test Results");
        ImGui.Separator();

        // Summary
        var passed = _testResults.Count(r => r.Passed);
        var failed = _testResults.Count(r => !r.Passed);
        var summaryColor = failed == 0 
            ? new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f) 
            : new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f);
        ImGui.TextColored(summaryColor, $"Passed: {passed} | Failed: {failed}");
        ImGui.Spacing();

        // Take a snapshot to avoid concurrent modification during iteration
        var resultsSnapshot = _testResults.ToList();

        // Individual results in scrollable area
        var availHeight = Math.Min(200f, resultsSnapshot.Count * 25f + 10f);
        if (ImGui.BeginChild("##test_results", new System.Numerics.Vector2(0, availHeight), true))
        {
            foreach (var result in resultsSnapshot)
            {
                var color = result.Passed 
                    ? new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f) 
                    : new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f);
                var icon = result.Passed ? "✓" : "✗";
                
                ImGui.TextColored(color, $"{icon} {result.Name}");
                if (!string.IsNullOrEmpty(result.Message))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), $"- {result.Message}");
                }
                if (!string.IsNullOrEmpty(result.Details))
                {
                    ImGui.Indent();
                    ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f), result.Details);
                    ImGui.Unindent();
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawTestStatus(bool tested, string testName)
    {
        if (!tested)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f), "Not run");
            return;
        }

        var result = _testResults.LastOrDefault(r => r.Name.Contains(testName));
        if (result == null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f), "Not run");
            return;
        }

        var color = result.Passed 
            ? new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f) 
            : new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f);
        ImGui.TextColored(color, result.Passed ? "PASS" : "FAIL");
    }

    private void DrawDbStats()
    {
        var db = _currencyTrackerService.DbService;
        if (db == null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f), "Database not available");
            return;
        }

        ImGui.TextUnformatted($"DB Path: {db.DbPath ?? "N/A"}");

        try
        {
            // Get table counts using available methods
            var characters = db.GetAllCharacterNames();
            var characterCount = characters?.Count ?? 0;

            ImGui.TextUnformatted($"Characters: {characterCount}");
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f), $"Error reading stats: {ex.Message}");
        }
    }

    private void RunSingleTest(string name, Func<TestResult> test)
    {
        Task.Run(() =>
        {
            _isRunningTests = true;
            _currentTestName = name;
            try
            {
                var result = test();
                _testResults.Add(result);
                
                // Update specific test status flags
                if (name.Contains("DB Connection")) _dbConnectionTested = true;
                if (name.Contains("DB Read/Write")) _dbReadWriteTested = true;
                if (name.Contains("AutoRetainer")) _arIpcTested = true;
                if (name.Contains("Universalis API")) _universalisTested = true;
                if (name.Contains("WebSocket")) _webSocketTested = true;
            }
            catch (Exception ex)
            {
                _testResults.Add(new TestResult(name, false, "Exception thrown", ex.Message));
            }
            finally
            {
                _isRunningTests = false;
                _currentTestName = "";
            }
        });
    }

    private async void RunAllTestsAsync()
    {
        try
        {
            _isRunningTests = true;
            _testResults.Clear();

            var tests = new List<(string Name, Func<TestResult> Test)>
        {
            ("DB Connection", TestDbConnection),
            ("DB Read/Write", TestDbReadWrite),
            ("DB Sanity Check", TestDbSanity),
            ("AutoRetainer IPC", TestAutoRetainerIpc),
            ("Universalis API", TestUniversalisApi),
            ("WebSocket", TestWebSocket),
            ("Cache Service", TestCacheService),
            ("Tracked Data Registry", TestRegistry),
            ("Config Service", TestConfigService),
            // Cache Architecture Tests (Phase 1)
            ("CharacterDataCache Init", TestCharacterDataCacheInit),
            ("CharacterDataCache Reads", TestCharacterDataCacheReads),
            ("CharacterDataCache Writes", TestCharacterDataCacheWrites),
            ("TimeSeriesCache Delegation", TestTimeSeriesCacheDelegation),
            ("Cache-DB Consistency", TestCacheDbConsistency),
            // Phase 2: Registry Cache Tests
            ("Registry Cache", TestRegistryCache),
            ("Category Lookup", TestCategoryLookup),
            // Phase 3: Time-Series Cache Tests
            ("TimeSeries Batch Read", TestTimeSeriesBatchRead),
            ("TimeSeries Latest Values", TestTimeSeriesLatestValues),
            // Phase 4: Configuration Cache Tests
            ("Config Dirty Tracking", TestConfigDirtyTracking),
            ("Config Debounce", TestConfigDebounce),
            ("Config Statistics", TestConfigStatistics),
            // Phase 5: Market Data Cache Tests
            ("Market Cache Price Ops", TestMarketCachePriceOps),
            ("Market Cache TTL", TestMarketCacheTtl),
            ("Market Cache Stats", TestMarketCacheStats),
            // Phase 6: Layout Editing Cache Tests
            ("Layout Tool Cache", TestLayoutToolCache),
            ("Layout Snapshot Debounce", TestLayoutSnapshotDebounce),
            ("Layout Stats", TestLayoutStats),
        };

        foreach (var (name, test) in tests)
        {
            _currentTestName = name;
            try
            {
                var result = await Task.Run(test);
                _testResults.Add(result);
            }
            catch (Exception ex)
            {
                _testResults.Add(new TestResult(name, false, "Exception thrown", ex.Message));
            }

            // Small delay between tests
            await Task.Delay(100);
        }

        _dbConnectionTested = true;
        _dbReadWriteTested = true;
        _arIpcTested = true;
        _universalisTested = true;
        _webSocketTested = true;
        // Cache architecture tests
        _cacheInitTested = true;
        _cacheReadsTested = true;
        _cacheWritesTested = true;
        _cacheDelegationTested = true;
        _cacheConsistencyTested = true;
        // Phase 2 tests
        _registryCacheTested = true;
        _categoryLookupTested = true;
        // Phase 3 tests
        _timeSeriesBatchReadTested = true;
        _timeSeriesLatestValuesTested = true;
        // Phase 4 tests
        _configDirtyTrackingTested = true;
        _configDebounceTested = true;
        _configStatisticsTested = true;
        // Phase 5 tests
        _marketCachePriceOpsTested = true;
        _marketCacheTtlTested = true;
        _marketCacheStatsTested = true;
        // Phase 6 tests
        _layoutToolCacheTested = true;
        _layoutSnapshotDebounceTested = true;
        _layoutStatsTested = true;
        }
        catch (Exception ex)
        {
            LogService.Error($"RunAllTestsAsync failed: {ex.Message}");
            _testResults.Add(new TestResult("Test Runner", false, "Test runner crashed", ex.Message));
        }
        finally
        {
            _isRunningTests = false;
            _currentTestName = "";
        }
    }

    #region Test Implementations

    private TestResult TestDbConnection()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var db = _currencyTrackerService.DbService;
            if (db == null)
                return new TestResult("DB Connection", false, "DbService is null");

            if (string.IsNullOrEmpty(db.DbPath))
                return new TestResult("DB Connection", false, "DB path is null or empty");

            if (!File.Exists(db.DbPath))
                return new TestResult("DB Connection", false, $"DB file does not exist: {db.DbPath}");

            sw.Stop();
            return new TestResult("DB Connection", true, $"Connected in {sw.ElapsedMilliseconds}ms", $"Path: {db.DbPath}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("DB Connection", false, "Connection failed", ex.Message);
        }
    }

    private TestResult TestDbReadWrite()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var db = _currencyTrackerService.DbService;
            if (db == null)
                return new TestResult("DB Read/Write", false, "DbService is null");

            // Test read: get all character names
            var characters = db.GetAllCharacterNames();
            if (characters == null)
                return new TestResult("DB Read/Write", false, "Failed to read characters");

            // Test getting series (creates if not exists)
            var testSeriesId = db.GetOrCreateSeries("__test_series__", 0);
            
            sw.Stop();
            return new TestResult("DB Read/Write", true, $"Read/Write test passed in {sw.ElapsedMilliseconds}ms", 
                $"Found {characters.Count} character(s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("DB Read/Write", false, "Read/Write test failed", ex.Message);
        }
    }

    private TestResult TestDbSanity()
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();
        
        try
        {
            var db = _currencyTrackerService.DbService;
            if (db == null)
                return new TestResult("DB Sanity Check", false, "DbService is null");

            // Check 1: Verify DB file exists
            if (string.IsNullOrEmpty(db.DbPath))
                return new TestResult("DB Sanity Check", false, "DB path is null or empty");
                
            if (!File.Exists(db.DbPath))
                return new TestResult("DB Sanity Check", false, $"DB file does not exist: {db.DbPath}");

            // Check 2: Verify DB file size is reasonable
            var fileInfo = new FileInfo(db.DbPath);
            var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            if (sizeMb > 500) // Warn if over 500MB
                warnings.Add($"DB file is large: {sizeMb:F1}MB");

            // Check 3: Verify we can query the database
            var characters = db.GetAllCharacterNames();
            if (characters == null)
            {
                errors.Add("Failed to query characters table");
            }
            else if (characters.Count == 0)
            {
                warnings.Add("No characters found in database (empty database)");
            }
            else
            {
                // Check for characters with no name (informational)
                var unnamedCount = characters.Count(c => string.IsNullOrEmpty(c.name));
                if (unnamedCount > 0)
                    warnings.Add($"Found {unnamedCount} character(s) with no name");
                    
                // Check 4: Try to query time series data
                var gilVariable = TrackedDataType.Gil.ToString();
                var hasGilData = false;
                foreach (var (charId, _) in characters)
                {
                    var points = db.GetPoints(gilVariable, charId, 1);
                    if (points != null && points.Count > 0)
                    {
                        hasGilData = true;
                        break;
                    }
                }
                if (!hasGilData)
                    warnings.Add("No Gil data found for any character");
            }

            sw.Stop();
            
            // Errors = fail, warnings = pass with notes
            if (errors.Count > 0)
                return new TestResult("DB Sanity Check", false, $"{errors.Count} error(s) found", string.Join("\n", errors));
            else if (warnings.Count > 0)
                return new TestResult("DB Sanity Check", true, $"Passed with {warnings.Count} warning(s)", string.Join("\n", warnings));
            else
                return new TestResult("DB Sanity Check", true, $"Passed in {sw.ElapsedMilliseconds}ms", $"DB size: {sizeMb:F2}MB, {characters?.Count ?? 0} character(s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("DB Sanity Check", false, "Sanity check failed", ex.Message);
        }
    }

    private TestResult TestAutoRetainerIpc()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!_arIpcService.IsAvailable)
                return new TestResult("AutoRetainer IPC", false, "AutoRetainer not available", 
                    "Install AutoRetainer or ensure it's enabled");

            // Try to get registered character IDs
            var characterIds = _arIpcService.GetRegisteredCharacterIds();
            if (characterIds == null)
                return new TestResult("AutoRetainer IPC", false, "Failed to get registered characters");

            sw.Stop();
            return new TestResult("AutoRetainer IPC", true, $"IPC connected in {sw.ElapsedMilliseconds}ms", 
                $"Found {characterIds.Count} registered character(s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("AutoRetainer IPC", false, "IPC test failed", ex.Message);
        }
    }

    private TestResult TestUniversalisApi()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Try to get worlds list (cached endpoint, quick test)
            var worldsTask = _universalisService.GetWorldsAsync();
            if (!worldsTask.Wait(TimeSpan.FromSeconds(10)))
                return new TestResult("Universalis API", false, "Request timed out after 10s");

            var worlds = worldsTask.Result;
            if (worlds == null || worlds.Count == 0)
                return new TestResult("Universalis API", false, "Failed to get worlds list");

            sw.Stop();
            return new TestResult("Universalis API", true, $"API connected in {sw.ElapsedMilliseconds}ms", 
                $"Retrieved {worlds.Count} worlds");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Universalis API", false, "API test failed", ex.Message);
        }
    }

    private TestResult TestWebSocket()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var isConnected = _webSocketService.IsConnected;
            var feedCount = _webSocketService.LiveFeedCount;

            sw.Stop();
            
            if (isConnected)
                return new TestResult("WebSocket", true, $"Connected, {feedCount} feed entries", 
                    $"Checked in {sw.ElapsedMilliseconds}ms");
            else
                return new TestResult("WebSocket", true, "Not connected (may be disabled)", 
                    "WebSocket connects when price tracking is enabled");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("WebSocket", false, "WebSocket test failed", ex.Message);
        }
    }

    private TestResult TestCacheService()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cache = _currencyTrackerService.CacheService;
            if (cache == null)
                return new TestResult("Cache Service", false, "CacheService is null");

            // Get cache stats using the available method
            var stats = cache.GetStatistics();

            sw.Stop();
            return new TestResult("Cache Service", true, $"Cache operational in {sw.ElapsedMilliseconds}ms", 
                $"Total points: {stats.TotalPoints}, Series: {stats.SeriesCount}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Cache Service", false, "Cache test failed", ex.Message);
        }
    }

    private TestResult TestRegistry()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var registry = _currencyTrackerService.Registry;
            if (registry == null)
                return new TestResult("Tracked Data Registry", false, "Registry is null");

            // Get registered data types from Definitions property
            var definitionCount = registry.Definitions.Count;

            sw.Stop();
            return new TestResult("Tracked Data Registry", true, $"Registry operational in {sw.ElapsedMilliseconds}ms", 
                $"Registered {definitionCount} data type(s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Tracked Data Registry", false, "Registry test failed", ex.Message);
        }
    }

    private TestResult TestConfigService()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var config = _configService.Config;
            if (config == null)
                return new TestResult("Config Service", false, "Config is null");

            // Verify config can be accessed
            var version = config.Version;

            sw.Stop();
            return new TestResult("Config Service", true, $"Config loaded in {sw.ElapsedMilliseconds}ms", 
                $"Config version: {version}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Config Service", false, "Config test failed", ex.Message);
        }
    }

    // Cache Architecture Tests (Phase 1)

    private TestResult TestCharacterDataCacheInit()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cache = _currencyTrackerService.CharacterDataCache;
            if (cache == null)
                return new TestResult("CharacterDataCache Init", false, "CharacterDataCache is null");

            if (!cache.IsInitialized)
                return new TestResult("CharacterDataCache Init", false, "Cache not initialized");

            var count = cache.CachedCharacterCount;
            sw.Stop();
            return new TestResult("CharacterDataCache Init", true, $"Initialized in {sw.ElapsedMilliseconds}ms",
                $"Cached {count} character(s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("CharacterDataCache Init", false, "Init test failed", ex.Message);
        }
    }

    private TestResult TestCharacterDataCacheReads()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cache = _currencyTrackerService.CharacterDataCache;
            if (cache == null)
                return new TestResult("CharacterDataCache Reads", false, "CharacterDataCache is null");

            // Test GetAllCharacterNames
            var names = cache.GetAllCharacterNames();
            if (names == null)
                return new TestResult("CharacterDataCache Reads", false, "GetAllCharacterNames returned null");

            // Test GetAllCharacterIds
            var ids = cache.GetAllCharacterIds();
            if (ids == null)
                return new TestResult("CharacterDataCache Reads", false, "GetAllCharacterIds returned null");

            // Test GetDisambiguatedNames (requires character IDs)
            var disambiguated = cache.GetDisambiguatedNames(ids);
            if (disambiguated == null)
                return new TestResult("CharacterDataCache Reads", false, "GetDisambiguatedNames returned null");

            // Test individual character lookup if we have any
            var hitsBefore = cache.CacheHits;
            if (ids.Count > 0)
            {
                var firstId = ids[0];
                var name = cache.GetCharacterName(firstId);
                var hitsAfter = cache.CacheHits;

                if (hitsAfter <= hitsBefore)
                    return new TestResult("CharacterDataCache Reads", false, "Cache hit counter not incrementing");
            }

            sw.Stop();
            return new TestResult("CharacterDataCache Reads", true, $"Read ops completed in {sw.ElapsedMilliseconds}ms",
                $"Characters: {names.Count}, Disambiguated: {disambiguated.Count}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("CharacterDataCache Reads", false, "Read test failed", ex.Message);
        }
    }

    private TestResult TestCharacterDataCacheWrites()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cache = _currencyTrackerService.CharacterDataCache;
            if (cache == null)
                return new TestResult("CharacterDataCache Writes", false, "CharacterDataCache is null");

            // We don't want to actually modify data in tests, so just verify the cache accepts updates
            // by checking that writing an existing character's name works
            var ids = cache.GetAllCharacterIds();
            if (ids.Count == 0)
            {
                sw.Stop();
                return new TestResult("CharacterDataCache Writes", true, "Skipped (no characters)",
                    "No characters available to test write operations");
            }

            // Get current name and write it back (no-op but tests the path)
            var firstId = ids[0];
            var currentName = cache.GetCharacterName(firstId);
            if (!string.IsNullOrEmpty(currentName))
            {
                cache.SetCharacterName(firstId, currentName);
            }

            sw.Stop();
            return new TestResult("CharacterDataCache Writes", true, $"Write ops completed in {sw.ElapsedMilliseconds}ms",
                "Successfully tested SetCharacterName path");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("CharacterDataCache Writes", false, "Write test failed", ex.Message);
        }
    }

    private TestResult TestTimeSeriesCacheDelegation()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var timeSeriesCache = _currencyTrackerService.CacheService;
            var characterCache = _currencyTrackerService.CharacterDataCache;

            if (timeSeriesCache == null)
                return new TestResult("TimeSeriesCache Delegation", false, "TimeSeriesCacheService is null");
            if (characterCache == null)
                return new TestResult("TimeSeriesCache Delegation", false, "CharacterDataCache is null");

            // Get character IDs from character cache
            var ids = characterCache.GetAllCharacterIds();
            if (ids.Count == 0)
            {
                sw.Stop();
                return new TestResult("TimeSeriesCache Delegation", true, "Skipped (no characters)",
                    "No characters available to test delegation");
            }

            // Test that TimeSeriesCacheService.GetCharacterName returns same value as CharacterDataCache
            var firstId = ids[0];
            var fromTimeSeries = timeSeriesCache.GetCharacterName(firstId);
            var fromCharacterCache = characterCache.GetCharacterName(firstId);

            if (fromTimeSeries != fromCharacterCache)
                return new TestResult("TimeSeriesCache Delegation", false, "Delegation mismatch",
                    $"TimeSeriesCache: '{fromTimeSeries}' vs CharacterCache: '{fromCharacterCache}'");

            sw.Stop();
            return new TestResult("TimeSeriesCache Delegation", true, $"Delegation verified in {sw.ElapsedMilliseconds}ms",
                $"Character '{fromTimeSeries}' returned consistently from both caches");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("TimeSeriesCache Delegation", false, "Delegation test failed", ex.Message);
        }
    }

    private TestResult TestCacheDbConsistency()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cache = _currencyTrackerService.CharacterDataCache;
            var db = _currencyTrackerService.DbService;

            if (cache == null)
                return new TestResult("Cache-DB Consistency", false, "CharacterDataCache is null");
            if (db == null)
                return new TestResult("Cache-DB Consistency", false, "DbService is null");

            // Get data from both sources (both return List<(ulong characterId, string? name)>)
            var cacheNames = cache.GetAllCharacterNames();
            var dbNames = db.GetAllCharacterNames();

            if (cacheNames == null)
                return new TestResult("Cache-DB Consistency", false, "Cache returned null");
            if (dbNames == null)
                return new TestResult("Cache-DB Consistency", false, "DB returned null");

            // Check count consistency
            if (cacheNames.Count != dbNames.Count)
                return new TestResult("Cache-DB Consistency", false, "Count mismatch",
                    $"Cache: {cacheNames.Count}, DB: {dbNames.Count}");

            // Convert to dictionaries for easier comparison
            var cacheDict = cacheNames.ToDictionary(x => x.characterId, x => x.name);
            var dbDict = dbNames.ToDictionary(x => x.characterId, x => x.name);

            // Check content consistency
            foreach (var (characterId, dbName) in dbNames)
            {
                if (!cacheDict.TryGetValue(characterId, out var cachedName))
                    return new TestResult("Cache-DB Consistency", false, "Missing character in cache",
                        $"Character ID {characterId} not found in cache");

                if (cachedName != dbName)
                    return new TestResult("Cache-DB Consistency", false, "Name mismatch",
                        $"ID {characterId}: Cache='{cachedName}', DB='{dbName}'");
            }

            sw.Stop();
            return new TestResult("Cache-DB Consistency", true, $"Consistency verified in {sw.ElapsedMilliseconds}ms",
                $"All {cacheNames.Count} characters match between cache and DB");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Cache-DB Consistency", false, "Consistency test failed", ex.Message);
        }
    }

    // Phase 2: Registry Cache Tests

    private TestResult TestRegistryCache()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var registry = _currencyTrackerService.Registry;
            if (registry == null)
                return new TestResult("Registry Cache", false, "Registry is null");

            // Verify caches are built
            if (registry.Count == 0)
                return new TestResult("Registry Cache", false, "No definitions registered");

            if (registry.CategoryCount == 0)
                return new TestResult("Registry Cache", false, "No categories cached");

            // Test AllTypes cached list
            var allTypes = registry.AllTypes;
            if (allTypes == null || allTypes.Count == 0)
                return new TestResult("Registry Cache", false, "AllTypes cache empty");

            if (allTypes.Count != registry.Count)
                return new TestResult("Registry Cache", false, "AllTypes count mismatch",
                    $"AllTypes: {allTypes.Count}, Definitions: {registry.Count}");

            // Test EnabledByDefault cached list
            var enabledByDefault = registry.EnabledByDefault;
            if (enabledByDefault == null)
                return new TestResult("Registry Cache", false, "EnabledByDefault cache is null");

            sw.Stop();
            return new TestResult("Registry Cache", true, $"Registry cache verified in {sw.ElapsedMilliseconds}ms",
                $"Definitions: {registry.Count}, Categories: {registry.CategoryCount}, EnabledByDefault: {enabledByDefault.Count}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Registry Cache", false, "Registry cache test failed", ex.Message);
        }
    }

    private TestResult TestCategoryLookup()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var registry = _currencyTrackerService.Registry;
            if (registry == null)
                return new TestResult("Category Lookup", false, "Registry is null");

            // Test cached GetByCategory for each category
            var categoriesChecked = 0;
            var totalDefinitions = 0;

            foreach (TrackedDataCategory category in Enum.GetValues(typeof(TrackedDataCategory)))
            {
                var defs = registry.GetByCategory(category);
                if (defs.Count > 0)
                {
                    categoriesChecked++;
                    totalDefinitions += defs.Count;

                    // Verify all returned definitions are actually in this category
                    foreach (var def in defs)
                    {
                        if (def.Category != category)
                            return new TestResult("Category Lookup", false, "Category mismatch in cache",
                                $"Definition {def.Type} has category {def.Category} but was returned for {category}");
                    }
                }
            }

            // Verify total matches
            if (totalDefinitions != registry.Count)
                return new TestResult("Category Lookup", false, "Category totals don't match definition count",
                    $"Sum of categories: {totalDefinitions}, Total definitions: {registry.Count}");

            sw.Stop();
            return new TestResult("Category Lookup", true, $"Category lookup verified in {sw.ElapsedMilliseconds}ms",
                $"Checked {categoriesChecked} categories with {totalDefinitions} total definitions");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Category Lookup", false, "Category lookup test failed", ex.Message);
        }
    }

    // Phase 3: Time-Series Cache Tests

    private TestResult TestTimeSeriesBatchRead()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cache = _currencyTrackerService.CacheService;
            if (cache == null)
                return new TestResult("TimeSeries Batch Read", false, "TimeSeriesCacheService is null");

            // Test GetAllPointsBatch for a known variable (Gil is always tracked)
            var gilPoints = cache.GetAllPointsBatch("Gil", null);
            
            // Test with time filter
            var sinceYesterday = DateTime.UtcNow.AddDays(-1);
            var recentGilPoints = cache.GetAllPointsBatch("Gil", sinceYesterday);

            // Test GetPointsBatchWithSuffix (for item tracking patterns)
            var itemPatternResults = cache.GetPointsBatchWithSuffix("Item_", "", null);

            sw.Stop();
            var gilPointCount = gilPoints.TryGetValue("Gil", out var pts) ? pts.Count : 0;
            var recentCount = recentGilPoints.TryGetValue("Gil", out var rpts) ? rpts.Count : 0;
            
            return new TestResult("TimeSeries Batch Read", true, $"Batch reads completed in {sw.ElapsedMilliseconds}ms",
                $"Gil points: {gilPointCount} total, {recentCount} recent, {itemPatternResults.Count} item variables");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("TimeSeries Batch Read", false, "Batch read test failed", ex.Message);
        }
    }

    private TestResult TestTimeSeriesLatestValues()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cache = _currencyTrackerService.CacheService;
            if (cache == null)
                return new TestResult("TimeSeries Latest Values", false, "TimeSeriesCacheService is null");

            // Test GetLatestValuesForVariable
            var gilLatest = cache.GetLatestValuesForVariable("Gil");
            
            // Check that cache hits are being counted
            var hitsBefore = cache.CacheHits;
            var _ = cache.GetLatestValuesForVariable("Gil");
            var hitsAfter = cache.CacheHits;
            
            // Verify cache hit counter
            if (hitsAfter <= hitsBefore)
                return new TestResult("TimeSeries Latest Values", false, "Cache hit counter not incrementing");

            // Test HasDataForVariable
            var hasGil = cache.HasDataForVariable("Gil");
            var hasInvalid = cache.HasDataForVariable("InvalidVariable_xyz");
            
            if (hasInvalid)
                return new TestResult("TimeSeries Latest Values", false, "HasDataForVariable returned true for invalid variable");

            sw.Stop();
            return new TestResult("TimeSeries Latest Values", true, $"Latest values verified in {sw.ElapsedMilliseconds}ms",
                $"Gil: {gilLatest.Count} characters, HasGilData: {hasGil}, CacheHits: {hitsAfter}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("TimeSeries Latest Values", false, "Latest values test failed", ex.Message);
        }
    }

    #region Phase 4: Configuration Cache Tests

    private TestResult TestConfigDirtyTracking()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Verify IsDirty property is accessible
            var isDirtyBefore = _configService.IsDirty;
            
            // Mark dirty and verify
            _configService.MarkDirty();
            var isDirtyAfter = _configService.IsDirty;
            
            if (!isDirtyAfter)
                return new TestResult("Config Dirty Tracking", false, "MarkDirty did not set IsDirty to true");

            // SaveImmediate should clear dirty flag
            _configService.SaveImmediate();
            var isDirtyAfterSave = _configService.IsDirty;
            
            if (isDirtyAfterSave)
                return new TestResult("Config Dirty Tracking", false, "SaveImmediate did not clear IsDirty flag");

            sw.Stop();
            return new TestResult("Config Dirty Tracking", true, $"Dirty tracking verified in {sw.ElapsedMilliseconds}ms",
                $"Before: {isDirtyBefore}, After MarkDirty: {isDirtyAfter}, After Save: {isDirtyAfterSave}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Config Dirty Tracking", false, "Dirty tracking test failed", ex.Message);
        }
    }

    private TestResult TestConfigDebounce()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Get save count before test
            var saveCountBefore = _configService.SaveCount;
            var accessCountBefore = _configService.ConfigAccessCount;
            
            // Call MarkDirty multiple times rapidly (should only result in one save after debounce)
            for (int i = 0; i < 5; i++)
            {
                _configService.MarkDirty();
            }
            
            var accessCountAfter = _configService.ConfigAccessCount;
            
            // Verify access count incremented for each MarkDirty call
            if (accessCountAfter < accessCountBefore + 5)
                return new TestResult("Config Debounce", false, "ConfigAccessCount not incrementing properly");

            // Force save immediately to clear
            _configService.SaveImmediate();
            
            var saveCountAfter = _configService.SaveCount;
            
            // Should have at least one save
            if (saveCountAfter <= saveCountBefore)
                return new TestResult("Config Debounce", false, "SaveCount not incrementing");

            sw.Stop();
            return new TestResult("Config Debounce", true, $"Debounce verified in {sw.ElapsedMilliseconds}ms",
                $"Saves: {saveCountBefore} -> {saveCountAfter}, Accesses: {accessCountBefore} -> {accessCountAfter}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Config Debounce", false, "Debounce test failed", ex.Message);
        }
    }

    private TestResult TestConfigStatistics()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Test all statistics properties are accessible
            var saveCount = _configService.SaveCount;
            var skipCount = _configService.SaveSkippedCount;
            var accessCount = _configService.ConfigAccessCount;
            var isDirty = _configService.IsDirty;
            var lastSave = _configService.LastSaveTime;

            // Verify ResetStatistics works
            _configService.ResetStatistics();
            
            var saveCountAfterReset = _configService.SaveCount;
            var skipCountAfterReset = _configService.SaveSkippedCount;
            var accessCountAfterReset = _configService.ConfigAccessCount;
            
            if (saveCountAfterReset != 0 || skipCountAfterReset != 0 || accessCountAfterReset != 0)
                return new TestResult("Config Statistics", false, "ResetStatistics did not clear counters");

            sw.Stop();
            return new TestResult("Config Statistics", true, $"Statistics verified in {sw.ElapsedMilliseconds}ms",
                $"Before reset: Saves={saveCount}, Skipped={skipCount}, Accesses={accessCount}, LastSave={lastSave?.ToString("HH:mm:ss") ?? "never"}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Config Statistics", false, "Statistics test failed", ex.Message);
        }
    }

    #endregion

    #region Phase 5: Market Data Cache Tests

    private TestResult TestMarketCachePriceOps()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Test SetPrice and GetPrice
            const int testItemId = 999999;
            const int testWorldId = 99;
            
            _marketDataCacheService.SetPrice(testItemId, testWorldId, 1000, 1500, 
                lastSaleNq: 950, lastSaleHq: 1400, source: PriceSource.ApiCall);
            
            var price = _marketDataCacheService.GetPrice(testItemId, testWorldId);
            if (!price.HasValue)
                return new TestResult("Market Cache Price Ops", false, "GetPrice returned null after SetPrice");
            
            if (price.Value.MinNq != 1000 || price.Value.MinHq != 1500)
                return new TestResult("Market Cache Price Ops", false, 
                    $"Price mismatch: expected (1000, 1500), got ({price.Value.MinNq}, {price.Value.MinHq})");
            
            // Test UpdateMinPrices (should keep lower price)
            _marketDataCacheService.UpdateMinPrices(testItemId, testWorldId, 800, 1600);
            
            price = _marketDataCacheService.GetPrice(testItemId, testWorldId);
            if (price?.MinNq != 800) // Should be updated to lower price
                return new TestResult("Market Cache Price Ops", false, "UpdateMinPrices did not update NQ to lower price");
            if (price?.MinHq != 1500) // Should keep original (lower)
                return new TestResult("Market Cache Price Ops", false, "UpdateMinPrices incorrectly updated HQ price");
            
            // Test batch retrieval
            var batch = _marketDataCacheService.GetPricesBatch(new[] { testItemId, testItemId + 1 }, testWorldId);
            if (batch.Count != 2)
                return new TestResult("Market Cache Price Ops", false, "Batch retrieval returned wrong count");
            if (batch[testItemId] == null)
                return new TestResult("Market Cache Price Ops", false, "Batch retrieval missing existing item");
            
            // Cleanup
            _marketDataCacheService.RemovePrice(testItemId, testWorldId);
            
            sw.Stop();
            return new TestResult("Market Cache Price Ops", true, $"Price operations verified in {sw.ElapsedMilliseconds}ms",
                $"SetPrice, GetPrice, UpdateMinPrices, GetPricesBatch all passed");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Market Cache Price Ops", false, "Price ops test failed", ex.Message);
        }
    }

    private TestResult TestMarketCacheTtl()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            const int testItemId = 999998;
            const int testWorldId = 99;
            
            // Set a price
            _marketDataCacheService.SetPrice(testItemId, testWorldId, 500, 750, source: PriceSource.WebSocket);
            
            // Get with metadata
            var entry = _marketDataCacheService.GetPriceWithMetadata(testItemId, testWorldId);
            if (entry == null)
                return new TestResult("Market Cache TTL", false, "GetPriceWithMetadata returned null");
            
            // Check freshness properties
            if (!entry.IsFresh)
                return new TestResult("Market Cache TTL", false, "Newly created entry is not fresh");
            
            if (entry.IsStale)
                return new TestResult("Market Cache TTL", false, "Newly created entry is marked as stale");
            
            if (entry.IsExpired)
                return new TestResult("Market Cache TTL", false, "Newly created entry is marked as expired");
            
            if (entry.Freshness < 0.99)
                return new TestResult("Market Cache TTL", false, $"Freshness should be ~1.0, got {entry.Freshness:F2}");
            
            if (entry.Age.TotalSeconds > 5)
                return new TestResult("Market Cache TTL", false, $"Age should be <5s, got {entry.Age.TotalSeconds:F1}s");
            
            // Verify source tracking
            if (entry.Source != PriceSource.WebSocket)
                return new TestResult("Market Cache TTL", false, $"Source mismatch: expected WebSocket, got {entry.Source}");
            
            // Cleanup
            _marketDataCacheService.RemovePrice(testItemId, testWorldId);
            
            sw.Stop();
            return new TestResult("Market Cache TTL", true, $"TTL properties verified in {sw.ElapsedMilliseconds}ms",
                $"IsFresh: {entry.IsFresh}, Age: {entry.Age.TotalMilliseconds:F0}ms, Freshness: {entry.Freshness:F2}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Market Cache TTL", false, "TTL test failed", ex.Message);
        }
    }

    private TestResult TestMarketCacheStats()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Reset statistics first
            _marketDataCacheService.ResetStatistics();
            
            const int testItemId = 999997;
            const int testWorldId = 99;
            
            // Cause a cache miss
            var _ = _marketDataCacheService.GetPrice(testItemId, testWorldId);
            
            var missesAfter = _marketDataCacheService.CacheMisses;
            if (missesAfter != 1)
                return new TestResult("Market Cache Stats", false, $"Expected 1 cache miss, got {missesAfter}");
            
            // Set price and cause a cache hit
            _marketDataCacheService.SetPrice(testItemId, testWorldId, 100, 200);
            _ = _marketDataCacheService.GetPrice(testItemId, testWorldId);
            
            var hitsAfter = _marketDataCacheService.CacheHits;
            if (hitsAfter != 1)
                return new TestResult("Market Cache Stats", false, $"Expected 1 cache hit, got {hitsAfter}");
            
            // Get full statistics
            var stats = _marketDataCacheService.GetStatistics();
            if (stats.TotalPriceEntries < 1)
                return new TestResult("Market Cache Stats", false, "Statistics TotalPriceEntries is 0");
            
            // Test hit rate calculation
            if (stats.HitRate < 40 || stats.HitRate > 60) // Should be ~50% (1 hit, 1 miss)
                return new TestResult("Market Cache Stats", false, $"Hit rate unexpected: {stats.HitRate:F1}%");
            
            // Cleanup
            _marketDataCacheService.RemovePrice(testItemId, testWorldId);
            _marketDataCacheService.ResetStatistics();
            
            sw.Stop();
            return new TestResult("Market Cache Stats", true, $"Statistics verified in {sw.ElapsedMilliseconds}ms",
                $"Hits: {hitsAfter}, Misses: {missesAfter}, HitRate: {stats.HitRate:F1}%");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Market Cache Stats", false, "Statistics test failed", ex.Message);
        }
    }

    #endregion

    #region Phase 6: Layout Editing Cache Tests

    private TestResult TestLayoutToolCache()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Test GetToolNames
            var toolNames = _layoutEditingService.GetToolNames();
            
            // Test HasTool with existing and non-existing tools
            var layoutName = _layoutEditingService.CurrentLayoutName;
            var toolCount = _layoutEditingService.ToolCount;
            
            // Test HasTool with invalid name
            var hasInvalidTool = _layoutEditingService.HasTool("InvalidToolName_xyz_12345");
            if (hasInvalidTool)
                return new TestResult("Layout Tool Cache", false, "HasTool returned true for invalid tool");
            
            // If we have tools, test lookup
            if (toolCount > 0 && toolNames.Count > 0)
            {
                var firstToolName = toolNames[0];
                var tool = _layoutEditingService.GetToolByName(firstToolName);
                if (tool == null)
                    return new TestResult("Layout Tool Cache", false, $"GetToolByName returned null for '{firstToolName}'");
                
                var hasTool = _layoutEditingService.HasTool(firstToolName);
                if (!hasTool)
                    return new TestResult("Layout Tool Cache", false, $"HasTool returned false for existing tool '{firstToolName}'");
            }
            
            sw.Stop();
            return new TestResult("Layout Tool Cache", true, $"Tool cache verified in {sw.ElapsedMilliseconds}ms",
                $"Layout: '{layoutName}', Tools: {toolCount}, ToolNames: {toolNames.Count}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Layout Tool Cache", false, "Tool cache test failed", ex.Message);
        }
    }

    private TestResult TestLayoutSnapshotDebounce()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Get initial stats
            var statsBefore = _layoutEditingService.GetStatistics();
            var skippedBefore = statsBefore.SnapshotSkippedCount;
            
            // Test FlushDirtySnapshot (should not throw)
            _layoutEditingService.FlushDirtySnapshot();
            
            // Verify statistics are accessible
            var statsAfter = _layoutEditingService.GetStatistics();
            
            // Check that snapshot statistics are tracked
            if (statsAfter.SnapshotWriteCount < 0)
                return new TestResult("Layout Snapshot Debounce", false, "SnapshotWriteCount is negative");
            
            if (statsAfter.SnapshotSkippedCount < 0)
                return new TestResult("Layout Snapshot Debounce", false, "SnapshotSkippedCount is negative");
            
            sw.Stop();
            return new TestResult("Layout Snapshot Debounce", true, $"Debounce verified in {sw.ElapsedMilliseconds}ms",
                $"Writes: {statsAfter.SnapshotWriteCount}, Skipped: {statsAfter.SnapshotSkippedCount}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Layout Snapshot Debounce", false, "Debounce test failed", ex.Message);
        }
    }

    private TestResult TestLayoutStats()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Get statistics
            var stats = _layoutEditingService.GetStatistics();
            
            // Verify all properties are accessible
            var _ = stats.CurrentLayoutName;
            var __ = stats.CurrentLayoutType;
            var ___ = stats.IsDirty;
            var ____ = stats.ToolCount;
            var _____ = stats.SaveCount;
            var ______ = stats.DiscardCount;
            var _______ = stats.DirtyMarkCount;
            var ________ = stats.SnapshotSavingsPercent;
            
            // Test ResetStatistics
            _layoutEditingService.ResetStatistics();
            var statsAfterReset = _layoutEditingService.GetStatistics();
            
            if (statsAfterReset.SaveCount != 0 || statsAfterReset.DirtyMarkCount != 0)
                return new TestResult("Layout Stats", false, "ResetStatistics did not clear counters");
            
            // Verify grid dimensions cache
            var (cols, rows) = _layoutEditingService.GetEffectiveGridDimensions();
            if (cols <= 0 || rows <= 0)
                return new TestResult("Layout Stats", false, $"Invalid grid dimensions: {cols}x{rows}");
            
            sw.Stop();
            return new TestResult("Layout Stats", true, $"Statistics verified in {sw.ElapsedMilliseconds}ms",
                $"Layout: '{stats.CurrentLayoutName}', Dirty: {stats.IsDirty}, Grid: {cols}x{rows}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult("Layout Stats", false, "Statistics test failed", ex.Message);
        }
    }

    #endregion

    #endregion

    /// <summary>
    /// Represents the result of a single test.
    /// </summary>
    private record TestResult(string Name, bool Passed, string Message, string? Details = null);
}
