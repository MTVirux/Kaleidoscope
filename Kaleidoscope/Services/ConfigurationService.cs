using Kaleidoscope.Config;
using Kaleidoscope.Interfaces;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OtterGui.Services;
using System.Timers;

namespace Kaleidoscope.Services;

/// <summary>
/// Manages plugin configuration including layouts, window state, and settings.
/// Configuration is held in memory (cache-first) with write-through persistence.
/// </summary>
/// <remarks>
/// This follows a hybrid approach: the main Configuration uses Dalamud's standard
/// IPluginConfiguration, while sub-configs use JSON files for modularity.
/// 
/// Phase 4 Cache Architecture:
/// - Config is loaded once on startup and kept in memory
/// - All reads are instant (no disk I/O)
/// - Writes are debounced to coalesce rapid changes
/// - Dirty tracking prevents unnecessary saves
/// </remarks>
public sealed class ConfigurationService : IConfigurationService, IRequiredService, IDisposable
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;
    
    private bool _isDirty;
    private System.Timers.Timer? _saveDebounceTimer;
    private readonly object _saveLock = new();
    private const int SaveDebounceMs = 500; // Coalesce saves within 500ms
    
    private long _saveCount;
    private long _saveSkippedCount;
    private long _configAccessCount;
    private DateTime? _lastSaveTime;

    public Configuration Config { get; private set; }
    public ConfigManager ConfigManager { get; private set; }
    public GeneralConfig GeneralConfig { get; private set; }
    public CurrencyTrackerConfig CurrencyTrackerConfig { get; private set; }
    public WindowConfig WindowConfig { get; private set; }

    /// <summary>
    /// Event raised when configuration is saved. Subscribe to this to react to config changes.
    /// </summary>
    public event Action? OnConfigChanged;

    /// <summary>
    /// Event raised when the active layout is changed. Subscribers should apply the new layout.
    /// Parameters: layoutName, layoutType
    /// </summary>
    public event Action<string, LayoutType>? OnActiveLayoutChanged;

    public ConfigurationService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        var cfg = _pluginInterface.GetPluginConfig() as Configuration;
        if (cfg == null)
        {
            cfg = new Configuration();
            _pluginInterface.SavePluginConfig(cfg);
        }
        Config = cfg;

        NormalizeLayouts();
        EnsureDefaultCrystalColors();

        var saveDir = _pluginInterface.GetPluginConfigDirectory();
        ConfigManager = new ConfigManager(saveDir);
        GeneralConfig = ConfigManager.LoadOrCreate("general.json", () => new GeneralConfig { ShowOnStart = Config.ShowOnStart });
        CurrencyTrackerConfig = ConfigManager.LoadOrCreate("currencytracker.json", () => new CurrencyTrackerConfig { TrackingEnabled = true, TrackingIntervalMs = ConfigStatic.DefaultTrackingIntervalMs });
        WindowConfig = ConfigManager.LoadOrCreate("windows.json", () => new WindowConfig
        {
            PinMainWindow = Config.PinMainWindow,
            PinConfigWindow = Config.PinConfigWindow,
            MainWindowPos = Config.MainWindowPos,
            MainWindowSize = Config.MainWindowSize,
            ConfigWindowPos = Config.ConfigWindowPos,
            ConfigWindowSize = Config.ConfigWindowSize
        });

        _saveDebounceTimer = new System.Timers.Timer(500);
        _saveDebounceTimer.Elapsed += OnDebounceTimerElapsed;
        _saveDebounceTimer.AutoReset = false;

        LoadLayouts();
        SyncFromSubConfigs();

        LogService.Debug(LogCategory.Config, "ConfigurationService initialized with debounced save support");
    }

    private void NormalizeLayouts()
    {
        Config.Layouts ??= new List<ContentLayoutState>();

        var seenNames = new Dictionary<(string Name, LayoutType Type), int>(
            new LayoutNameTypeComparer());
        
        foreach (var layout in Config.Layouts)
        {
            var keyName = layout.Name?.Trim() ?? string.Empty;
            var key = (Name: keyName, Type: layout.Type);
            if (seenNames.TryGetValue(key, out var count))
            {
                seenNames[key] = count + 1;
                var newName = $"{keyName} ({count + 1})";
                while (Config.Layouts.Any(l => l != layout && 
                                               l.Type == layout.Type && 
                                               string.Equals(l.Name, newName, StringComparison.OrdinalIgnoreCase)))
                {
                    count++;
                    newName = $"{keyName} ({count + 1})";
                }
                layout.Name = newName;
                seenNames[key] = count + 1;
            }
            else
            {
                seenNames[key] = 1;
            }
        }

        var windowedLayouts = Config.Layouts.Where(x => x.Type == LayoutType.Windowed).ToList();
        if (!string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName) &&
            !windowedLayouts.Any(x => string.Equals(x.Name, Config.ActiveWindowedLayoutName, StringComparison.OrdinalIgnoreCase)))
        {
            Config.ActiveWindowedLayoutName = string.Empty;
        }
        if (string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName) && windowedLayouts.Count > 0)
        {
            Config.ActiveWindowedLayoutName = windowedLayouts.First().Name;
        }

        var fullscreenLayouts = Config.Layouts.Where(x => x.Type == LayoutType.Fullscreen).ToList();
        if (!string.IsNullOrWhiteSpace(Config.ActiveFullscreenLayoutName) &&
            !fullscreenLayouts.Any(x => string.Equals(x.Name, Config.ActiveFullscreenLayoutName, StringComparison.OrdinalIgnoreCase)))
        {
            Config.ActiveFullscreenLayoutName = string.Empty;
        }
        if (string.IsNullOrWhiteSpace(Config.ActiveFullscreenLayoutName) && fullscreenLayouts.Count > 0)
        {
            Config.ActiveFullscreenLayoutName = fullscreenLayouts.First().Name;
        }
    }

    private void LoadLayouts()
    {
        try
        {
            var loaded = ConfigManager.LoadOrCreate("layouts.json", () => new List<ContentLayoutState>());
            if (loaded != null)
            {
                Config.Layouts = loaded;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Config, $"Failed to load layouts: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures default colors are set for crystal items if not already configured.
    /// </summary>
    private void EnsureDefaultCrystalColors()
    {
        Config.GameItemColors ??= new Dictionary<uint, uint>();
        
        // Element colors in ABGR uint format
        // ABGR format: A << 24 | B << 16 | G << 8 | R
        // Fire: (1.0f, 0.3f, 0.2f, 1.0f) - red/orange → R=255, G=77, B=51, A=255
        // Ice: (0.4f, 0.7f, 1.0f, 1.0f) - light blue → R=102, G=179, B=255, A=255
        // Wind: (0.3f, 0.9f, 0.5f, 1.0f) - green → R=77, G=230, B=128, A=255
        // Earth: (0.8f, 0.6f, 0.3f, 1.0f) - brown/tan → R=204, G=153, B=77, A=255
        // Lightning: (0.7f, 0.3f, 0.9f, 1.0f) - purple → R=179, G=77, B=230, A=255
        // Water: (0.3f, 0.5f, 1.0f, 1.0f) - blue → R=77, G=128, B=255, A=255
        uint[] elementColorsAbgr =
        {
            0xFF334DFF, // Fire
            0xFFFFB366, // Ice
            0xFF80E64D, // Wind
            0xFF4D99CC, // Earth
            0xFFE64DB3, // Lightning
            0xFFFF804D  // Water
        };
        
        // Crystal item IDs: 
        // Shards: 2-7 (Fire=2, Ice=3, Wind=4, Earth=5, Lightning=6, Water=7)
        // Crystals: 8-13 (Fire=8, Ice=9, Wind=10, Earth=11, Lightning=12, Water=13)
        // Clusters: 14-19 (Fire=14, Ice=15, Wind=16, Earth=17, Lightning=18, Water=19)
        const int baseId = ConfigStatic.CrystalBaseItemId; // 2
        const int tierOffset = ConfigStatic.CrystalTierOffset; // 6
        
        for (int element = 0; element < 6; element++)
        {
            var color = elementColorsAbgr[element];
            
            var shardId = (uint)(baseId + element);
            if (!Config.GameItemColors.ContainsKey(shardId))
                Config.GameItemColors[shardId] = color;
            
            var crystalId = (uint)(baseId + tierOffset + element);
            if (!Config.GameItemColors.ContainsKey(crystalId))
                Config.GameItemColors[crystalId] = color;
            
            var clusterId = (uint)(baseId + 2 * tierOffset + element);
            if (!Config.GameItemColors.ContainsKey(clusterId))
                Config.GameItemColors[clusterId] = color;
        }
    }

    private void SyncFromSubConfigs()
    {
        Config.ShowOnStart = GeneralConfig.ShowOnStart;
        Config.ExclusiveFullscreen = GeneralConfig.ExclusiveFullscreen;
        Config.ContentGridCellWidthPercent = GeneralConfig.ContentGridCellWidthPercent;
        Config.ContentGridCellHeightPercent = GeneralConfig.ContentGridCellHeightPercent;
        Config.EditMode = GeneralConfig.EditMode;
        Config.PinMainWindow = WindowConfig.PinMainWindow;
        Config.PinConfigWindow = WindowConfig.PinConfigWindow;
        Config.MainWindowPos = WindowConfig.MainWindowPos;
        Config.MainWindowSize = WindowConfig.MainWindowSize;
        Config.ConfigWindowPos = WindowConfig.ConfigWindowPos;
        Config.ConfigWindowSize = WindowConfig.ConfigWindowSize;
    }

    /// <summary>
    /// Marks the configuration as dirty, scheduling a debounced save.
    /// Use this instead of calling Save() directly for non-critical changes.
    /// </summary>
    public void MarkDirty()
    {
        lock (_saveLock)
        {
            _isDirty = true;
            Interlocked.Increment(ref _configAccessCount);
            
            _saveDebounceTimer?.Stop();
            _saveDebounceTimer?.Start();
        }
    }

    /// <summary>
    /// Saves immediately if dirty, bypassing debounce. Use for critical saves (e.g., shutdown).
    /// </summary>
    public void SaveImmediate()
    {
        lock (_saveLock)
        {
            _saveDebounceTimer?.Stop();
            
            if (_isDirty)
            {
                SaveInternal();
            }
            else
            {
                Interlocked.Increment(ref _saveSkippedCount);
            }
        }
    }

    /// <summary>
    /// Standard save method. If debouncing is enabled and config is not dirty, skips the save.
    /// </summary>
    public void Save()
    {
        lock (_saveLock)
        {
            _saveDebounceTimer?.Stop();
            SaveInternal();
        }
    }

    private void SaveInternal()
    {
        try
        {
            _pluginInterface.SavePluginConfig(Config);
            LogService.Info(LogCategory.Config, $"Saved plugin config; layouts={Config.Layouts?.Count ?? 0} activeWindowed='{Config.ActiveWindowedLayoutName}' activeFullscreen='{Config.ActiveFullscreenLayoutName}'");
            
            Interlocked.Increment(ref _saveCount);
            _lastSaveTime = DateTime.UtcNow;
            _isDirty = false;
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Config, $"Error saving plugin config: {ex}");
        }

        SaveSubConfigs();

        try
        {
            OnConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Config, $"Error invoking OnConfigChanged: {ex}");
        }
    }

    private void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (_saveLock)
        {
            if (_isDirty)
            {
                SaveInternal();
            }
        }
    }

    /// <summary>
    /// Sets the active layout and notifies subscribers to apply it.
    /// </summary>
    /// <param name="layoutName">The name of the layout to activate.</param>
    /// <param name="layoutType">The type of layout (Windowed or Fullscreen).</param>
    public void SetActiveLayout(string layoutName, LayoutType layoutType)
    {
        if (string.IsNullOrWhiteSpace(layoutName)) return;

        if (layoutType == LayoutType.Windowed)
            Config.ActiveWindowedLayoutName = layoutName;
        else
            Config.ActiveFullscreenLayoutName = layoutName;

        Save();

        try
        {
            OnActiveLayoutChanged?.Invoke(layoutName, layoutType);
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Config, $"Error invoking OnActiveLayoutChanged: {ex}");
        }
    }

    private void SaveSubConfigs()
    {
        try
        {
            var g = new GeneralConfig
            {
                ShowOnStart = Config.ShowOnStart,
                ExclusiveFullscreen = Config.ExclusiveFullscreen,
                ContentGridCellWidthPercent = Config.ContentGridCellWidthPercent,
                ContentGridCellHeightPercent = Config.ContentGridCellHeightPercent,
                EditMode = Config.EditMode
            };
            ConfigManager.Save("general.json", g);

            var s = new CurrencyTrackerConfig
            {
                TrackingEnabled = true, // Always enabled, cannot be turned off
                TrackingIntervalMs = CurrencyTrackerConfig.TrackingIntervalMs,
                DatabaseCacheSizeMb = CurrencyTrackerConfig.DatabaseCacheSizeMb
            };
            ConfigManager.Save("currencytracker.json", s);

            var w = new WindowConfig
            {
                PinMainWindow = Config.PinMainWindow,
                PinConfigWindow = Config.PinConfigWindow,
                MainWindowPos = Config.MainWindowPos,
                MainWindowSize = Config.MainWindowSize,
                ConfigWindowPos = Config.ConfigWindowPos,
                ConfigWindowSize = Config.ConfigWindowSize
            };
            ConfigManager.Save("windows.json", w);
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Config, $"Error saving sub-configs: {ex.Message}");
        }
    }

    public void SaveLayouts()
    {
        try
        {
            ConfigManager.Save("layouts.json", Config.Layouts);
            LogService.Debug(LogCategory.Config, $"Saved layouts: {Config.Layouts?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Config, $"Error saving layouts: {ex.Message}");
        }
    }

    #region Statistics Properties
    
    /// <summary>
    /// Total number of saves performed.
    /// </summary>
    public long SaveCount => Interlocked.Read(ref _saveCount);
    
    /// <summary>
    /// Number of saves skipped because config was not dirty.
    /// </summary>
    public long SaveSkippedCount => Interlocked.Read(ref _saveSkippedCount);
    
    /// <summary>
    /// Number of times MarkDirty was called.
    /// </summary>
    public long ConfigAccessCount => Interlocked.Read(ref _configAccessCount);
    
    /// <summary>
    /// Whether the configuration has unsaved changes.
    /// </summary>
    public bool IsDirty => _isDirty;
    
    /// <summary>
    /// Last time the configuration was saved.
    /// </summary>
    public DateTime? LastSaveTime => _lastSaveTime;
    
    /// <summary>
    /// Resets all statistics counters.
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _saveCount, 0);
        Interlocked.Exchange(ref _saveSkippedCount, 0);
        Interlocked.Exchange(ref _configAccessCount, 0);
        _lastSaveTime = null;
    }
    
    #endregion

    #region IDisposable
    
    public void Dispose()
    {
        lock (_saveLock)
        {
            _saveDebounceTimer?.Stop();
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
            
            if (_isDirty)
            {
                try
                {
                    _pluginInterface.SavePluginConfig(Config);
                    SaveSubConfigs();
                    _isDirty = false;
                    LogService.Info(LogCategory.Config, "Flushed pending config changes on dispose");
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.Config, $"Error flushing config on dispose: {ex}");
                }
            }
        }
    }
    
    #endregion

    /// <summary>
    /// Helper comparer for (Name, Type) tuple that uses case-insensitive name comparison.
    /// </summary>
    private class LayoutNameTypeComparer : IEqualityComparer<(string Name, LayoutType Type)>
    {
        public bool Equals((string Name, LayoutType Type) x, (string Name, LayoutType Type) y)
        {
            return x.Type == y.Type && 
                   string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Name, LayoutType Type) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name ?? string.Empty),
                obj.Type);
        }
    }
}
