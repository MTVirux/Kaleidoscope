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

        public MainWindow(string? moneyTrackerDbPath = null, Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null, Func<int>? getSamplerInterval = null, Action<int>? setSamplerInterval = null) : base(GetDisplayTitle())
        {
            Size = new System.Numerics.Vector2(600, 360);
            _moneyTracker = new MoneyTrackerComponent(moneyTrackerDbPath, getSamplerEnabled, setSamplerEnabled, getSamplerInterval, setSamplerInterval);
        }

        public override void Draw()
        {
            ImGui.TextUnformatted("Main UI");
            ImGui.Separator();
            if (_moneyTracker.HasDb)
            {
                if (ImGui.Button("Sanitize DB Data"))
                {
                    ImGui.OpenPopup("main_sanitize_db_confirm");
                    _sanitizeDbOpen = true;
                }
            }
            if (ImGui.BeginPopupModal("main_sanitize_db_confirm", ref _sanitizeDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("This will remove Money Tracker data for characters that do not have a stored name association. Proceed?");
                if (ImGui.Button("Yes"))
                {
                    try { _moneyTracker.CleanUnassociatedCharacters(); } catch { }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
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
