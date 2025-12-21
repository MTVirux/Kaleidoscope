using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Background service that samples game data (currencies, inventories) and persists it via KaleidoscopeDbService.
/// Uses event-driven hooks for real-time updates with a fallback periodic sync.
/// </summary>
/// <remarks>
/// Primary: Reacts to inventory/currency change events from InventoryChangeService.
/// Fallback: Periodic timer sync to catch any missed updates (runs less frequently).
/// </remarks>
public sealed class SamplerService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly FilenameService _filenames;
    private readonly ConfigurationService _configService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly TrackedDataRegistry _registry;
    private readonly InventoryChangeService _inventoryChangeService;

    // Fallback timer for periodic sync (runs less frequently since we have hooks)
    private Timer? _fallbackTimer;
    private const int FallbackIntervalSeconds = 1; // Reduced frequency since hooks handle most updates

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
    /// Gets or sets the fallback sampling interval in milliseconds.
    /// This is only used for periodic sync, not the primary event-driven updates.
    /// </summary>
    public int IntervalMs
    {
        get => FallbackIntervalSeconds * 1000;
        set
        {
            // Interval is now fixed for fallback; this property kept for config compatibility
        }
    }

    /// <summary>
    /// Gets the underlying database service for direct data access.
    /// </summary>
    public KaleidoscopeDbService DbService => _dbService;

    /// <summary>
    /// Gets the tracked data registry.
    /// </summary>
    public TrackedDataRegistry Registry => _registry;

    private readonly AutoRetainerIpcService _arIpc;

    public SamplerService(
        IPluginLog log,
        FilenameService filenames,
        ConfigurationService configService,
        AutoRetainerIpcService arIpc,
        TrackedDataRegistry registry,
        InventoryChangeService inventoryChangeService)
    {
        _log = log;
        _filenames = filenames;
        _configService = configService;
        _arIpc = arIpc;
        _registry = registry;
        _inventoryChangeService = inventoryChangeService;

        // Create the database service
        _dbService = new KaleidoscopeDbService(filenames.DatabasePath);

        // Perform one-time migration of stored names
        _dbService.MigrateStoredNames();

        // Load initial values from config
        _enabled = configService.SamplerConfig.SamplerEnabled;

        // Auto-import from AutoRetainer on startup
        ImportFromAutoRetainer();

        // Subscribe to inventory change events (primary update mechanism)
        _inventoryChangeService.OnInventoryChanged += OnInventoryChanged;

        // Start fallback timer for periodic sync
        StartFallbackTimer();

        _log.Information("[SamplerService] Initialized with event-driven hooks + fallback timer");
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

    private void StartFallbackTimer()
    {
        _fallbackTimer = new Timer(OnFallbackTimerTick, null, TimeSpan.FromSeconds(FallbackIntervalSeconds), TimeSpan.FromSeconds(FallbackIntervalSeconds));
    }

    /// <summary>
    /// Called when inventory/currency changes are detected via hooks.
    /// This is the primary update mechanism for real-time tracking.
    /// </summary>
    private void OnInventoryChanged()
    {
        if (!_enabled) return;

        try
        {
            SampleAllEnabledTypes();
        }
        catch (Exception ex)
        {
            _log.Debug($"[SamplerService] OnInventoryChanged error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fallback timer tick - runs less frequently to catch any missed updates.
    /// </summary>
    private void OnFallbackTimerTick(object? state)
    {
        if (!_enabled) return;

        try
        {
            SampleAllEnabledTypes();
        }
        catch (Exception ex)
        {
            _log.Error($"Sampler fallback tick error: {ex.Message}");
        }
    }

    /// <summary>
    /// Samples all enabled data types and persists any changes.
    /// </summary>
    private void SampleAllEnabledTypes()
    {
        var cid = GameStateService.PlayerContentId;
        if (cid == 0) return;

        // Sample all enabled data types
        var enabledTypes = _configService.Config.EnabledTrackedDataTypes;
        if (enabledTypes == null || enabledTypes.Count == 0)
        {
            // Fallback to just Gil if nothing configured
            enabledTypes = new HashSet<TrackedDataType> { TrackedDataType.Gil };
        }

        var anyInserted = false;
        foreach (var dataType in enabledTypes)
        {
            try
            {
                var value = _registry.GetCurrentValue(dataType);
                if (value.HasValue)
                {
                    var variableName = dataType.ToString();
                    var inserted = _dbService.SaveSampleIfChanged(variableName, cid, value.Value);
                    if (inserted)
                    {
                        anyInserted = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"Failed to sample {dataType}: {ex.Message}");
            }
        }

        if (anyInserted)
        {
            // Save character name on any data insert
            TrySaveCharacterName(cid);
        }
    }

    private void PersistSample(ulong characterId, TrackedDataType dataType, long value)
    {
        var variableName = dataType.ToString();
        var inserted = _dbService.SaveSampleIfChanged(variableName, characterId, value);

        if (inserted)
        {
            TrySaveCharacterName(characterId);
        }
    }

    private void TrySaveCharacterName(ulong characterId)
    {
        try
        {
            var name = Kaleidoscope.Libs.CharacterLib.GetCharacterName(characterId);
            if (string.IsNullOrEmpty(name)) return;

            // Sanitize the name (remove "You (Name)" wrappers, etc.)
            var sanitized = SanitizeName(name);

            // Try to get local player name if it's just "You"
            if (string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
            {
                var localName = GameStateService.LocalPlayerName;
                if (!string.IsNullOrEmpty(localName))
                    sanitized = localName;
            }

            // Validate and save
            if (!string.IsNullOrEmpty(sanitized) && Kaleidoscope.Libs.CharacterLib.ValidateName(sanitized))
            {
                _dbService.SaveCharacterName(characterId, sanitized);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[SamplerService] Character name save failed: {ex.Message}");
        }
    }

    private static string? SanitizeName(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        var s = raw.Trim();

        // Look for patterns like "You (Name)" and extract the inner name
        var idxOpen = s.IndexOf('(');
        var idxClose = s.LastIndexOf(')');
        if (idxOpen >= 0 && idxClose > idxOpen)
        {
            var inner = s.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
            if (!string.IsNullOrEmpty(inner)) return inner;
        }

        return s;
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
        _inventoryChangeService.OnInventoryChanged -= OnInventoryChanged;

        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
        _dbService.Dispose();
        GC.SuppressFinalize(this);
    }
}
