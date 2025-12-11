namespace CrystalTerror.Gui.MainWindow
{
    using System.Reflection;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

        public class MainWindow : Window
    {
        public MainWindow() : base(GetDisplayTitle())
        {
            Size = new System.Numerics.Vector2(400, 300);
        }

        public override void Draw()
        {
            ImGui.TextUnformatted("Main UI");
        }

        private static string GetDisplayTitle()
        {
            var asm = Assembly.GetExecutingAssembly();
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var asmVer = asm.GetName().Version?.ToString();
            var ver = !string.IsNullOrEmpty(infoVer) ? infoVer : (!string.IsNullOrEmpty(asmVer) ? asmVer : "0.0.0");
            return $"Kaleidoscope {ver}";
        }
    }
}
