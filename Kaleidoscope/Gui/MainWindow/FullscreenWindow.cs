namespace Kaleidoscope.Gui.MainWindow
{
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using Kaleidoscope.Gui.TopBar;
    using Kaleidoscope.Gui.Widgets;

    public class FullscreenWindow : Window
    {
        
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
            // Draw the fullscreen content inside the content container.
            try
            {
                using var c = ContentContainer.Begin(5f);
                _moneyTracker?.Draw();
            }
            catch { }
        }
    }
}
