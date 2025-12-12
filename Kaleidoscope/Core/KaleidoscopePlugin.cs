namespace Kaleidoscope
{
    using System;
    using Dalamud.Interface.Windowing;
    using Kaleidoscope.Gui.TopBar;
    using Microsoft.Data.Sqlite;
    using System.IO;
    using Dalamud.Plugin;

    public sealed class KaleidoscopePlugin : IDalamudPlugin, IDisposable
    {
        public string Name => "Crystal Terror";

        public Kaleidoscope.Configuration Config { get; private set; }
        public Kaleidoscope.Config.ConfigManager ConfigManager { get; private set; }
        public Kaleidoscope.Config.GeneralConfig GeneralConfig { get; private set; }
        public Kaleidoscope.Config.SamplerConfig SamplerConfig { get; private set; }
        public Kaleidoscope.Config.WindowConfig WindowConfig { get; private set; }

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly WindowSystem windowSystem;
        private readonly Kaleidoscope.Gui.MainWindow.MainWindow mainWindow;
        private readonly Kaleidoscope.Gui.MainWindow.FullscreenWindow fullscreenWindow;
        private readonly Kaleidoscope.Gui.MainWindow.MoneyTrackerComponent moneyTrackerComponent;
        private System.Threading.Timer? _samplerTimer;
        private string? _dbPath;
        private volatile bool _samplerEnabled = true;
        private int _samplerIntervalSeconds = 1; // default to 1 second
        private readonly Kaleidoscope.Gui.ConfigWindow.ConfigWindow configWindow;

        public KaleidoscopePlugin(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));

            // Initialize ECommons services (Svc) so UI components and services can access Dalamud services.
            ECommons.DalamudServices.Svc.Init(pluginInterface);

            var cfg = this.pluginInterface.GetPluginConfig() as Kaleidoscope.Configuration;
            if (cfg == null)
            {
                cfg = new Kaleidoscope.Configuration();
                this.pluginInterface.SavePluginConfig(cfg);
            }
            this.Config = cfg;

            // Create per-category config manager and load per-category files. Keep the single plugin config as compatibility
            // and copy values to/from per-category objects so runtime code continues to read from `this.Config`.
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

            // Copy per-file values into the runtime single config for compatibility with existing code
            try { this.Config.ShowOnStart = this.GeneralConfig.ShowOnStart; } catch { }
            try { this.Config.PinMainWindow = this.WindowConfig.PinMainWindow; } catch { }
            try { this.Config.PinConfigWindow = this.WindowConfig.PinConfigWindow; } catch { }
            try { this.Config.MainWindowPos = this.WindowConfig.MainWindowPos; } catch { }
            try { this.Config.MainWindowSize = this.WindowConfig.MainWindowSize; } catch { }
            try { this.Config.ConfigWindowPos = this.WindowConfig.ConfigWindowPos; } catch { }
            try { this.Config.ConfigWindowSize = this.WindowConfig.ConfigWindowSize; } catch { }

            this.windowSystem = new WindowSystem("Kaleidoscope");
            _dbPath = System.IO.Path.Combine(saveDir, "moneytracker.sqlite");
            // Create and pass simple sampler controls to the UI (callbacks)
            // Expose sampler interval to the UI in milliseconds; convert back to seconds for the internal timer.
            // Create a shared MoneyTrackerComponent and provide it to both main and fullscreen windows
            this.moneyTrackerComponent = new Kaleidoscope.Gui.MainWindow.MoneyTrackerComponent(_dbPath,
                () => _samplerEnabled,
                enabled => _samplerEnabled = enabled,
                () => _samplerIntervalSeconds * 1000,
                ms => {
                    if (ms <= 0) ms = 1;
                    var sec = (ms + 999) / 1000; // convert ms to seconds, rounding up
                    _samplerIntervalSeconds = sec;
                    // Recreate the timer with the new interval if it's enabled
                    _samplerTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_samplerIntervalSeconds));
                }
            );

            this.mainWindow = new Kaleidoscope.Gui.MainWindow.MainWindow(this, this.moneyTrackerComponent, _dbPath,
                () => _samplerEnabled,
                enabled => _samplerEnabled = enabled,
                () => _samplerIntervalSeconds * 1000,
                ms => {
                    if (ms <= 0) ms = 1;
                    var sec = (ms + 999) / 1000; // convert ms to seconds, rounding up
                    _samplerIntervalSeconds = sec;
                    _samplerTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_samplerIntervalSeconds));
                }
            );

            this.fullscreenWindow = new Kaleidoscope.Gui.MainWindow.FullscreenWindow(this, this.moneyTrackerComponent);
            // Start a basic sampler that uses the same storage to record gil periodically while the plugin runs.
                _samplerTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (!_samplerEnabled) return;
                    var now = DateTime.UtcNow;
                    var cid = ECommons.DalamudServices.Svc.ClientState.LocalContentId;
                    uint gil = 0;
                    unsafe
                    {
                        var im = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
                        if (im != null) gil = im->GetGil();
                        var cm = FFXIVClientStructs.FFXIV.Client.Game.CurrencyManager.Instance();
                        if (gil == 0 && cm != null)
                        {
                            try { gil = cm->GetItemCount(1); } catch { gil = 0; }
                        }
                    }
                    try
                    {
                        if (!string.IsNullOrEmpty(_dbPath))
                        {
                            var dir = System.IO.Path.GetDirectoryName(_dbPath)!;
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                            var csb = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWriteCreate };
                            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(csb.ToString());
                            conn.Open();
                            using var initCmd = conn.CreateCommand();
                            initCmd.CommandText = @"CREATE TABLE IF NOT EXISTS series (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    variable TEXT NOT NULL,
    character_id INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    series_id INTEGER NOT NULL,
    timestamp INTEGER NOT NULL,
    value INTEGER NOT NULL,
    FOREIGN KEY(series_id) REFERENCES series(id)
);

