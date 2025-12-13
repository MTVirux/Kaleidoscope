namespace Kaleidoscope.Gui.MainWindow
{
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    

    public class FullscreenWindow : Window
    {
        
        private readonly GilTrackerComponent _moneyTracker;
        private readonly WindowContentContainer _contentContainer;
        private readonly Kaleidoscope.KaleidoscopePlugin plugin;

        public FullscreenWindow(Kaleidoscope.KaleidoscopePlugin plugin, GilTrackerComponent sharedMoneyTracker) : base("Kaleidoscope Fullscreen", ImGuiWindowFlags.NoDecoration)
        {
            this.plugin = plugin;
            // Use the shared gil tracker from the main plugin instance
            _moneyTracker = sharedMoneyTracker;

            // Create a content container similar to the main window so HUD tools
            // can be reused in fullscreen mode. Keep registrations minimal — the
            // gil tracker reuses the shared tracker instance.
            _contentContainer = new WindowContentContainer(() => plugin.Config.ContentGridCellWidthPercent, () => plugin.Config.ContentGridCellHeightPercent, () => plugin.Config.GridSubdivisions);

            try
            {
                // Register the same toolset as the main window. Registrar will
                // construct concrete tool instances; each instance is independent.
                var dbPath = _moneyTracker?.DbPath;
                WindowToolRegistrar.RegisterTools(_contentContainer, dbPath);

                // Add a default independent GilTracker tool instance (connected to same DB file)
                try
                {
                    var defaultGt = WindowToolRegistrar.CreateToolInstance("GilTracker", new System.Numerics.Vector2(20, 50), dbPath);
                    if (defaultGt != null) _contentContainer.AddTool(defaultGt);
                }
                catch { }
            }
            catch { }
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
            try
            {
                // Draw the content container occupying the fullscreen window.
                // The container computes its drawing area from the current ImGui window
                // so simply calling Draw will render tools laid out as in the main window.
                // In fullscreen, default to non-edit mode. Only enable edit mode
                // while the user is actively holding CTRL+SHIFT.
                try
                {
                    var io = ImGui.GetIO();
                    var fsEdit = io.KeyCtrl && io.KeyShift;
                    _contentContainer?.Draw(fsEdit);
                }
                catch
                {
                    // Fall back to config value if IO access fails for any reason
                    _contentContainer?.Draw(this.plugin.Config.EditMode);
                }
            }
            catch { }
        }
    }
}
