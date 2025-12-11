namespace CrystalTerror.Gui.MainWindow
{
    using System.Reflection;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

        public class MainWindow : Window
    {
        private readonly string displayTitle;

        public MainWindow() : base("Kaleidoscope")
        {
            // Try informational version first, fall back to assembly version
            var asm = Assembly.GetExecutingAssembly();
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var asmVer = asm.GetName().Version?.ToString();
            var ver = !string.IsNullOrEmpty(infoVer) ? infoVer : (!string.IsNullOrEmpty(asmVer) ? asmVer : "0.0.0");

            this.displayTitle = $"Kaleidoscope {ver}";

            Size = new System.Numerics.Vector2(400, 300);
        }

        public override void Draw()
        {
            ImGui.Begin(this.displayTitle);
            ImGui.TextUnformatted("Main UI");
            ImGui.End();
        }
    }
}
