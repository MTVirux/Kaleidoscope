namespace Kaleidoscope.Gui.MainWindow
{
    
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using ECommons.Logging;
    
    using OtterGui.Text;
    using System.Linq;
    using Dalamud.Interface;

        public class MainWindow : Window
    {
        private readonly GilTrackerComponent _moneyTracker;
        private readonly WindowContentContainer _contentContainer;
        private TitleBarButton? editModeButton;
            // Saved (non-fullscreen) position/size so we can restore after exiting fullscreen
            private System.Numerics.Vector2 _savedPos = new System.Numerics.Vector2(100, 100);
            private System.Numerics.Vector2 _savedSize = new System.Numerics.Vector2(800, 600);
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

        public MainWindow(Kaleidoscope.KaleidoscopePlugin plugin, GilTrackerComponent sharedMoneyTracker, string? gilTrackerDbPath = null, Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null, Func<int>? getSamplerInterval = null, Action<int>? setSamplerInterval = null) : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.plugin = plugin;
            // Do not set `Size` here to avoid forcing a window size each frame.
            // The saved size is applied only when the user pins the window (saved on pin action).
            _moneyTracker = sharedMoneyTracker ?? new GilTrackerComponent(gilTrackerDbPath, getSamplerEnabled, setSamplerEnabled, getSamplerInterval, setSamplerInterval);

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

            // TopBar removed: config and exit requests handled by titlebar buttons and plugin commands.

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
            // Edit mode toggle
            var editTb = new TitleBarButton
            {
                Icon = FontAwesomeIcon.Edit,
                IconOffset = new System.Numerics.Vector2(2, 2),
                ShowTooltip = () => ImGui.SetTooltip("Toggle HUD edit mode"),
            };
            editTb.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    var newState = !plugin.Config.EditMode;
                    // If turning off edit mode, persist current layout
                    if (!newState)
                    {
                        try
                        {
                            var layouts = plugin.Config.Layouts ??= new List<ContentLayoutState>();
                            var activeName = !string.IsNullOrWhiteSpace(plugin.Config.ActiveLayoutName) ? plugin.Config.ActiveLayoutName : null;
                            ContentLayoutState? layout = null;
                            if (activeName != null)
                                layout = layouts.Find(x => x.Name == activeName);
                            layout ??= layouts.FirstOrDefault();

                            if (layout == null)
                            {
                                // No saved layouts exist yet; create a new one with a sensible name.
                                layout = new ContentLayoutState() { Name = activeName ?? "Default" };
                                layouts.Add(layout);
                            }
                            layout.Tools = _contentContainer?.ExportLayout() ?? new List<ToolLayoutState>();
                            plugin.SaveConfig();
                        }
                        catch { }
                    }
                    else
                    {
                        // When enabling edit mode, capture the current window position/size
                        // and force the window to be pinned so it doesn't move/resize while
                        // the user edits HUD layout.
                        try
                        {
                            plugin.Config.PinMainWindow = true;
                            try { plugin.Config.MainWindowPos = ImGui.GetWindowPos(); } catch { }
                            try { plugin.Config.MainWindowSize = ImGui.GetWindowSize(); } catch { }
                            try { plugin.SaveConfig(); } catch { }
                        }
                        catch { }
                    }
                    try { plugin.Config.EditMode = newState; plugin.SaveConfig(); }
                    catch { }
                }
            };
            editModeButton = editTb;
            TitleBarButtons.Add(editModeButton);

            // Create content container and add default tools
            _contentContainer = new WindowContentContainer(() => plugin.Config.ContentGridCellWidthPercent, () => plugin.Config.ContentGridCellHeightPercent, () => plugin.Config.GridSubdivisions);
            // Register available tools into the content container's tool registry so the
            // context "Add tool" menu can enumerate them. Registration is centralized
            // in `WindowToolRegistrar` so both main and fullscreen windows expose
            // the same set of available tools.
            try { WindowToolRegistrar.RegisterTools(_contentContainer, gilTrackerDbPath); } catch { }
            // Add a default GilTracker instance (each tool has independent state)
            try
            {
                var defaultGt = WindowToolRegistrar.CreateToolInstance("GilTracker", new System.Numerics.Vector2(20, 50), gilTrackerDbPath);
                if (defaultGt != null) _contentContainer.AddTool(defaultGt);

                // Decide whether to add default-only tools (CharacterPicker).
                // If any saved layouts exist, prefer applying them and do not auto-add default tools
                // so removed tools aren't reintroduced.
                var layouts = plugin.Config.Layouts ?? new List<ContentLayoutState>();
                var activeName = !string.IsNullOrWhiteSpace(plugin.Config.ActiveLayoutName) ? plugin.Config.ActiveLayoutName : null;
                ContentLayoutState? layout = null;
                if (activeName != null)
                    layout = layouts.Find(x => x.Name == activeName);
                layout ??= layouts.FirstOrDefault();

                if (layout != null && layout.Tools != null && layout.Tools.Count > 0)
                {
                    _contentContainer.ApplyLayout(layout.Tools);
                    if (string.IsNullOrWhiteSpace(plugin.Config.ActiveLayoutName)) plugin.Config.ActiveLayoutName = layout.Name;
                }
                else
                {
                    // No saved layout present: add the Character Picker as a default tool on first run.
                    var cpTool = new Tools.CharacterPicker.CharacterPickerTool();
                    cpTool.Position = new System.Numerics.Vector2(420, 50);
                    _contentContainer.AddTool(cpTool);
                }
            }
            catch { }

            // Wire layout persistence callbacks so the content container can save/load named layouts
            _contentContainer.OnSaveLayout = (name, tools) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var layouts = plugin.Config.Layouts ??= new List<ContentLayoutState>();
                    var existing = layouts.Find(x => x.Name == name);
                    if (existing == null)
                    {
                        existing = new ContentLayoutState { Name = name };
                        layouts.Add(existing);
                    }
                    existing.Tools = tools ?? new List<ToolLayoutState>();
                    plugin.Config.ActiveLayoutName = name;
                    try { plugin.SaveConfig(); } catch { }
                    try { plugin.ConfigManager.Save("layouts.json", plugin.Config.Layouts); } catch { }
                    try { ECommons.Logging.PluginLog.Information($"Saved layout '{name}' ({existing.Tools.Count} tools)"); } catch { }
                }
                catch { }
            };

            _contentContainer.OnLoadLayout = (name) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var layouts = plugin.Config.Layouts ?? new List<ContentLayoutState>();
                    var found = layouts.Find(x => x.Name == name);
                    if (found != null)
                    {
                        _contentContainer.ApplyLayout(found.Tools);
                        plugin.Config.ActiveLayoutName = name;
                        try { plugin.SaveConfig(); } catch { }
                        try { plugin.ConfigManager.Save("layouts.json", plugin.Config.Layouts); } catch { }
                        try { ECommons.Logging.PluginLog.Information($"Loaded layout '{name}' ({found.Tools.Count} tools)"); } catch { }
                    }
                }
                catch { }
            };

            _contentContainer.GetAvailableLayoutNames = () =>
            {
                try
                {
                    return (plugin.Config.Layouts ?? new List<ContentLayoutState>()).Select(x => x.Name).ToList();
                }
                catch { return new List<string>(); }
            };

            // Immediate persistence on any layout change (drag/resize/add/remove)
            _contentContainer.OnLayoutChanged = (tools) =>
            {
                try
                {
                    var activeName = !string.IsNullOrWhiteSpace(plugin.Config.ActiveLayoutName)
                        ? plugin.Config.ActiveLayoutName
                        : (plugin.Config.Layouts?.FirstOrDefault()?.Name ?? "Default");
                    var layouts = plugin.Config.Layouts ??= new List<ContentLayoutState>();
                    var existing = layouts.Find(x => x.Name == activeName);
                    if (existing == null)
                    {
                        existing = new ContentLayoutState { Name = activeName };
                        layouts.Add(existing);
                    }
                    existing.Tools = tools ?? new List<ToolLayoutState>();
                    plugin.Config.ActiveLayoutName = activeName;
                    try { plugin.SaveConfig(); } catch { }
                    try { plugin.ConfigManager.Save("layouts.json", plugin.Config.Layouts); } catch { }
                    try { ECommons.Logging.PluginLog.Information($"Auto-saved active layout '{activeName}' ({existing.Tools.Count} tools)"); } catch { }
                }
                catch { }
            };
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

            // TopBar removed: nothing to force-hide.
        }

        // Called by host to apply a layout stored in plugin.Config by name.
        public void ApplyLayoutByName(string name)
        {
            try
            {
                var layout = plugin.Config.Layouts?.Find(x => x.Name == name) ?? plugin.Config.Layouts?.FirstOrDefault();
                if (layout != null && _contentContainer != null)
                {
                    _contentContainer.ApplyLayout(layout.Tools);
                }
            }
            catch { }
        }

        public override void PreDraw()
        {
            // When pinned, lock position and size; when unpinned, allow moving and resizing.
            // NOTE: do not force `PinMainWindow` here each frame. Pinning is captured
            // when edit mode is enabled (see the edit button handler). Respect the
            // user's manual unlock action while edit mode is active to avoid
            // unexpected jumps when toggling the lock button.
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
            // Main content drawing: render the HUD content container
            try
            {
                _contentContainer?.Draw(this.plugin.Config.EditMode);
                // If the container reports layout changes, persist them into the active layout
                    try
                    {
                        if (_contentContainer != null && _contentContainer.TryConsumeLayoutDirty())
                        {
                            try { ECommons.Logging.PluginLog.Information("Detected layout dirty, persisting active layout"); } catch { }
                            var activeName = !string.IsNullOrWhiteSpace(plugin.Config.ActiveLayoutName)
                                ? plugin.Config.ActiveLayoutName
                                : (plugin.Config.Layouts?.FirstOrDefault()?.Name ?? "Default");
                            var layouts = plugin.Config.Layouts ??= new List<ContentLayoutState>();
                            var existing = layouts.Find(x => x.Name == activeName);
                            if (existing == null)
                            {
                                existing = new ContentLayoutState { Name = activeName };
                                layouts.Add(existing);
                            }
                            existing.Tools = _contentContainer.ExportLayout();
                            plugin.Config.ActiveLayoutName = activeName;
                            try { plugin.SaveConfig(); } catch { }
                        }
                    }
                    catch { }
            }
            catch { }
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
