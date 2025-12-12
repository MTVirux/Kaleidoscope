namespace Kaleidoscope
{
    using System;
    using Dalamud.Interface.Windowing;
    using Microsoft.Data.Sqlite;
    using System.IO;
    using Dalamud.Plugin;

    public sealed class KaleidoscopePlugin : IDalamudPlugin, IDisposable
    {
        public string Name => "Crystal Terror";

        public Kaleidoscope.Configuration Config { get; private set; }

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly WindowSystem windowSystem;
        private readonly Kaleidoscope.Gui.MainWindow.MainWindow mainWindow;
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

            this.windowSystem = new WindowSystem("Kaleidoscope");
            var saveDir = this.pluginInterface.GetPluginConfigDirectory();
            _dbPath = System.IO.Path.Combine(saveDir, "moneytracker.sqlite");
            // Create and pass simple sampler controls to the UI (callbacks)
            // Expose sampler interval to the UI in milliseconds; convert back to seconds for the internal timer.
            this.mainWindow = new Kaleidoscope.Gui.MainWindow.MainWindow(_dbPath,
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
                            cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, $v)";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("$s", seriesId);
                            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToUniversalTime().Ticks);
                            cmd.Parameters.AddWithValue("$v", (long)gil);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch { }
                }
                catch { }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(_samplerIntervalSeconds));
            this.configWindow = new Kaleidoscope.Gui.ConfigWindow.ConfigWindow(
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
                }
            );

            this.windowSystem.AddWindow(this.mainWindow);
            this.windowSystem.AddWindow(this.configWindow);

            // Open the main window by default when the plugin loads
            this.mainWindow.IsOpen = true;

            this.pluginInterface.UiBuilder.Draw += this.DrawUi;
            this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
            this.pluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;
        }

        public void Dispose()
        {
            this.pluginInterface.UiBuilder.Draw -= this.DrawUi;
            this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
            this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;
            this.windowSystem.RemoveAllWindows();
            if (this.mainWindow is IDisposable mw)
                mw.Dispose();

            if (this.configWindow is IDisposable cw)
                cw.Dispose();
            _samplerTimer?.Dispose();
        }

        private void DrawUi() => this.windowSystem.Draw();

        private void OpenConfigUi() => this.configWindow.IsOpen = true;

        private void OpenMainUi() => this.mainWindow.IsOpen = true;
    }
}
