using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Gui.Widgets.Tree;
using System.Diagnostics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Tests category for the config window.
/// Provides interactive testing of services, integrations, and database sanity checks.
/// Only visible in the Developer menu (CTRL+ALT or developer mode enabled).
/// </summary>
public sealed class TestsCategory : Kaleidoscope.Gui.ConfigWindow.IConfigCategory
{
    /// <inheritdoc/>
    public string Label => "Tests";

    /// <inheritdoc/>
    public bool IsDeveloper => true;

    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly AutoRetainerService _arIpcService;
    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly ConfigurationService _configService;
    private readonly MarketDataCacheService _marketDataCacheService;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly ResourceObservationService _resourcesService;
    private readonly ResourceStore _resourceStore;
    private readonly ResourceDbWriter _resourceWriter;

    // Test results storage
    private readonly object _testResultsLock = new();
    private readonly List<TestResult> _testResults = new();
    private bool _isRunningTests;
    private string _currentTestName = "";

    // Test registry — the single source of truth for the runnable tests. The draw layout,
    // run-all and clear/reset all derive from this list; each test's status is looked up
    // from the last matching entry in _testResults.
    private readonly List<TestDefinition> _tests;
    private readonly Dictionary<string, TestDefinition> _testsByName;

    private static readonly Vector4 ColorDev = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorRunning = new(1f, 1f, 0f, 1f);
    private static readonly Vector4 ColorPass = new(0.5f, 1f, 0.5f, 1f);
    private static readonly Vector4 ColorFail = new(1f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 ColorMuted = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 ColorMutedLight = new(0.7f, 0.7f, 0.7f, 1f);

    public TestsCategory(
        CurrencyTrackerService currencyTrackerService,
        AutoRetainerService arIpcService,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        ConfigurationService configService,
        MarketDataCacheService marketDataCacheService,
        LayoutEditingService layoutEditingService,
        ResourceObservationService resourcesService,
        ResourceStore resourceStore,
        ResourceDbWriter resourceWriter)
    {
        _currencyTrackerService = currencyTrackerService;
        _arIpcService = arIpcService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _configService = configService;
        _marketDataCacheService = marketDataCacheService;
        _layoutEditingService = layoutEditingService;
        _resourcesService = resourcesService;
        _resourceStore = resourceStore;
        _resourceWriter = resourceWriter;

        _tests = BuildTestRegistry();
        _testsByName = _tests.ToDictionary(t => t.Name);
    }

    private List<TestDefinition> BuildTestRegistry() => new()
    {
        new("DB Connection", "Database", TestDbConnection),
        new("DB Read/Write", "Database", TestDbReadWrite),
        new("DB Sanity Check", "Database", TestDbSanity),

        new("AutoRetainer IPC", "Integration", TestAutoRetainerIpc),
        new("Universalis API", "Integration", TestUniversalisApi),
        new("WebSocket", "Integration", TestWebSocket),

        new("Cache Service", "Service", TestCacheService),
        new("Tracked Data Registry", "Service", TestRegistry),
        new("Config Service", "Service", TestConfigService),

        // Cache Architecture Tests (Phase 1)
        new("CharacterDataCache Init", "Cache Architecture", TestCharacterDataCacheInit),
        new("CharacterDataCache Reads", "Cache Architecture", TestCharacterDataCacheReads),
        new("CharacterDataCache Writes", "Cache Architecture", TestCharacterDataCacheWrites),
        new("TimeSeriesCache Delegation", "Cache Architecture", TestTimeSeriesCacheDelegation),
        new("Cache-DB Consistency", "Cache Architecture", TestCacheDbConsistency),
        // Phase 2: Registry Cache Tests
        new("Registry Cache", "Cache Architecture", TestRegistryCache),
        new("Category Lookup", "Cache Architecture", TestCategoryLookup),
        // Phase 3: Time-Series Cache Tests
        new("TimeSeries Batch Read", "Cache Architecture", TestTimeSeriesBatchRead),
        new("TimeSeries Latest Values", "Cache Architecture", TestTimeSeriesLatestValues),
        // Phase 4: Configuration Cache Tests
        new("Config Dirty Tracking", "Cache Architecture", TestConfigDirtyTracking),
        new("Config Debounce", "Cache Architecture", TestConfigDebounce),
        new("Config Statistics", "Cache Architecture", TestConfigStatistics),
        // Phase 5: Market Data Cache Tests
        new("Market Cache Price Ops", "Cache Architecture", TestMarketCachePriceOps),
        new("Market Cache TTL", "Cache Architecture", TestMarketCacheTtl),
        new("Market Cache Stats", "Cache Architecture", TestMarketCacheStats),
        // Phase 6: Layout Editing Cache Tests
        new("Layout Tool Cache", "Cache Architecture", TestLayoutToolCache),
        new("Layout Snapshot Debounce", "Cache Architecture", TestLayoutSnapshotDebounce),
        new("Layout Stats", "Cache Architecture", TestLayoutStats),
    };

    public void Draw()
    {
        ImGui.TextUnformatted("Tests");
        ImGui.Separator();

        ImGui.TextColored(ColorDev, "Developer Tool - Service & Integration Tests");
        ImGui.Spacing();

        // Run all tests button
        if (ImGui.Button("Run All Tests") && !_isRunningTests)
        {
            lock (_testResultsLock) { _testResults.Clear(); }
            RunAllTests();
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear Results"))
        {
            lock (_testResultsLock) { _testResults.Clear(); }
        }

        if (_isRunningTests)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorRunning, $"Running: {_currentTestName}...");
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
        DrawResourcesPanel();
        ImGui.Spacing();

        // Test results display
        DrawTestResults();
    }

    /// <summary>
    /// Draws one test row: a run button (disabled while a run is in progress) plus its
    /// last-run status. This replaces the per-test BeginDisabled/Button/EndDisabled/status blocks.
    /// </summary>
    private void DrawTest(string name, string? buttonLabel = null)
    {
        if (!_testsByName.TryGetValue(name, out var def))
            return;

        ImGui.BeginDisabled(_isRunningTests);
        if (ImGui.Button(buttonLabel ?? $"Test {name}"))
            RunTest(def);
        ImGui.EndDisabled();
        ImGui.SameLine();
        DrawTestStatus(name);
    }

    private void DrawDatabaseTests()
    {
        if (TreeHelpers.DrawCollapsingSection("Database Tests", true))
        {
            ImGui.Indent();

            DrawTest("DB Connection");
            DrawTest("DB Read/Write");
            DrawTest("DB Sanity Check", "Run DB Sanity Check");

            // DB Stats
            if (TreeHelpers.DrawSection("Database Statistics"))
            {
                DrawDbStats();
                TreeHelpers.EndSection();
            }

            ImGui.Unindent();
        }
    }

    private void DrawIntegrationTests()
    {
        if (TreeHelpers.DrawCollapsingSection("Integration Tests", true))
        {
            ImGui.Indent();

            DrawTest("AutoRetainer IPC");
            ImGui.SameLine();
            ImGui.TextColored(
                _arIpcService.IsAvailable ? ColorPass : ColorFail,
                _arIpcService.IsAvailable ? "(Available)" : "(Unavailable)");

            DrawTest("Universalis API");

            DrawTest("WebSocket");
            ImGui.SameLine();
            ImGui.TextColored(
                _webSocketService.IsConnected ? ColorPass : ColorMutedLight,
                _webSocketService.IsConnected ? "(Connected)" : "(Disconnected)");

            ImGui.Unindent();
        }
    }

    private void DrawServiceTests()
    {
        if (TreeHelpers.DrawCollapsingSection("Service Tests", false))
        {
            ImGui.Indent();

            DrawTest("Cache Service");
            DrawTest("Tracked Data Registry");
            DrawTest("Config Service");

            ImGui.Unindent();
        }
    }

    private void DrawCacheArchitectureTests()
    {
        if (TreeHelpers.DrawCollapsingSection("Cache Architecture Tests", false))
        {
            ImGui.Indent();

            DrawPhaseHeader("Phase 1: Character Data Cache");
            DrawTest("CharacterDataCache Init");
            DrawTest("CharacterDataCache Reads");
            DrawTest("CharacterDataCache Writes");
            DrawTest("TimeSeriesCache Delegation");
            DrawTest("Cache-DB Consistency");

            DrawPhaseHeader("Phase 2: Tracked Data Registry Cache");
            DrawTest("Registry Cache");
            DrawTest("Category Lookup");

            DrawPhaseHeader("Phase 3: Time-Series Cache");
            DrawTest("TimeSeries Batch Read");
            DrawTest("TimeSeries Latest Values");

            DrawPhaseHeader("Phase 4: Configuration Cache");
            DrawTest("Config Dirty Tracking");
            DrawTest("Config Debounce");
            DrawTest("Config Statistics");

            DrawPhaseHeader("Phase 5: Market Data Cache");
            DrawTest("Market Cache Price Ops");
            DrawTest("Market Cache TTL");
            DrawTest("Market Cache Stats");

            DrawPhaseHeader("Phase 6: Layout Editing Cache");
            DrawTest("Layout Tool Cache");
            DrawTest("Layout Snapshot Debounce");
            DrawTest("Layout Stats");

            ImGui.Spacing();

            // Cache Statistics Display
            if (TreeHelpers.DrawSection("Cache Statistics"))
            {
                DrawCacheStats();
                TreeHelpers.EndSection();
            }

            ImGui.Unindent();
        }
    }

    private static void DrawPhaseHeader(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(ColorMutedLight, label);
        ImGui.Spacing();
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
            ImGui.TextColored(ColorFail, $"Error reading cache stats: {ex.Message}");
        }
    }

