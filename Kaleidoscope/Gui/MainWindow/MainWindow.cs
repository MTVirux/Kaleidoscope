namespace Kaleidoscope.Gui.MainWindow
{
    
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using ECommons.Logging;
    using Kaleidoscope.Gui.TopBar;
    using OtterGui.Text;
    using Dalamud.Interface;

        public class MainWindow : Window
    {
        private readonly MoneyTrackerComponent _moneyTracker;
            // Saved (non-fullscreen) position/size so we can restore after exiting fullscreen
            private System.Numerics.Vector2 _savedPos = new System.Numerics.Vector2(100, 100);
            private System.Numerics.Vector2 _savedSize = new System.Numerics.Vector2(800, 600);
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

        public MainWindow(Kaleidoscope.KaleidoscopePlugin plugin, MoneyTrackerComponent sharedMoneyTracker, string? moneyTrackerDbPath = null, Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null, Func<int>? getSamplerInterval = null, Action<int>? setSamplerInterval = null) : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.plugin = plugin;
            // Do not set `Size` here to avoid forcing a window size each frame.
            // The saved size is applied only when the user pins the window (saved on pin action).
            _moneyTracker = sharedMoneyTracker ?? new MoneyTrackerComponent(moneyTrackerDbPath, getSamplerEnabled, setSamplerEnabled, getSamplerInterval, setSamplerInterval);

            // Enforce a sensible minimum size for the main window
            this.SizeConstraints = new WindowSizeConstraints() { MinimumSize = new System.Numerics.Vector2(300, 120) };

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
                ShowTooltip = () => ImGui.SetTooltip("Toggle fullscreen"),
            };
            fsTb.Click = (m) =>
            {
                if (m != ImGuiMouseButton.Left) return;

                // Enter fullscreen: save current window pos/size and ask the plugin to show the fullscreen window.
                try
                {
                    _savedPos = ImGui.GetWindowPos();
                    _savedSize = ImGui.GetWindowSize();
                    try { this.plugin.RequestShowFullscreen(); } catch { }
                }
                catch { }
            };
            fullscreenButton = fsTb;
            TitleBarButtons.Add(fullscreenButton);

            // TopBar exit requests are handled by the plugin so it can toggle windows.
            TopBar.OnExitFullscreenRequested = () => { try { this.plugin.RequestExitFullscreen(); } catch { } };
            // TopBar config requests should open the plugin config UI.
            TopBar.OnOpenConfigRequested = () => { try { this.plugin.OpenConfigUi(); } catch { } };

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

        // Expose an explicit exit fullscreen helper so TopBar can call it.
        public void ExitFullscreen()
        {
            // Restore saved pos/size when returning from fullscreen.
            try
            {
                ImGui.SetNextWindowPos(_savedPos);
                ImGui.SetNextWindowSize(_savedSize);
                try
                {
                    if (this.plugin.Config.PinMainWindow)
                    {
                        this.plugin.Config.MainWindowPos = _savedPos;
                        this.plugin.Config.MainWindowSize = _savedSize;
                        this.plugin.SaveConfig();
                    }
                }
                catch { }
            }
            catch { }

            // Force the topbar to animate out
            TopBar.ForceHide();
        }

        public override void PreDraw()
        {
            // When pinned, lock position and size; when unpinned, allow moving and resizing.
            if (this.plugin.Config.PinMainWindow)
            {
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
                try
                {
                    ImGui.SetNextWindowPos(this.plugin.Config.MainWindowPos);
                    ImGui.SetNextWindowSize(this.plugin.Config.MainWindowSize);
                }
                catch { }
            }
            else
            {
                Flags &= ~ImGuiWindowFlags.NoMove;
                Flags &= ~ImGuiWindowFlags.NoResize;
            }

            // Ensure titlebar is visible and normal behavior when not in the separate fullscreen window.
            Flags &= ~ImGuiWindowFlags.NoTitleBar;

            // Ensure titlebar button icon reflects current configuration every frame
            if (this.lockButton != null)
            {
                this.lockButton.Icon = this.plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }

            // If exclusive fullscreen is enabled, immediately switch to fullscreen
            // when the main window is shown so the plugin opens in fullscreen mode
            // and does not remain in the main window.
            if (this.plugin.Config.ExclusiveFullscreen)
            {
                try
                {
                    // Prevent further PreDraw calls for this window by closing it
                    // before requesting the fullscreen window to open.
                    this.IsOpen = false;
                    this.plugin.RequestShowFullscreen();
                }
                catch { }
            }
        }

        public override void Draw()
        {
            // Render a top bar positioned relative to this main window (shows when Alt held).
            // Keep drawing while the topbar is animating so it can animate out after exit.
            TopBar.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            
            if (TopBar.IsAnimating)
            {
                TopBar.Draw(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            }

            // Draw the main content using the shared money tracker component
            //try { _moneyTracker?.Draw(); } catch { }
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
