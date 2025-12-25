using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;
using System.Threading.Channels;

namespace Kaleidoscope.Services;

/// <summary>
/// Background service that samples game data (currencies, inventories) and persists it via KaleidoscopeDbService.
/// Uses event-driven hooks for real-time updates with a fallback periodic sync.
/// Database writes are offloaded to a background thread to avoid game lag.
/// </summary>
/// <remarks>
/// Primary: Reacts to inventory/currency change events from InventoryChangeService.
/// Fallback: Periodic timer sync to catch any missed updates (runs less frequently).
/// Threading: Game data reads happen on main thread, database writes happen on background thread.
/// </remarks>
public sealed class SamplerService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly FilenameService _filenames;
    private readonly ConfigurationService _configService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly TrackedDataRegistry _registry;
    private readonly InventoryChangeService _inventoryChangeService;
    private readonly TimeSeriesCacheService _cacheService;

    // Background thread for database writes
    private readonly Channel<SampleWorkItem> _sampleQueue;
    private readonly Task _backgroundWorker;
    private readonly CancellationTokenSource _cts = new();

    private volatile bool _enabled = true;

    /// <summary>
    /// Gets or sets whether sampling is enabled.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Gets the effective sampling interval in milliseconds.
    /// This is controlled by InventoryChangeService's polling interval.
    /// </summary>
    public int IntervalMs
    {
        get => 1000; // InventoryChangeService polls every 1s
        set { /* Interval is now controlled by InventoryChangeService */ }
    }

    /// <summary>
    /// Gets the underlying database service for direct data access.
    /// </summary>
    public KaleidoscopeDbService DbService => _dbService;

    /// <summary>
    /// Gets the tracked data registry.
    /// </summary>
    public TrackedDataRegistry Registry => _registry;

    /// <summary>
    /// Gets the in-memory cache service for fast data access.
    /// </summary>
    public TimeSeriesCacheService CacheService => _cacheService;

    /// <summary>
    /// Event fired when inventory value history is modified (e.g., sale record deleted).
    /// </summary>
    public event Action? OnInventoryValueHistoryChanged;

    /// <summary>
    /// Notifies subscribers that inventory value history has changed.
    /// Call this after modifying inventory_value_history records.
    /// </summary>
    public void NotifyInventoryValueHistoryChanged()
    {
        OnInventoryValueHistoryChanged?.Invoke();
    }

    private readonly AutoRetainerIpcService _arIpc;

    public SamplerService(
        IPluginLog log,
        FilenameService filenames,
        ConfigurationService configService,
        AutoRetainerIpcService arIpc,
        TrackedDataRegistry registry,
        InventoryChangeService inventoryChangeService,
        TimeSeriesCacheService cacheService)
    {
        _log = log;
        _filenames = filenames;
        _configService = configService;
        _arIpc = arIpc;
        _registry = registry;
        _inventoryChangeService = inventoryChangeService;
        _cacheService = cacheService;

        // Create the database service with configured cache size
        var cacheSizeMb = configService.SamplerConfig.DatabaseCacheSizeMb;
        _dbService = new KaleidoscopeDbService(filenames.DatabasePath, cacheSizeMb);

        // Initialize background work queue (unbounded, single consumer)
        _sampleQueue = Channel.CreateUnbounded<SampleWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Start the background worker thread for database writes
        _backgroundWorker = Task.Factory.StartNew(
            ProcessSampleQueueAsync,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        ).Unwrap();

        // Perform one-time migration of stored names
        _dbService.MigrateStoredNames();

        // Load initial values from config
        _enabled = configService.SamplerConfig.SamplerEnabled;

        // Auto-import from AutoRetainer on startup
        ImportFromAutoRetainer();

        // Populate cache from database on startup
        PopulateCacheFromDatabase();

        // Subscribe to inventory change events - uses pre-captured values to avoid re-reading game memory
        _inventoryChangeService.OnValuesChanged += OnValuesChanged;

        _log.Information("[SamplerService] Initialized with background thread for database writes");
    }

    /// <summary>
    /// Imports character data from AutoRetainer if available.
    /// This runs automatically on plugin startup and can be called manually.
    /// </summary>
    public void ImportFromAutoRetainer()
    {
        try
        {
            if (!_arIpc.IsAvailable)
            {
                LogService.Debug("AutoRetainer not available for auto-import");
                return;
            }
            
            var characters = _arIpc.GetAllCharacterData();
            if (characters.Count == 0)
            {
                LogService.Debug("No characters returned from AutoRetainer");
                return;
            }
            
            var importCount = 0;
            var updatedCount = 0;
            
            foreach (var (name, world, gil, cid) in characters)
            {
                if (cid == 0 || string.IsNullOrEmpty(name)) continue;
                
                // Always save/overwrite the character name from AutoRetainer (AR data takes priority)
                _dbService.SaveCharacterName(cid, name);
                
                // Create a series if it doesn't exist
                var seriesId = _dbService.GetOrCreateSeries("Gil", cid);
                if (seriesId.HasValue)
                {
                    // Check if we already have data for this character
                    var existingValue = _dbService.GetLastValueForCharacter("Gil", cid);
                    
                    // AutoRetainer data takes priority - add sample if gil differs from latest
                    if (existingValue == null || (long)existingValue.Value != gil)
                    {
                        _dbService.SaveSampleIfChanged("Gil", cid, gil);
                        if (existingValue != null) updatedCount++;
                    }
                    importCount++;
                }
            }
            
            if (importCount > 0)
            {
                var msg = $"Auto-imported {importCount} characters from AutoRetainer";
                if (updatedCount > 0)
                    msg += $" ({updatedCount} updated with new gil values)";
                LogService.Info(msg);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"AutoRetainer auto-import failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when inventory/currency values change with pre-captured values.
    /// Uses values already read by InventoryChangeService to avoid re-reading game memory.
    /// </summary>
    private void OnValuesChanged(IReadOnlyDictionary<TrackedDataType, long> changedValues)
    {
        if (!_enabled) return;
        if (changedValues.Count == 0) return;

        try
        {
            var cid = GameStateService.PlayerContentId;
            if (cid == 0) return;

            // Capture character name on main thread (game data access)
            string? characterName = null;
            try
            {
                var rawName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(cid);
                characterName = NameSanitizer.SanitizeWithPlayerFallback(rawName);
            }
            catch { /* Ignore name capture failures */ }

            // Queue all changed values for background database write
            // Also update cache immediately for instant UI access
            foreach (var (dataType, value) in changedValues)
            {
                var variable = dataType.ToString();
                
                // Update cache immediately (main thread) for instant UI access
                var isNewValue = _cacheService.AddPoint(variable, cid, value);
                
                // Cache character name if available
                if (!string.IsNullOrEmpty(characterName))
                {
                    _cacheService.SetCharacterName(cid, characterName);
                }
                
                // Queue DB write (background thread) for persistence
                if (isNewValue)
                {
                    var workItem = new SampleWorkItem(cid, variable, value, characterName);
                    _sampleQueue.Writer.TryWrite(workItem);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[SamplerService] OnValuesChanged error: {ex.Message}");
        }
    }

    /// <summary>
    /// Background worker that processes queued sample writes.
    /// Runs on a dedicated thread to avoid blocking the game.
    /// </summary>
    private async Task ProcessSampleQueueAsync()
    {
        var reader = _sampleQueue.Reader;

        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var workItem))
                {
                    if (_cts.Token.IsCancellationRequested) return;

                    try
                    {
                        var inserted = _dbService.SaveSampleIfChanged(workItem.Variable, workItem.CharacterId, workItem.Value);

                        if (inserted && !string.IsNullOrEmpty(workItem.CharacterName) 
                            && Kaleidoscope.Libs.CharacterLib.ValidateName(workItem.CharacterName))
                        {
                            _dbService.SaveCharacterName(workItem.CharacterId, workItem.CharacterName);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug($"[SamplerService] Background write error: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            LogService.Error($"[SamplerService] Background worker crashed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Populates the in-memory cache from database on startup.
    /// Loads recent data based on cache configuration.
    /// </summary>
    private void PopulateCacheFromDatabase()
    {
        var cacheConfig = _configService.Config.TimeSeriesCacheConfig;
        if (!cacheConfig.PrePopulateOnStartup)
        {
            _log.Debug("[SamplerService] Cache pre-population disabled");
            return;
        }

        try
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-cacheConfig.StartupLoadHours);
            var loadedSeries = 0;
            var loadedPoints = 0L;

            // Load character names first (with game name, display name, and color)
            var characterData = _dbService.GetAllCharacterDataExtended();
            _cacheService.PopulateCharacterNames(characterData);
            _log.Debug($"[SamplerService] Loaded {characterData.Count} character names into cache");

            // Load data for each tracked data type
            foreach (var dataType in _registry.Definitions.Keys)
            {
                var variable = dataType.ToString();

                // Get available characters for this variable
                var characters = _dbService.GetAvailableCharacters(variable);
                if (characters.Count == 0) continue;

                _cacheService.PopulateAvailableCharacters(variable, characters);

                // Load recent points for each character
                foreach (var charId in characters)
                {
                    var points = _dbService.GetPointsSince(variable, charId, cutoffTime);
                    if (points.Count > 0)
                    {
                        _cacheService.PopulateFromDatabase(variable, charId, points);
                        loadedSeries++;
                        loadedPoints += points.Count;
                    }
                }
            }

            _log.Information($"[SamplerService] Cache populated: {loadedSeries} series, {loadedPoints} points from last {cacheConfig.StartupLoadHours}h");
        }
        catch (Exception ex)
        {
            _log.Error($"[SamplerService] Failed to populate cache from database: {ex.Message}");
        }
    }

    #region Data Management Helpers

    /// <summary>
    /// Gets whether the database exists and has data.
    /// </summary>
    public bool HasDb => !string.IsNullOrEmpty(_filenames.DatabasePath) 
                         && File.Exists(_filenames.DatabasePath);

    /// <summary>
    /// Clears all data for a specific data type from the database.
    /// </summary>
    public void ClearAllData(TrackedDataType dataType)
    {
        _dbService.ClearAllData(dataType.ToString());
        _log.Information($"Cleared all {dataType} data");
    }

    /// <summary>
    /// Clears all data from the database (all data types).
    /// </summary>
    public void ClearAllData()
    {
        _dbService.ClearAllTables();
        _log.Information("Cleared all tracking data");
    }

    /// <summary>
    /// Removes data for characters without a name association.
    /// </summary>
    public int CleanUnassociatedCharacters()
    {
        var totalCount = 0;
        foreach (var dataType in _registry.Definitions.Keys)
        {
            totalCount += _dbService.CleanUnassociatedCharacters(dataType.ToString());
        }
        if (totalCount > 0)
            _log.Information($"Cleaned {totalCount} unassociated character series");
        return totalCount;
    }

    /// <summary>
    /// Removes data for characters without a name association for a specific data type.
    /// </summary>
    public int CleanUnassociatedCharacters(TrackedDataType dataType)
    {
        var count = _dbService.CleanUnassociatedCharacters(dataType.ToString());
        if (count > 0)
            _log.Information($"Cleaned {count} unassociated {dataType} character series");
        return count;
    }

    /// <summary>
    /// Exports data to a CSV file and returns the file path.
    /// </summary>
    public string? ExportCsv(TrackedDataType dataType, ulong? characterId = null)
    {
        var dbPath = _filenames.DatabasePath;
        if (string.IsNullOrEmpty(dbPath)) return null;

        try
        {
            var variableName = dataType.ToString();
            var csvContent = _dbService.ExportToCsv(variableName, characterId);
            if (string.IsNullOrEmpty(csvContent)) return null;

            var dir = Path.GetDirectoryName(dbPath) ?? "";
            var suffix = characterId.HasValue && characterId.Value != 0
                ? $"-{characterId.Value}"
                : "-all";
            var fileName = $"{variableName.ToLower()}{suffix}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv";
            var filePath = Path.Combine(dir, fileName);

            File.WriteAllText(filePath, csvContent);
            _log.Information($"Exported {dataType} data to {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to export {dataType} CSV: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Exports data to a CSV file and returns the file path (legacy Gil-only method).
    /// </summary>
    [Obsolete("Use ExportCsv(TrackedDataType, ulong?) instead")]
    public string? ExportCsv(ulong? characterId = null) => ExportCsv(TrackedDataType.Gil, characterId);

    #endregion

    public void Dispose()
    {
        // Unsubscribe from inventory change events
        _inventoryChangeService.OnValuesChanged -= OnValuesChanged;

        // Signal background worker to stop and wait for it to finish
        _cts.Cancel();
        _sampleQueue.Writer.Complete();

        try
        {
            // Wait for background worker to finish processing (with timeout)
            _backgroundWorker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException) { /* Expected if task was canceled */ }
        catch (Exception ex)
        {
            LogService.Debug($"[SamplerService] Background worker shutdown error: {ex.Message}");
        }

        _cts.Dispose();
        _dbService.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Work item representing a sample to be persisted to the database.
/// </summary>
/// <param name="CharacterId">The character's content ID.</param>
/// <param name="Variable">The variable name (e.g., "Gil", "TomestonePoetics").</param>
/// <param name="Value">The sampled value.</param>
/// <param name="CharacterName">The character name to save (captured on main thread).</param>
internal readonly record struct SampleWorkItem(
    ulong CharacterId,
    string Variable,
    long Value,
    string? CharacterName
);
