namespace Kaleidoscope.Gui.MainWindow
{
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    

    public class FullscreenWindow : Window
    {
        
        private readonly MoneyTrackerComponent _moneyTracker;
        private readonly Kaleidoscope.KaleidoscopePlugin plugin;

        public FullscreenWindow(Kaleidoscope.KaleidoscopePlugin plugin, MoneyTrackerComponent sharedMoneyTracker) : base("Kaleidoscope Fullscreen", ImGuiWindowFlags.NoDecoration)
        {
            this.plugin = plugin;
            _moneyTracker = sharedMoneyTracker;
            // TopBar removed: plugin will handle exit requests elsewhere.
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
            // Fullscreen window no longer draws the removed TopBar/Widgets.
        }
    }
}
