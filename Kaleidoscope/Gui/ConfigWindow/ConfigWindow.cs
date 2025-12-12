namespace Kaleidoscope.Gui.ConfigWindow
{
    using System;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

    public class ConfigWindow : Window, IDisposable
    {
        private readonly Kaleidoscope.Configuration config;
        private readonly Action saveConfig;
        private readonly Func<bool>? getSamplerEnabled;
        private readonly Action<bool>? setSamplerEnabled;
        private readonly Func<int>? getSamplerIntervalMs;
        private readonly Action<int>? setSamplerIntervalMs;

        public ConfigWindow(Kaleidoscope.Configuration config, Action saveConfig,
            Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null,
            Func<int>? getSamplerIntervalMs = null, Action<int>? setSamplerIntervalMs = null)
            : base("Kaleidoscope Configuration")
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.saveConfig = saveConfig ?? throw new ArgumentNullException(nameof(saveConfig));
            this.getSamplerEnabled = getSamplerEnabled;
            this.setSamplerEnabled = setSamplerEnabled;
            this.getSamplerIntervalMs = getSamplerIntervalMs;
            this.setSamplerIntervalMs = setSamplerIntervalMs;

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

            // Window pin settings
            var pinMain = this.config.PinMainWindow;
            if (ImGui.Checkbox("Pin main window", ref pinMain))
            {
                this.config.PinMainWindow = pinMain;
                this.saveConfig();
            }
            var pinConfig = this.config.PinConfigWindow;
            if (ImGui.Checkbox("Pin config window", ref pinConfig))
            {
                this.config.PinConfigWindow = pinConfig;
                this.saveConfig();
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
