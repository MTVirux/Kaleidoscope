namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

    public class WindowsCategory
    {
        private readonly Kaleidoscope.Configuration config;
        private readonly Action saveConfig;

        public WindowsCategory(Kaleidoscope.Configuration config, Action saveConfig)
        {
            this.config = config;
            this.saveConfig = saveConfig;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Windows");
            ImGui.Separator();
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
        }
    }
}
