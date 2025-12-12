namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

    public class GeneralCategory
    {
        private readonly Kaleidoscope.KaleidoscopePlugin plugin;
        private readonly Kaleidoscope.Configuration config;
        private readonly Action saveConfig;

        public GeneralCategory(Kaleidoscope.KaleidoscopePlugin plugin, Kaleidoscope.Configuration config, Action saveConfig)
        {
            this.plugin = plugin;
            this.config = config;
            this.saveConfig = saveConfig;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("General");
            ImGui.Separator();
            var showOnStart = this.config.ShowOnStart;
            if (ImGui.Checkbox("Show on start", ref showOnStart))
            {
                this.config.ShowOnStart = showOnStart;
                this.saveConfig();
            }

            var exclusiveFs = this.config.ExclusiveFullscreen;
            if (ImGui.Checkbox("Exclusive fullscreen", ref exclusiveFs))
            {
                this.config.ExclusiveFullscreen = exclusiveFs;
                this.saveConfig();
                try
                {
                    if (exclusiveFs)
                    {
                        try { this.plugin.RequestShowFullscreen(); } catch { }
                    }
                    else
                    {
                        try { this.plugin.RequestExitFullscreen(); } catch { }
                    }
                }
                catch { }
            }
        }
    }
}
