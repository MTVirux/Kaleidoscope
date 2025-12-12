namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

    public class SamplerCategory
    {
        private readonly Func<bool>? _getSamplerEnabled;
        private readonly Action<bool>? _setSamplerEnabled;
        private readonly Func<int>? _getSamplerIntervalMs;
        private readonly Action<int>? _setSamplerIntervalMs;
        private readonly Action? _saveConfig;

        public SamplerCategory(Func<bool>? getSamplerEnabled, Action<bool>? setSamplerEnabled, Func<int>? getSamplerIntervalMs, Action<int>? setSamplerIntervalMs, Action? saveConfig)
        {
            this._getSamplerEnabled = getSamplerEnabled;
            this._setSamplerEnabled = setSamplerEnabled;
            this._getSamplerIntervalMs = getSamplerIntervalMs;
            this._setSamplerIntervalMs = setSamplerIntervalMs;
            this._saveConfig = saveConfig;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Sampler");
            ImGui.Separator();
            if (this._getSamplerEnabled != null && this._setSamplerEnabled != null)
            {
                var enabled = this._getSamplerEnabled();
                if (ImGui.Checkbox("Enable sampler", ref enabled))
                {
                    this._setSamplerEnabled(enabled);
                    try { this._saveConfig?.Invoke(); } catch { }
                }
            }

            if (this._getSamplerIntervalMs != null && this._setSamplerIntervalMs != null)
            {
                var interval = this._getSamplerIntervalMs();
                if (ImGui.InputInt("Sampler interval (ms)", ref interval))
                {
                    if (interval < 1) interval = 1;
                    this._setSamplerIntervalMs(interval);
                    try { this._saveConfig?.Invoke(); } catch { }
                }
            }
        }
    }
}
