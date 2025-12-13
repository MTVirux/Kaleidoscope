namespace Kaleidoscope.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Kaleidoscope;
    using Kaleidoscope.Config;
    using Kaleidoscope.Interfaces;
    using Dalamud.Plugin;

    public class ConfigurationService : IConfigurationService
    {
        private readonly IDalamudPluginInterface pluginInterface;

        public Configuration Config { get; private set; }

        public ConfigManager ConfigManager { get; private set; }
        public GeneralConfig GeneralConfig { get; private set; }
        public SamplerConfig SamplerConfig { get; private set; }
        public WindowConfig WindowConfig { get; private set; }

        public ConfigurationService(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));

            var cfg = this.pluginInterface.GetPluginConfig() as Kaleidoscope.Configuration;
            if (cfg == null)
            {
                cfg = new Kaleidoscope.Configuration();
                this.pluginInterface.SavePluginConfig(cfg);
            }
            this.Config = cfg;

            // Normalize layouts
            try
            {
                if (this.Config.Layouts != null && this.Config.Layouts.Count > 1)
                {
                    var deduped = this.Config.Layouts
                        .GroupBy(l => (l.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .ToList();
                    this.Config.Layouts = deduped;
                }

                if (this.Config.Layouts == null)
                {
                    this.Config.Layouts = new List<ContentLayoutState>();
                }

                if (!string.IsNullOrWhiteSpace(this.Config.ActiveLayoutName) && !this.Config.Layouts.Any(x => string.Equals(x.Name, this.Config.ActiveLayoutName, StringComparison.OrdinalIgnoreCase)))
                {
                    this.Config.ActiveLayoutName = null;
                }
                else if (string.IsNullOrWhiteSpace(this.Config.ActiveLayoutName) && this.Config.Layouts.Count > 0)
                {
                    this.Config.ActiveLayoutName = this.Config.Layouts.First().Name;
                }
            }
            catch { }

            var saveDir = this.pluginInterface.GetPluginConfigDirectory();
            this.ConfigManager = new Kaleidoscope.Config.ConfigManager(saveDir);
            this.GeneralConfig = this.ConfigManager.LoadOrCreate("general.json", () => new Kaleidoscope.Config.GeneralConfig { ShowOnStart = this.Config.ShowOnStart });
            this.SamplerConfig = this.ConfigManager.LoadOrCreate("sampler.json", () => new Kaleidoscope.Config.SamplerConfig { SamplerEnabled = true, SamplerIntervalMs = 1000 });
            this.WindowConfig = this.ConfigManager.LoadOrCreate("windows.json", () => new Kaleidoscope.Config.WindowConfig {
                PinMainWindow = this.Config.PinMainWindow,
                PinConfigWindow = this.Config.PinConfigWindow,
                MainWindowPos = this.Config.MainWindowPos,
                MainWindowSize = this.Config.MainWindowSize,
                ConfigWindowPos = this.Config.ConfigWindowPos,
                ConfigWindowSize = this.Config.ConfigWindowSize
            });

            try
            {
                var loaded = this.ConfigManager.LoadOrCreate("layouts.json", () => new System.Collections.Generic.List<ContentLayoutState>());
                if (loaded != null)
                {
                    this.Config.Layouts = loaded;
                }
            }
            catch { }

            try { this.Config.ShowOnStart = this.GeneralConfig.ShowOnStart; } catch { }
            try { this.Config.ExclusiveFullscreen = this.GeneralConfig.ExclusiveFullscreen; } catch { }
            try { this.Config.ContentGridCellWidthPercent = this.GeneralConfig.ContentGridCellWidthPercent; } catch { }
            try { this.Config.ContentGridCellHeightPercent = this.GeneralConfig.ContentGridCellHeightPercent; } catch { }
            try { this.Config.EditMode = this.GeneralConfig.EditMode; } catch { }
            try { this.Config.PinMainWindow = this.WindowConfig.PinMainWindow; } catch { }
            try { this.Config.PinConfigWindow = this.WindowConfig.PinConfigWindow; } catch { }
            try { this.Config.MainWindowPos = this.WindowConfig.MainWindowPos; } catch { }
            try { this.Config.MainWindowSize = this.WindowConfig.MainWindowSize; } catch { }
            try { this.Config.ConfigWindowPos = this.WindowConfig.ConfigWindowPos; } catch { }
            try { this.Config.ConfigWindowSize = this.WindowConfig.ConfigWindowSize; } catch { }
        }

        public void Save()
        {
            try
            {
                this.pluginInterface.SavePluginConfig(this.Config);
                try { ECommons.Logging.PluginLog.Information($"Saved plugin config; layouts={this.Config.Layouts?.Count ?? 0} active='{this.Config.ActiveLayoutName}'"); } catch { }
            }
            catch (Exception ex)
            {
                try { ECommons.Logging.PluginLog.Error($"Error saving plugin config: {ex}"); } catch { }
            }

            try
            {
                var g = new Kaleidoscope.Config.GeneralConfig {
                    ShowOnStart = this.Config.ShowOnStart,
                    ExclusiveFullscreen = this.Config.ExclusiveFullscreen,
                    ContentGridCellWidthPercent = this.Config.ContentGridCellWidthPercent,
                    ContentGridCellHeightPercent = this.Config.ContentGridCellHeightPercent,
                    EditMode = this.Config.EditMode
                };
                this.ConfigManager.Save("general.json", g);
                var s = new Kaleidoscope.Config.SamplerConfig { SamplerEnabled = true, SamplerIntervalMs = 1000 };
                this.ConfigManager.Save("sampler.json", s);
                var w = new Kaleidoscope.Config.WindowConfig {
                    PinMainWindow = this.Config.PinMainWindow,
                    PinConfigWindow = this.Config.PinConfigWindow,
                    MainWindowPos = this.Config.MainWindowPos,
                    MainWindowSize = this.Config.MainWindowSize,
                    ConfigWindowPos = this.Config.ConfigWindowPos,
                    ConfigWindowSize = this.Config.ConfigWindowSize
                };
                this.ConfigManager.Save("windows.json", w);
            }
            catch { }
        }

        public void SaveLayouts()
        {
            try { this.ConfigManager.Save("layouts.json", this.Config.Layouts); } catch { }
        }
    }
}
