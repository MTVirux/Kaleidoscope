namespace Kaleidoscope.Gui.MainWindow
{
    using System.Reflection;
    using System;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using OtterGui.Text;
    using Dalamud.Interface;

        public class MainWindow : Window
    {
        private readonly MoneyTrackerComponent _moneyTracker;
            // Draggable container state
            private readonly System.Numerics.Vector2[] _containerPos = new System.Numerics.Vector2[4];
            private readonly System.Numerics.Vector2[] _containerSize = new System.Numerics.Vector2[4];
            private bool[] _containerDragging = new bool[4];
            private bool[] _containerResizing = new bool[4];
            private const float ResizeHandleSize = 12f;
            private bool _containersInitialized = false;
            private const float SnapDistance = 8f;
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

        private readonly Kaleidoscope.KaleidoscopePlugin plugin;
        private TitleBarButton? lockButton;
        private TitleBarButton? fullscreenButton;
        private bool _isFullscreen = false;

        public MainWindow(Kaleidoscope.KaleidoscopePlugin plugin, string? moneyTrackerDbPath = null, Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null, Func<int>? getSamplerInterval = null, Action<int>? setSamplerInterval = null) : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.plugin = plugin;
            // Do not set `Size` here to avoid forcing a window size each frame.
            // The saved size is applied only when the user pins the window (saved on pin action).
            _moneyTracker = new MoneyTrackerComponent(moneyTrackerDbPath, getSamplerEnabled, setSamplerEnabled, getSamplerInterval, setSamplerInterval);

            // Create and add title bar buttons
            TitleBarButtons.Add(new TitleBarButton
            {
                Click = (m) => { if (m == ImGuiMouseButton.Left) plugin.OpenConfigUi(); },
                Icon = FontAwesomeIcon.Cog,
                IconOffset = new System.Numerics.Vector2(2, 2),
                ShowTooltip = () => ImGui.SetTooltip("Open settings"),
            });

            // Fullscreen toggle button
            var fsTb = new TitleBarButton
            {
                Icon = FontAwesomeIcon.ArrowsUpDownLeftRight,
                IconOffset = new System.Numerics.Vector2(2, 2),
                ShowTooltip = () => ImGui.SetTooltip(_isFullscreen ? "Exit fullscreen" : "Enter fullscreen"),
            };
            fsTb.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    _isFullscreen = !_isFullscreen;
                }
            };
            fullscreenButton = fsTb;
            TitleBarButtons.Add(fullscreenButton);

            var lockTb = new TitleBarButton
            {
                Icon = plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                IconOffset = new System.Numerics.Vector2(3, 2),
                ShowTooltip = () => ImGui.SetTooltip("Lock window position and size"),
            };

            lockTb.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    // Toggle pinned state. When enabling pin, capture the current window
                    // position and size so the window remains where the user placed it.
                    var newPinned = !plugin.Config.PinMainWindow;
                    plugin.Config.PinMainWindow = newPinned;
                    if (newPinned)
                    {
                        try
                        {
                            plugin.Config.MainWindowPos = ImGui.GetWindowPos();
                            plugin.Config.MainWindowSize = ImGui.GetWindowSize();
                        }
                        catch { }
                    }
                    try { plugin.SaveConfig(); } catch { }
                    lockTb.Icon = plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
                }
            };

            lockButton = lockTb;
            TitleBarButtons.Add(lockButton);
        }

        public override void PreDraw()
        {
            // Ensure the window is resizable in all states
            Flags &= ~ImGuiWindowFlags.NoResize;

            // Fullscreen handling: when enabled, force window to cover the display and disable move/resize
            if (_isFullscreen)
            {
                Flags |= ImGuiWindowFlags.NoMove;
                Flags |= ImGuiWindowFlags.NoResize;
                try
                {
                    var io = ImGui.GetIO();
                    ImGui.SetNextWindowPos(new System.Numerics.Vector2(0f, 0f));
                    ImGui.SetNextWindowSize(io.DisplaySize);
                }
                catch { }
            }
            else
            {
                if (this.plugin.Config.PinMainWindow)
                {
                    Flags |= ImGuiWindowFlags.NoMove;
                    ImGui.SetNextWindowPos(this.plugin.Config.MainWindowPos);
                }
                else
                {
                    Flags &= ~ImGuiWindowFlags.NoMove;
                }
            }

            // Ensure titlebar button icon reflects current configuration every frame
            if (this.lockButton != null)
            {
                this.lockButton.Icon = this.plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }
        }

        public override void Draw()
        {
            
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
