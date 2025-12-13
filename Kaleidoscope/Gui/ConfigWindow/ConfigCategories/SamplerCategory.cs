namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Kaleidoscope.Services;

    public class SamplerCategory
    {
        private readonly SamplerService _samplerService;
        private readonly ConfigurationService _configService;

        public SamplerCategory(SamplerService samplerService, ConfigurationService configService)
        {
            _samplerService = samplerService;
            _configService = configService;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Sampler");
            ImGui.Separator();

            var enabled = _samplerService.Enabled;
            if (ImGui.Checkbox("Enable sampler", ref enabled))
            {
                _samplerService.Enabled = enabled;
                _configService.Save();
            }

            var interval = _samplerService.IntervalMs;
            if (ImGui.InputInt("Sampler interval (ms)", ref interval))
            {
                if (interval < 1) interval = 1;
                _samplerService.IntervalMs = interval;
                _configService.Save();
            }
        }
    }
}
