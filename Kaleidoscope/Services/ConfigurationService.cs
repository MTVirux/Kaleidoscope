namespace Kaleidoscope.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Kaleidoscope;
    using Kaleidoscope.Config;
    using Kaleidoscope.Interfaces;
    using Dalamud.Plugin;
    using Dalamud.Plugin.Services;

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
            SamplerConfig = ConfigManager.LoadOrCreate("sampler.json", () => new SamplerConfig { SamplerEnabled = true, SamplerIntervalMs = 1000 });
            WindowConfig = ConfigManager.LoadOrCreate("windows.json", () => new WindowConfig {
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

            if (!string.IsNullOrWhiteSpace(Config.ActiveLayoutName) && 
                !Config.Layouts.Any(x => string.Equals(x.Name, Config.ActiveLayoutName, StringComparison.OrdinalIgnoreCase)))
            {
                Config.ActiveLayoutName = null;
            }
            else if (string.IsNullOrWhiteSpace(Config.ActiveLayoutName) && Config.Layouts.Count > 0)
            {
                Config.ActiveLayoutName = Config.Layouts.First().Name;
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
                _log.Information($"Saved plugin config; layouts={Config.Layouts?.Count ?? 0} active='{Config.ActiveLayoutName}'");
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
                var g = new GeneralConfig {
                    ShowOnStart = Config.ShowOnStart,
                    ExclusiveFullscreen = Config.ExclusiveFullscreen,
                    ContentGridCellWidthPercent = Config.ContentGridCellWidthPercent,
                    ContentGridCellHeightPercent = Config.ContentGridCellHeightPercent,
                    EditMode = Config.EditMode
                };
                ConfigManager.Save("general.json", g);

                var s = new SamplerConfig { 
                    SamplerEnabled = SamplerConfig.SamplerEnabled, 
                    SamplerIntervalMs = SamplerConfig.SamplerIntervalMs 
                };
                ConfigManager.Save("sampler.json", s);

                var w = new WindowConfig {
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
}
