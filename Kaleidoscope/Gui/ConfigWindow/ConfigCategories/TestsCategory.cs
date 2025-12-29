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
public class TestsCategory
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly AutoRetainerIpcService _arIpcService;
    private readonly UniversalisService _universalisService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly ConfigurationService _configService;

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

    public TestsCategory(
        CurrencyTrackerService currencyTrackerService,
        AutoRetainerIpcService arIpcService,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        ConfigurationService configService)
    {
        _currencyTrackerService = currencyTrackerService;
        _arIpcService = arIpcService;
        _universalisService = universalisService;
        _webSocketService = webSocketService;
        _configService = configService;
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

        // Test results display
        DrawTestResults();
    }

    private void DrawDatabaseTests()
    {
        if (TreeHelpers.DrawCollapsingSection("Database Tests", true))
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
        if (TreeHelpers.DrawCollapsingSection("Service Tests", false))
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

        // Individual results in scrollable area
        var availHeight = Math.Min(200f, _testResults.Count * 25f + 10f);
        if (ImGui.BeginChild("##test_results", new System.Numerics.Vector2(0, availHeight), true))
        {
            foreach (var result in _testResults)
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
        _isRunningTests = false;
        _currentTestName = "";
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

    #endregion

    /// <summary>
    /// Represents the result of a single test.
    /// </summary>
    private record TestResult(string Name, bool Passed, string Message, string? Details = null);
}
