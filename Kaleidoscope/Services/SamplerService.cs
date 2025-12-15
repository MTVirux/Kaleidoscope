using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Background service that periodically samples game data (e.g., gil) and persists it via KaleidoscopeDbService.
/// </summary>
/// <remarks>
/// This follows the InventoryTools pattern for background services that need to
/// periodically poll game state and persist data.
/// </remarks>
public sealed class SamplerService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly FilenameService _filenames;
    private readonly ConfigurationService _configService;
    private readonly KaleidoscopeDbService _dbService;

    private Timer? _timer;
    private volatile bool _enabled = true;
    private int _intervalSeconds = 1;

    /// <summary>
    /// Gets or sets whether sampling is enabled.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Gets or sets the sampling interval in milliseconds.
    /// </summary>
    public int IntervalMs
    {
        get => _intervalSeconds * 1000;
        set
        {
            if (value <= 0) value = ConfigStatic.DefaultSamplerIntervalMs;
            var sec = (value + 999) / 1000;
            _intervalSeconds = sec;
            _timer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_intervalSeconds));
        }
    }

    /// <summary>
    /// Gets the underlying database service for direct data access.
    /// </summary>
    public KaleidoscopeDbService DbService => _dbService;

    public SamplerService(IPluginLog log, FilenameService filenames, ConfigurationService configService)
    {
        _log = log;
        _filenames = filenames;
        _configService = configService;

        // Create the database service
        _dbService = new KaleidoscopeDbService(filenames.DatabasePath);

        // Perform one-time migration of stored names
        _dbService.MigrateStoredNames();

        // Load initial values from config
        _enabled = configService.SamplerConfig.SamplerEnabled;
        _intervalSeconds = Math.Max(1, configService.SamplerConfig.SamplerIntervalMs / 1000);

        StartTimer();
    }

    private void StartTimer()
    {
        _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(_intervalSeconds));
    }

    private void OnTimerTick(object? state)
    {
        if (!_enabled) return;

        try
        {
            var cid = GameStateService.PlayerContentId;
            if (cid == 0) return;

            uint gil = 0;
            unsafe
            {
                var im = GameStateService.InventoryManagerInstance();
                if (im != null)
                {
                    // Use GetInventoryItemCount(1) as primary (matches AutoRetainer approach)
                    try { gil = (uint)im->GetInventoryItemCount(1); }
                    catch { gil = 0; }
                    
                    // Fallback to GetGil() if primary returns 0
                    if (gil == 0)
                    {
                        try { gil = im->GetGil(); }
                        catch { gil = 0; }
                    }
                }
            }

            PersistSample(cid, gil);
        }
        catch (Exception ex)
        {
            _log.Error($"Sampler tick error: {ex.Message}");
        }
    }

    private void PersistSample(ulong characterId, uint gil)
    {
        // Use the database service to save the sample
        var inserted = _dbService.SaveSampleIfChanged("Gil", characterId, (long)gil);

        if (inserted)
        {
            // Also try to save the character name if available
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
    /// Clears all data from the database.
    /// </summary>
    public void ClearAllData()
    {
        _dbService.ClearAllData("Gil");
        _log.Information("Cleared all GilTracker data");
    }

    /// <summary>
    /// Removes data for characters without a name association.
    /// </summary>
    public int CleanUnassociatedCharacters()
    {
        var count = _dbService.CleanUnassociatedCharacters("Gil");
        if (count > 0)
            _log.Information($"Cleaned {count} unassociated character series");
        return count;
    }

    /// <summary>
    /// Exports data to a CSV file and returns the file path.
    /// </summary>
    public string? ExportCsv(ulong? characterId = null)
    {
        var dbPath = _filenames.DatabasePath;
        if (string.IsNullOrEmpty(dbPath)) return null;

        try
        {
            var csvContent = _dbService.ExportToCsv("Gil", characterId);
            if (string.IsNullOrEmpty(csvContent)) return null;

            var dir = Path.GetDirectoryName(dbPath) ?? "";
            var suffix = characterId.HasValue && characterId.Value != 0
                ? $"-{characterId.Value}"
                : "-all";
            var fileName = $"giltracker{suffix}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv";
            var filePath = Path.Combine(dir, fileName);

            File.WriteAllText(filePath, csvContent);
            _log.Information($"Exported data to {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to export CSV: {ex.Message}");
            return null;
        }
    }

    #endregion

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _dbService.Dispose();
        GC.SuppressFinalize(this);
    }
}
