using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Runs core service tests on plugin startup and logs errors for any failures.
/// Tests run on a background thread to avoid blocking the main thread.
/// </summary>
public sealed class StartupTestService : IRequiredService
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService _configService;

    public StartupTestService(
        CurrencyTrackerService currencyTrackerService,
        ConfigurationService configService)
    {
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;

        Task.Run(RunStartupTests);
    }

    private void RunStartupTests()
    {
        var tests = new List<(string Name, Func<(bool Passed, string Message, string? Details)> Test)>
        {
            ("DB Connection", TestDbConnection),
            ("DB Read/Write", TestDbReadWrite),
            ("DB Sanity Check", TestDbSanity),
            ("Cache Service", TestCacheService),
            ("Tracked Data Registry", TestRegistry),
            ("Config Service", TestConfigService),
        };

        var passed = 0;
        var failed = 0;

        foreach (var (name, test) in tests)
        {
            try
            {
                var result = test();
                if (result.Passed)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    var detail = result.Details != null ? $" — {result.Details}" : "";
                    LogService.Error(LogCategory.Database, $"[StartupTest] FAIL: {name}: {result.Message}{detail}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                LogService.Error(LogCategory.Database, $"[StartupTest] FAIL: {name}: Exception thrown — {ex.Message}");
            }
        }

        if (failed > 0)
            LogService.Warning($"[StartupTest] Completed: {passed} passed, {failed} failed");
        else
            LogService.Info($"[StartupTest] All {passed} tests passed");
    }

    private (bool Passed, string Message, string? Details) TestDbConnection()
    {
        var db = _currencyTrackerService.DbService;
        if (db == null)
            return (false, "DbService is null", null);

        if (string.IsNullOrEmpty(db.DbPath))
            return (false, "DB path is null or empty", null);

        if (!File.Exists(db.DbPath))
            return (false, $"DB file does not exist: {db.DbPath}", null);

        return (true, "Connected", $"Path: {db.DbPath}");
    }

    private (bool Passed, string Message, string? Details) TestDbReadWrite()
    {
        var db = _currencyTrackerService.DbService;
        if (db == null)
            return (false, "DbService is null", null);

        var characters = db.GetAllCharacterNames();
        if (characters == null)
            return (false, "Failed to read characters", null);

        var testSeriesId = db.GetOrCreateSeries("__test_series__", 0);

        return (true, "Read/Write test passed", $"Found {characters.Count} character(s)");
    }

    private (bool Passed, string Message, string? Details) TestDbSanity()
    {
        var errors = new List<string>();

        var db = _currencyTrackerService.DbService;
        if (db == null)
            return (false, "DbService is null", null);

        if (string.IsNullOrEmpty(db.DbPath))
            return (false, "DB path is null or empty", null);

        if (!File.Exists(db.DbPath))
            return (false, $"DB file does not exist: {db.DbPath}", null);

        var fileInfo = new FileInfo(db.DbPath);
        var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
        if (sizeMb > 500)
            errors.Add($"DB file is very large: {sizeMb:F1}MB");

        var characters = db.GetAllCharacterNames();
        if (characters == null)
            errors.Add("Failed to query characters table");

        if (errors.Count > 0)
            return (false, $"{errors.Count} error(s) found", string.Join("; ", errors));

        return (true, "Passed", $"DB size: {sizeMb:F2}MB, {characters?.Count ?? 0} character(s)");
    }

    private (bool Passed, string Message, string? Details) TestCacheService()
    {
        var cache = _currencyTrackerService.CacheService;
        if (cache == null)
            return (false, "CacheService is null", null);

        var stats = cache.GetStatistics();

        return (true, "Cache operational", $"Total points: {stats.TotalPoints}, Series: {stats.SeriesCount}");
    }

    private (bool Passed, string Message, string? Details) TestRegistry()
    {
        var registry = _currencyTrackerService.Registry;
        if (registry == null)
            return (false, "Registry is null", null);

        var definitionCount = registry.Definitions.Count;

        return (true, "Registry operational", $"Registered {definitionCount} data type(s)");
    }

    private (bool Passed, string Message, string? Details) TestConfigService()
    {
        var config = _configService.Config;
        if (config == null)
            return (false, "Config is null", null);

        var version = config.Version;

        return (true, "Config loaded", $"Config version: {version}");
    }
}
