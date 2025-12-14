using Kaleidoscope.Config;
using Kaleidoscope.Interfaces;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Manages plugin configuration including layouts, window state, and settings.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;

    public Configuration Config { get; private set; }
    public ConfigManager ConfigManager { get; private set; }
    public GeneralConfig GeneralConfig { get; private set; }
    public SamplerConfig SamplerConfig { get; private set; }
    public WindowConfig WindowConfig { get; private set; }

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

        var saveDir = _pluginInterface.GetPluginConfigDirectory();
        ConfigManager = new ConfigManager(saveDir);
        GeneralConfig = ConfigManager.LoadOrCreate("general.json", () => new GeneralConfig { ShowOnStart = Config.ShowOnStart });
        SamplerConfig = ConfigManager.LoadOrCreate("sampler.json", () => new SamplerConfig { SamplerEnabled = true, SamplerIntervalMs = ConfigStatic.DefaultSamplerIntervalMs });
        WindowConfig = ConfigManager.LoadOrCreate("windows.json", () => new WindowConfig
        {
            PinMainWindow = Config.PinMainWindow,
            PinConfigWindow = Config.PinConfigWindow,
            MainWindowPos = Config.MainWindowPos,
            MainWindowSize = Config.MainWindowSize,
            ConfigWindowPos = Config.ConfigWindowPos,
            ConfigWindowSize = Config.ConfigWindowSize
        });

        LoadLayouts();
        SyncFromSubConfigs();

        _log.Debug("ConfigurationService initialized");
    }

    private void NormalizeLayouts()
    {
        if (Config.Layouts != null && Config.Layouts.Count > 1)
        {
            var deduped = Config.Layouts
                .GroupBy(l => (l.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            Config.Layouts = deduped;
        }

        Config.Layouts ??= new List<ContentLayoutState>();

        // Migrate from legacy ActiveLayoutName if needed
#pragma warning disable CS0618 // Suppress obsolete warning for migration
        if (!string.IsNullOrWhiteSpace(Config.ActiveLayoutName))
        {
            var legacyLayout = Config.Layouts.FirstOrDefault(x => string.Equals(x.Name, Config.ActiveLayoutName, StringComparison.OrdinalIgnoreCase));
            if (legacyLayout != null)
            {
                if (legacyLayout.Type == LayoutType.Windowed && string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName))
                    Config.ActiveWindowedLayoutName = legacyLayout.Name;
                else if (legacyLayout.Type == LayoutType.Fullscreen && string.IsNullOrWhiteSpace(Config.ActiveFullscreenLayoutName))
                    Config.ActiveFullscreenLayoutName = legacyLayout.Name;
            }
            Config.ActiveLayoutName = string.Empty;
        }
#pragma warning restore CS0618

        // Validate windowed active layout
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

        // Validate fullscreen active layout
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
            _log.Error($"Failed to load layouts: {ex.Message}");
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

    public void Save()
    {
        try
        {
            _pluginInterface.SavePluginConfig(Config);
            _log.Information($"Saved plugin config; layouts={Config.Layouts?.Count ?? 0} activeWindowed='{Config.ActiveWindowedLayoutName}' activeFullscreen='{Config.ActiveFullscreenLayoutName}'");
        }
        catch (Exception ex)
        {
            _log.Error($"Error saving plugin config: {ex}");
        }

        SaveSubConfigs();
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

            var s = new SamplerConfig
            {
                SamplerEnabled = SamplerConfig.SamplerEnabled,
                SamplerIntervalMs = SamplerConfig.SamplerIntervalMs
            };
            ConfigManager.Save("sampler.json", s);

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
            _log.Error($"Error saving sub-configs: {ex.Message}");
        }
    }

    public void SaveLayouts()
    {
        try
        {
            ConfigManager.Save("layouts.json", Config.Layouts);
            _log.Debug($"Saved layouts: {Config.Layouts?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            _log.Error($"Error saving layouts: {ex.Message}");
        }
    }
}
