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

            // Content container grid resolution (percentages 1-100)
            ImGui.Separator();
            ImGui.TextUnformatted("Content container grid");
            ImGui.Indent();
            var width = this.config.ContentGridCellWidthPercent;
            if (ImGui.DragFloat("Cell width (%)##ContentGridWidth", ref width, 1f, 1f, 100f, "%.0f"))
            {
                if (width < 1f) width = 1f;
                if (width > 100f) width = 100f;
                this.config.ContentGridCellWidthPercent = width;
                this.saveConfig();
            }

            var height = this.config.ContentGridCellHeightPercent;
            if (ImGui.DragFloat("Cell height (%)##ContentGridHeight", ref height, 1f, 1f, 100f, "%.0f"))
            {
                if (height < 1f) height = 1f;
                if (height > 100f) height = 100f;
                this.config.ContentGridCellHeightPercent = height;
                this.saveConfig();
            }
            ImGui.Unindent();
        }
    }
}
