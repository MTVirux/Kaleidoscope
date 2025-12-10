namespace CrystalTerror.Gui.ConfigWindow
{
    using System;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

        public class ConfigWindow : Window, IDisposable
    {
        public ConfigWindow() : base("CrystalTerror Configuration")
        {
            this.SizeConstraints = new WindowSizeConstraints() { MinimumSize = new System.Numerics.Vector2(300, 200) };
        }

        public void Dispose() { }

        public override void Draw()
        {
            ImGui.TextUnformatted("Configuration UI removed.");
        }
    }
}