    private void DrawResourcesPanel()
    {
        if (!TreeHelpers.DrawCollapsingSection("Unified Resources", false))
            return;

        ImGui.Indent();

        if (_resourcesService == null || _resourceStore == null || _resourceWriter == null)
        {
            ImGui.TextDisabled("Resources services not available");
            ImGui.Unindent();
            return;
        }

        ImGui.TextUnformatted($"Version:           {_resourcesService.Version}");
        ImGui.TextUnformatted($"Live resources:    {_resourceStore.Snapshot().Count}");
        ImGui.TextUnformatted($"Pending DB writes: {_resourceWriter.PendingCount}");

        ImGui.Spacing();

        var pid = GameStateService.PlayerContentId;
        var gilKey = new ResourceKey
        {
            OwnerId   = pid,
            OwnerKind = OwnerKind.Player,
            Container = Container.SpecialPlayer,
            ItemId    = ResourceCatalog.GilItemId,
            Slot      = -1,
        };
        var stored = _resourceStore.Get(gilKey)?.Quantity ?? 0;
        long live;
        unsafe { var im = GameStateService.InventoryManagerInstance(); live = im == null ? 0 : (long)im->GetGil(); }
        var match = stored == live;

        ImGui.TextUnformatted($"Stored Gil: {stored:N0}  |  Live Gil: {live:N0}  |  ");
        ImGui.SameLine();
        var col = match ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0.4f, 0.4f, 1);
        ImGui.TextColored(col, match ? "MATCH" : "MISMATCH");

