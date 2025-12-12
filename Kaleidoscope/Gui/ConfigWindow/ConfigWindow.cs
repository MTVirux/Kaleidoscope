namespace Kaleidoscope.Gui.ConfigWindow
{
    using System;
    using Dalamud.Interface.Windowing;
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

    public class ConfigWindow : Window, IDisposable
    {
        private readonly Kaleidoscope.Configuration config;
        private readonly Action saveConfig;
        private readonly Func<bool>? getSamplerEnabled;
        private readonly Action<bool>? setSamplerEnabled;
        private readonly Func<int>? getSamplerIntervalMs;
        private readonly Action<int>? setSamplerIntervalMs;
        private readonly Func<bool>? _hasDb;
        private readonly Action? _clearAllData;
        private readonly Func<int>? _cleanUnassociatedCharacters;
        private readonly Func<string?>? _exportCsv;
        private bool _clearDbOpen = false;
        private bool _sanitizeDbOpen = false;

        public ConfigWindow(Kaleidoscope.Configuration config, Action saveConfig,
            Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null,
            Func<int>? getSamplerIntervalMs = null, Action<int>? setSamplerIntervalMs = null,
            Func<bool>? hasDb = null, Action? clearAllData = null, Func<int>? cleanUnassociatedCharacters = null, Func<string?>? exportCsv = null)
            : base("Kaleidoscope Configuration")
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.saveConfig = saveConfig ?? throw new ArgumentNullException(nameof(saveConfig));
            this.getSamplerEnabled = getSamplerEnabled;
            this.setSamplerEnabled = setSamplerEnabled;
            this.getSamplerIntervalMs = getSamplerIntervalMs;
            this.setSamplerIntervalMs = setSamplerIntervalMs;
            this._hasDb = hasDb;
            this._clearAllData = clearAllData;
            this._cleanUnassociatedCharacters = cleanUnassociatedCharacters;
            this._exportCsv = exportCsv;

            this.SizeConstraints = new WindowSizeConstraints() { MinimumSize = new System.Numerics.Vector2(300, 200) };
        }

        public void Dispose() { }

        public override void Draw()
        {
            // General
            var showOnStart = this.config.ShowOnStart;
            if (ImGui.Checkbox("Show on start", ref showOnStart))
            {
                this.config.ShowOnStart = showOnStart;
                this.saveConfig();
            }

            ImGui.Separator();

            // Data Management
            ImGui.TextUnformatted("Data Management");
            ImGui.Separator();
            var hasDb = this._hasDb != null ? this._hasDb() : false;
            if (ImGui.Button("Export CSV") && hasDb)
            {
                try
                {
                    var fileName = this._exportCsv != null ? this._exportCsv() : null;
                    if (!string.IsNullOrEmpty(fileName)) ImGui.TextUnformatted($"Exported to {fileName}");
                }
                catch { }
            }

            if (hasDb)
            {
                if (ImGui.Button("Clear DB"))
                {
                    ImGui.OpenPopup("config_clear_db_confirm");
                    _clearDbOpen = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Sanitize DB Data"))
                {
                    ImGui.OpenPopup("config_sanitize_db_confirm");
                    _sanitizeDbOpen = true;
                }
            }

            if (ImGui.BeginPopupModal("config_clear_db_confirm", ref _clearDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("This will permanently delete all saved Money Tracker data from the DB for all characters. Proceed?");
                if (ImGui.Button("Yes"))
                {
                    try { this._clearAllData?.Invoke(); } catch { }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (ImGui.BeginPopupModal("config_sanitize_db_confirm", ref _sanitizeDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("This will remove Money Tracker data for characters that do not have a stored name association. Proceed?");
                if (ImGui.Button("Yes"))
                {
                    try { this._cleanUnassociatedCharacters?.Invoke(); } catch { }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            ImGui.Separator();

            // Sampler controls (optional, only if callbacks provided)
            if (this.getSamplerEnabled != null && this.setSamplerEnabled != null)
            {
                var enabled = this.getSamplerEnabled();
                if (ImGui.Checkbox("Enable sampler", ref enabled))
                {
                    this.setSamplerEnabled(enabled);
                }
            }

            if (this.getSamplerIntervalMs != null && this.setSamplerIntervalMs != null)
            {
                var interval = this.getSamplerIntervalMs();
                if (ImGui.InputInt("Sampler interval (ms)", ref interval))
                {
                    if (interval < 1) interval = 1;
                    this.setSamplerIntervalMs(interval);
                }
            }
        }
    }
}
