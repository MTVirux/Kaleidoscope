namespace Kaleidoscope.Gui.MainWindow
{
    using System.Reflection;
    using System;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;

        public class MainWindow : Window
    {
        private readonly MoneyTrackerComponent _moneyTracker;
        private bool _sanitizeDbOpen = false;

        public bool HasDb => _moneyTracker?.HasDb ?? false;

        public void ClearAllData()
        {
            try { _moneyTracker.ClearAllData(); } catch { }
        }

        public int CleanUnassociatedCharacters()
        {
            try { return _moneyTracker.CleanUnassociatedCharacters(); } catch { return 0; }
        }

        public string? ExportCsv()
        {
            try { return _moneyTracker.ExportCsv(); } catch { return null; }
        }

        public MainWindow(string? moneyTrackerDbPath = null, Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null, Func<int>? getSamplerInterval = null, Action<int>? setSamplerInterval = null) : base(GetDisplayTitle())
        {
            Size = new System.Numerics.Vector2(600, 360);
            _moneyTracker = new MoneyTrackerComponent(moneyTrackerDbPath, getSamplerEnabled, setSamplerEnabled, getSamplerInterval, setSamplerInterval);
        }

        public override void Draw()
        {
            ImGui.TextUnformatted("Main UI");
            ImGui.Separator();
            // DB buttons moved to Config Window (Data Management)
            _moneyTracker.Draw();
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