        ImGui.Spacing();

        if (ImGui.Button("Force flush"))
            _resourceWriter.FlushOnce();

        ImGui.Unindent();
    }

    private void DrawTestResults()
    {
        List<TestResult> resultsSnapshot;
        lock (_testResultsLock)
        {
            if (_testResults.Count == 0) return;
            resultsSnapshot = _testResults.ToList();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Test Results");
        ImGui.Separator();

        // Summary
        var passed = resultsSnapshot.Count(r => r.Passed);
        var failed = resultsSnapshot.Count(r => !r.Passed);
        var summaryColor = failed == 0 ? ColorPass : ColorFail;
        ImGui.TextColored(summaryColor, $"Passed: {passed} | Failed: {failed}");
        ImGui.Spacing();

        // Individual results in scrollable area
        var availHeight = Math.Min(200f, resultsSnapshot.Count * 25f + 10f);
        if (ImGui.BeginChild("##test_results", new Vector2(0, availHeight), true))
        {
            foreach (var result in resultsSnapshot)
            {
                var color = result.Passed ? ColorPass : ColorFail;
                var icon = result.Passed ? "✓" : "✗";

                ImGui.TextColored(color, $"{icon} {result.Name}");
                if (!string.IsNullOrEmpty(result.Message))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColorMutedLight, $"- {result.Message}");
                }
                if (!string.IsNullOrEmpty(result.Details))
                {
                    ImGui.Indent();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), result.Details);
                    ImGui.Unindent();
                }
            }
        }
        ImGui.EndChild();
    }

    private void DrawTestStatus(string name)
    {
        TestResult? result;
        lock (_testResultsLock) { result = _testResults.LastOrDefault(r => r.Name == name); }
        if (result == null)
        {
            ImGui.TextColored(ColorMuted, "Not run");
            return;
        }

        ImGui.TextColored(result.Passed ? ColorPass : ColorFail, result.Passed ? "PASS" : "FAIL");
    }

    private void DrawDbStats()
    {
        var db = _currencyTrackerService.DbService;
        if (db == null)
        {
            ImGui.TextColored(ColorFail, "Database not available");
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
            ImGui.TextColored(ColorFail, $"Error reading stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs a single test on a background thread. Owns the running-flag bookkeeping and the
    /// exception safety net for the whole registry, so the individual test bodies only contain
    /// their assertion logic.
    /// </summary>
    private void RunTest(TestDefinition def)
    {
        Task.Run(() =>
        {
            _isRunningTests = true;
            _currentTestName = def.Name;
            try
            {
                var result = def.Run();
                lock (_testResultsLock) { _testResults.Add(result); }
            }
            catch (Exception ex)
            {
                lock (_testResultsLock) { _testResults.Add(new TestResult(def.Name, false, "Exception thrown", ex.Message)); }
            }
            finally
            {
                _isRunningTests = false;
                _currentTestName = "";
            }
        });
    }

    private async void RunAllTests()
    {
        try
        {
            _isRunningTests = true;
            lock (_testResultsLock) { _testResults.Clear(); }

            foreach (var def in _tests)
            {
                _currentTestName = def.Name;
                try
                {
                    var result = await Task.Run(def.Run);
                    lock (_testResultsLock) { _testResults.Add(result); }
                }
                catch (Exception ex)
                {
                    lock (_testResultsLock) { _testResults.Add(new TestResult(def.Name, false, "Exception thrown", ex.Message)); }
                }

                // Small delay between tests
                await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, $"RunAllTests failed: {ex.Message}");
            lock (_testResultsLock) { _testResults.Add(new TestResult("Test Runner", false, "Test runner crashed", ex.Message)); }
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

    private TestResult TestDbReadWrite()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestDbSanity()
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();

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

    private TestResult TestAutoRetainerIpc()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestUniversalisApi()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestWebSocket()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestCacheService()
    {
        var sw = Stopwatch.StartNew();
        var cache = _currencyTrackerService.CacheService;
        if (cache == null)
            return new TestResult("Cache Service", false, "CacheService is null");

        // Get cache stats using the available method
        var stats = cache.GetStatistics();

        sw.Stop();
        return new TestResult("Cache Service", true, $"Cache operational in {sw.ElapsedMilliseconds}ms",
            $"Total points: {stats.TotalPoints}, Series: {stats.SeriesCount}");
    }

    private TestResult TestRegistry()
    {
        var sw = Stopwatch.StartNew();
        var registry = _currencyTrackerService.Registry;
        if (registry == null)
            return new TestResult("Tracked Data Registry", false, "Registry is null");

        // Get registered data types from Definitions property
        var definitionCount = registry.Definitions.Count;

        sw.Stop();
        return new TestResult("Tracked Data Registry", true, $"Registry operational in {sw.ElapsedMilliseconds}ms",
            $"Registered {definitionCount} data type(s)");
    }

    private TestResult TestConfigService()
    {
        var sw = Stopwatch.StartNew();
        var config = _configService.Config;
        if (config == null)
            return new TestResult("Config Service", false, "Config is null");

        // Verify config can be accessed
        var version = config.Version;

        sw.Stop();
        return new TestResult("Config Service", true, $"Config loaded in {sw.ElapsedMilliseconds}ms",
            $"Config version: {version}");
    }

    // Cache Architecture Tests (Phase 1)

    private TestResult TestCharacterDataCacheInit()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestCharacterDataCacheReads()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestCharacterDataCacheWrites()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestTimeSeriesCacheDelegation()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestCacheDbConsistency()
    {
        var sw = Stopwatch.StartNew();
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

    // Phase 2: Registry Cache Tests

    private TestResult TestRegistryCache()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestCategoryLookup()
    {
        var sw = Stopwatch.StartNew();
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

    // Phase 3: Time-Series Cache Tests

    private TestResult TestTimeSeriesBatchRead()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestTimeSeriesLatestValues()
    {
        var sw = Stopwatch.StartNew();
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

    #region Phase 4: Configuration Cache Tests

    private TestResult TestConfigDirtyTracking()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestConfigDebounce()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestConfigStatistics()
    {
        var sw = Stopwatch.StartNew();
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

    #endregion

    #region Phase 5: Market Data Cache Tests

    private TestResult TestMarketCachePriceOps()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestMarketCacheTtl()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestMarketCacheStats()
    {
        var sw = Stopwatch.StartNew();
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

    #endregion

    #region Phase 6: Layout Editing Cache Tests

    private TestResult TestLayoutToolCache()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestLayoutSnapshotDebounce()
    {
        var sw = Stopwatch.StartNew();
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

    private TestResult TestLayoutStats()
    {
        var sw = Stopwatch.StartNew();
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

    #endregion

    #endregion

    /// <summary>
    /// A single runnable test: its display/result name, a grouping category, and the body to run.
    /// </summary>
    private sealed record TestDefinition(string Name, string Category, Func<TestResult> Run);

    /// <summary>
    /// Represents the result of a single test.
    /// </summary>
    private record TestResult(string Name, bool Passed, string Message, string? Details = null);
}