CREATE INDEX IF NOT EXISTS idx_series_variable_character ON series(variable, character_id);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp ON points(series_id, timestamp);
";
                            initCmd.ExecuteNonQuery();
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = "SELECT id FROM series WHERE variable = $v AND character_id = $c LIMIT 1";
                            cmd.Parameters.AddWithValue("$v", "Gil");
                            cmd.Parameters.AddWithValue("$c", (long)cid);
                            var r = cmd.ExecuteScalar();
                            long seriesId;
                            if (r != null && r != DBNull.Value) seriesId = (long)r;
                            else
                            {
                                cmd.CommandText = "INSERT INTO series(variable, character_id) VALUES($v, $c); SELECT last_insert_rowid();";
                                seriesId = (long)cmd.ExecuteScalar();
                            }
                            // Avoid inserting duplicate consecutive points: check last saved value for this series.
                            cmd.CommandText = "SELECT value FROM points WHERE series_id = $s ORDER BY timestamp DESC LIMIT 1";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("$s", seriesId);
                            var lastValObj = cmd.ExecuteScalar();
                            var shouldInsert = true;
                            if (lastValObj != null && lastValObj != DBNull.Value)
                            {
                                try
                                {
                                    var lastVal = (long)lastValObj;
                                    if (lastVal == (long)gil) shouldInsert = false;
                                }
                                catch { }
                            }
                            if (shouldInsert)
                            {
                                cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, $v)";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("$s", seriesId);
                                cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToUniversalTime().Ticks);
                                cmd.Parameters.AddWithValue("$v", (long)gil);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    catch { }
                }
                catch { }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(_samplerIntervalSeconds));
            this.configWindow = new Kaleidoscope.Gui.ConfigWindow.ConfigWindow(
                this,
                this.Config,
                () => this.pluginInterface.SavePluginConfig(this.Config),
                () => _samplerEnabled,
                enabled => _samplerEnabled = enabled,
                () => _samplerIntervalSeconds * 1000,
                ms => {
                    if (ms <= 0) ms = 1;
                    var sec = (ms + 999) / 1000;
                    _samplerIntervalSeconds = sec;
                    _samplerTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_samplerIntervalSeconds));
                },
                () => this.mainWindow.HasDb,
                () => { try { this.mainWindow.ClearAllData(); } catch { } },
                () => { try { return this.mainWindow.CleanUnassociatedCharacters(); } catch { return 0; } },
                () => { try { return this.mainWindow.ExportCsv(); } catch { return null; } }
            );

            this.windowSystem.AddWindow(this.mainWindow);
            this.windowSystem.AddWindow(this.fullscreenWindow);
            this.windowSystem.AddWindow(this.configWindow);

            // Open the main window by default when the plugin loads
            this.mainWindow.IsOpen = true;

            // Ensure fullscreen window starts closed
            this.fullscreenWindow.IsOpen = false;

            // Register chat/command handlers to open the main UI
            try
            {
                ECommons.DalamudServices.Svc.Commands.AddHandler("/kld", new Dalamud.Game.Command.CommandInfo((s, a) => this.OpenMainUi()) { HelpMessage = "Open Kaleidoscope UI", ShowInHelp = true });
                ECommons.DalamudServices.Svc.Commands.AddHandler("/kaleidoscope", new Dalamud.Game.Command.CommandInfo((s, a) => this.OpenMainUi()) { HelpMessage = "Open Kaleidoscope UI", ShowInHelp = true });
            }
            catch { }
            this.pluginInterface.UiBuilder.Draw += this.DrawUi;
            this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
            this.pluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;
        }

        public void SaveConfig()
        {
            try { this.pluginInterface.SavePluginConfig(this.Config); } catch { }
            // Also persist per-category files by copying values from the runtime config into files
            try
            {
                // General
                var g = new Kaleidoscope.Config.GeneralConfig { ShowOnStart = this.Config.ShowOnStart };
                this.ConfigManager.Save("general.json", g);
                // Sampler
                var s = new Kaleidoscope.Config.SamplerConfig { SamplerEnabled = this._samplerEnabled, SamplerIntervalMs = this._samplerIntervalSeconds * 1000 };
                this.ConfigManager.Save("sampler.json", s);
                // Windows
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

        public void Dispose()
        {
            try
            {
                ECommons.DalamudServices.Svc.Commands.RemoveHandler("/kld");
                ECommons.DalamudServices.Svc.Commands.RemoveHandler("/kaleidoscope");
            }
            catch { }
            this.pluginInterface.UiBuilder.Draw -= this.DrawUi;
            this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
            this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;
            this.windowSystem.RemoveAllWindows();
            if (this.mainWindow is IDisposable mw)
                mw.Dispose();

            if (this.fullscreenWindow is IDisposable fw)
                fw.Dispose();

            if (this.configWindow is IDisposable cw)
                cw.Dispose();
            _samplerTimer?.Dispose();
        }

        // Called by MainWindow to request showing the fullscreen window. This hides the main window.
        public void RequestShowFullscreen()
        {
            try
            {
                // Hide main and show fullscreen
                this.mainWindow.IsOpen = false;
                this.fullscreenWindow.IsOpen = true;
                TopBar.ForceHide();
            }
            catch { }
        }

        // Called by TopBar/FullscreenWindow to request exiting fullscreen and restoring main window
        public void RequestExitFullscreen()
        {
            try
            {
                // Close fullscreen and reopen main
                this.fullscreenWindow.IsOpen = false;
                this.mainWindow.IsOpen = true;
                try { this.mainWindow.ExitFullscreen(); } catch { }
            }
            catch { }
        }

        private void DrawUi() => this.windowSystem.Draw();

        public void OpenConfigUi() => this.configWindow.IsOpen = true;

        private void OpenMainUi() => this.mainWindow.IsOpen = true;
    }
}
