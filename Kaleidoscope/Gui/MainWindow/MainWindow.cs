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
            // Replace main content with four outlined containers stacked vertically.
            // They occupy the available content region height in percentages: 15%, 5%, 60%, 20%.
            // Use the current window size as a reliable fallback for layout calculations.
            var winSize = ImGui.GetWindowSize();
            var width = winSize.X;
            var totalHeight = winSize.Y;

            // Ensure a sane minimum height (avoid referencing nullable external Size bindings)
            if (totalHeight <= 0f)
            {
                totalHeight = 1f;
            }

            var percents = new float[] { 0.15f, 0.05f, 0.60f, 0.20f };

            for (var i = 0; i < percents.Length; ++i)
            {
                var h = MathF.Max(1f, totalHeight * percents[i]);
                var childId = $"##kaleido_container_{i}";
                ImGui.BeginChild(childId, new System.Numerics.Vector2(width, h), true);

                // Optional label (top-left) inside the container
                var label = $"Container {i + 1} - {percents[i] * 100:0}%";
                ImGui.TextUnformatted(label);

                ImGui.EndChild();

                // Add a small spacing between containers to visually separate them
                if (i < percents.Length - 1)
                    ImGui.Spacing();
            }
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
