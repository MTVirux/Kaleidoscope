namespace Kaleidoscope.Gui.MainWindow
{
    using System;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using ECommons.Logging;
    using Kaleidoscope.Gui.TopBar;

    public class FullscreenWindow : Window
    {
#if DEBUG
        private System.Numerics.Vector2 _lastWindowPos = new System.Numerics.Vector2(float.MinValue, float.MinValue);
        private System.Numerics.Vector2 _lastWindowSize = new System.Numerics.Vector2(float.MinValue, float.MinValue);
#endif
        private readonly MoneyTrackerComponent _moneyTracker;
        private readonly Kaleidoscope.KaleidoscopePlugin plugin;

        public FullscreenWindow(Kaleidoscope.KaleidoscopePlugin plugin, MoneyTrackerComponent sharedMoneyTracker) : base("Kaleidoscope Fullscreen", ImGuiWindowFlags.NoDecoration)
        {
            this.plugin = plugin;
            _moneyTracker = sharedMoneyTracker;
            // Ensure the topbar can request exit; plugin handles the actual toggle
            TopBar.OnExitFullscreenRequested = () => { try { this.plugin.RequestExitFullscreen(); } catch { } };
        }

        public override void PreDraw()
        {
            // Force fullscreen positioning and disable move/resize/title
            Flags |= ImGuiWindowFlags.NoMove;
            Flags |= ImGuiWindowFlags.NoResize;
            Flags |= ImGuiWindowFlags.NoTitleBar;
            Flags |= ImGuiWindowFlags.NoBringToFrontOnFocus;
            try
            {
                var io = ImGui.GetIO();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(0f, 0f));
                ImGui.SetNextWindowSize(io.DisplaySize);
            }
            catch { }
        }

        public override void Draw()
        {
            // Draw topbar on fullscreen window so the exit button is present
            try { TopBar.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize()); } catch { }

#if DEBUG
            try
            {
                var pos = ImGui.GetWindowPos();
                var size = ImGui.GetWindowSize();
                if (pos != _lastWindowPos || size != _lastWindowSize)
                {
                    _lastWindowPos = pos;
                    _lastWindowSize = size;
                    PluginLog.Verbose($"Main window pos=({pos.X:F1},{pos.Y:F1}) size=({size.X:F1}x{size.Y:F1})");
                }
            }
            catch { }
#endif
        }
    }
}
