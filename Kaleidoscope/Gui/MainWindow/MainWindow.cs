namespace Kaleidoscope.Gui.MainWindow
{
    
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using Dalamud.Plugin.Services;
    
    using OtterGui.Text;
    using System.Linq;
    using System;
    using Dalamud.Interface;
    using Kaleidoscope.Services;

    public class MainWindow : Window
    {
        private readonly IPluginLog _log;
        private readonly ConfigurationService _configService;
        private readonly SamplerService _samplerService;
        private readonly FilenameService _filenameService;
        private readonly StateService _stateService;
        private readonly GilTrackerComponent _moneyTracker;
        private WindowContentContainer? _contentContainer;
        private TitleBarButton? editModeButton;
        // Saved (non-fullscreen) position/size so we can restore after exiting fullscreen
        private Vector2 _savedPos = ConfigStatic.DefaultWindowPosition;
        private Vector2 _savedSize = ConfigStatic.DefaultWindowSize;

        // Track last-saved (to Config) window position/size and throttle saves
        private Vector2 _lastSavedPos = ConfigStatic.DefaultWindowPosition;
        private Vector2 _lastSavedSize = ConfigStatic.DefaultWindowSize;
        private DateTime _lastSaveTime = DateTime.MinValue;
        private const int SaveThrottleMs = 500;

        // Track previous frame's window position/size to detect main window move/resize
        private Vector2 _prevFramePos = Vector2.Zero;
        private Vector2 _prevFrameSize = Vector2.Zero;
        private bool _prevFrameInitialized = false;

        // Reference to WindowService for window coordination (set after construction due to circular dependency)
        private WindowService? _windowService;
        public void SetWindowService(WindowService ws) => _windowService = ws;

        public bool HasDb => _moneyTracker?.HasDb ?? false;

        public void ClearAllData()
        {
            try { _moneyTracker.ClearAllData(); }
            catch (Exception ex) { _log.Error($"ClearAllData failed: {ex.Message}"); }
        }

        public int CleanUnassociatedCharacters()
        {
            try { return _moneyTracker.CleanUnassociatedCharacters(); }
            catch (Exception ex) { _log.Error($"CleanUnassociatedCharacters failed: {ex.Message}"); return 0; }
        }

        public string? ExportCsv()
        {
            try { return _moneyTracker.ExportCsv(); }
            catch (Exception ex) { _log.Error($"ExportCsv failed: {ex.Message}"); return null; }
        }

        private TitleBarButton? lockButton;
        private TitleBarButton? fullscreenButton;

        public MainWindow(
            IPluginLog log,
            ConfigurationService configService,
            SamplerService samplerService,
            FilenameService filenameService,
            StateService stateService,
            GilTrackerComponent gilTrackerComponent) 
            : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _log = log;
            _configService = configService;
            _samplerService = samplerService;
            _filenameService = filenameService;
            _stateService = stateService;
            _moneyTracker = gilTrackerComponent;

            SizeConstraints = new WindowSizeConstraints() { MinimumSize = ConfigStatic.MinimumWindowSize };

            InitializeTitleBarButtons();
            InitializeContentContainer();

            // Initialize last-saved pos/size from config so change detection starts correct
            _lastSavedPos = Config.MainWindowPos;
            _lastSavedSize = Config.MainWindowSize;

            _log.Debug("MainWindow initialized");
        }

        private Configuration Config => _configService.Config;

        private void InitializeTitleBarButtons()
        {
            // Settings button
            TitleBarButtons.Add(new TitleBarButton
            {
                Click = (m) => { if (m == ImGuiMouseButton.Left) _windowService?.OpenConfigWindow(); },
                Icon = FontAwesomeIcon.Cog,
                IconOffset = new Vector2(2, 2),
                ShowTooltip = () => ImGui.SetTooltip("Open settings"),
            });

            // Fullscreen toggle button
            fullscreenButton = new TitleBarButton
            {
                Icon = FontAwesomeIcon.ArrowsUpDownLeftRight,
                IconOffset = new Vector2(2, 2),
                ShowTooltip = () => ImGui.SetTooltip("Toggle fullscreen"),
            };
            fullscreenButton.Click = (m) =>
            {
                if (m != ImGuiMouseButton.Left) return;
                try
                {
                    _savedPos = ImGui.GetWindowPos();
                    _savedSize = ImGui.GetWindowSize();
                    _stateService.EnterFullscreen();
                    _windowService?.RequestShowFullscreen();
                }
                catch (Exception ex) { _log.Error($"Fullscreen toggle failed: {ex.Message}"); }
            };
            TitleBarButtons.Add(fullscreenButton);

            // Lock button
            lockButton = new TitleBarButton
            {
                Icon = _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                IconOffset = new Vector2(3, 2),
                ShowTooltip = () => ImGui.SetTooltip("Lock window position and size"),
            };
            lockButton.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    if (!_stateService.IsLocked)
                    {
                        // About to lock - save current position/size
                        Config.MainWindowPos = ImGui.GetWindowPos();
                        Config.MainWindowSize = ImGui.GetWindowSize();
                    }
                    _stateService.ToggleLocked();
                    lockButton!.Icon = _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
                }
            };
            TitleBarButtons.Add(lockButton);

            // Edit mode toggle
            editModeButton = new TitleBarButton
            {
                Icon = FontAwesomeIcon.Edit,
                IconOffset = new Vector2(2, 2),
                ShowTooltip = () => ImGui.SetTooltip("Toggle HUD edit mode"),
            };
            editModeButton.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    if (_stateService.IsEditMode)
                    {
                        // Turning off edit mode - persist layout
                        PersistCurrentLayout();
                    }
                    else
                    {
                        // Turning on edit mode - save window position before locking
                        Config.MainWindowPos = ImGui.GetWindowPos();
                        Config.MainWindowSize = ImGui.GetWindowSize();
                    }
                    _stateService.ToggleEditMode();
                }
            };
            TitleBarButtons.Add(editModeButton);
        }

        private void InitializeContentContainer()
        {
            // Create content container
            _contentContainer = new WindowContentContainer(
                () => Config.ContentGridCellWidthPercent,
                () => Config.ContentGridCellHeightPercent,
                () => Config.GridSubdivisions);

            // Register available tools
            WindowToolRegistrar.RegisterTools(_contentContainer, _filenameService, _samplerService);

            // Apply saved layout or add defaults
            ApplyInitialLayout();

            // If no tools were restored from a layout, add the default GilTracker tool
            try
            {
                var exported = _contentContainer?.ExportLayout() ?? new System.Collections.Generic.List<ToolLayoutState>();
                if (exported.Count == 0)
                {
                    var defaultGt = WindowToolRegistrar.CreateToolInstance("GilTracker", new Vector2(20, 50), _filenameService, _samplerService);
                    if (defaultGt != null) _contentContainer.AddTool(defaultGt);
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"Failed to add default tool after layout apply: {ex.Message}");
            }

            // Wire layout persistence callbacks
            WireLayoutCallbacks();

            // Wire interaction state callbacks to StateService
            WireInteractionCallbacks();
        }

        private void ApplyInitialLayout()
        {
            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            // Filter to only windowed layouts for the main window
            var windowedLayouts = layouts.Where(x => x.Type == LayoutType.Windowed).ToList();
            var activeName = !string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName) ? Config.ActiveWindowedLayoutName : null;
            ContentLayoutState? layout = null;
            
            if (activeName != null)
                layout = windowedLayouts.Find(x => x.Name == activeName);
            layout ??= windowedLayouts.FirstOrDefault();

            if (layout != null)
            {
                // Apply grid settings from the layout
                _contentContainer?.SetGridSettingsFromLayout(layout);
                
                if (layout.Tools != null && layout.Tools.Count > 0)
                {
                    _contentContainer?.ApplyLayout(layout.Tools);
                }
                
                if (string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName)) 
                    Config.ActiveWindowedLayoutName = layout.Name;
            }
        }

        private void WireLayoutCallbacks()
        {
            if (_contentContainer == null) return;
            
            _contentContainer.OnSaveLayout = (name, tools) =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                
                var layouts = Config.Layouts ??= new List<ContentLayoutState>();
                var existing = layouts.Find(x => x.Name == name);
                if (existing == null)
                {
                    existing = new ContentLayoutState { Name = name, Type = LayoutType.Windowed };
                    layouts.Add(existing);
                }
                existing.Tools = tools ?? new List<ToolLayoutState>();
                Config.ActiveWindowedLayoutName = name;
                _configService.Save();
                _configService.SaveLayouts();
                _log.Information($"Saved layout '{name}' ({existing.Tools.Count} tools)");
            };

            _contentContainer.OnLoadLayout = (name) =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                
                var layouts = Config.Layouts ?? new List<ContentLayoutState>();
                var found = layouts.Find(x => x.Name == name && x.Type == LayoutType.Windowed);
                if (found != null)
                {
                    // Apply grid settings first
                    _contentContainer.SetGridSettingsFromLayout(found);
                    // Then apply tool layout
                    _contentContainer.ApplyLayout(found.Tools);
                    Config.ActiveWindowedLayoutName = name;
                    _configService.Save();
                    _configService.SaveLayouts();
                    _log.Information($"Loaded layout '{name}' ({found.Tools.Count} tools)");
                }
            };

            _contentContainer.GetAvailableLayoutNames = () =>
            {
                return (Config.Layouts ?? new List<ContentLayoutState>())
                    .Where(x => x.Type == LayoutType.Windowed)
                    .Select(x => x.Name)
                    .ToList();
            };

            _contentContainer.OnManageLayouts = () =>
            {
                _windowService?.OpenLayoutsConfig();
            };

            _contentContainer.OnLayoutChanged = (tools) =>
            {
                var activeName = !string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName)
                    ? Config.ActiveWindowedLayoutName
                    : (Config.Layouts?.Where(x => x.Type == LayoutType.Windowed).FirstOrDefault()?.Name ?? "Default");
                var layouts = Config.Layouts ??= new List<ContentLayoutState>();
                var existing = layouts.Find(x => x.Name == activeName);
                if (existing == null)
                {
                    existing = new ContentLayoutState { Name = activeName, Type = LayoutType.Windowed };
                    layouts.Add(existing);
                }
                existing.Tools = tools ?? new List<ToolLayoutState>();
                Config.ActiveWindowedLayoutName = activeName;
                _configService.Save();
                _configService.SaveLayouts();
                _log.Debug($"Auto-saved active layout '{activeName}' ({existing.Tools.Count} tools)");
            };

            // Wire grid settings change callback
            _contentContainer.OnGridSettingsChanged = (gridSettings) =>
            {
                var activeName = !string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName)
                    ? Config.ActiveWindowedLayoutName
                    : (Config.Layouts?.Where(x => x.Type == LayoutType.Windowed).FirstOrDefault()?.Name ?? "Default");
                var layouts = Config.Layouts ??= new List<ContentLayoutState>();
                var existing = layouts.Find(x => x.Name == activeName);
                if (existing == null)
                {
                    existing = new ContentLayoutState { Name = activeName, Type = LayoutType.Windowed };
                    layouts.Add(existing);
                }
                // Apply grid settings to layout state
                gridSettings.ApplyToLayoutState(existing);
                Config.ActiveWindowedLayoutName = activeName;
                _configService.Save();
                _configService.SaveLayouts();
                _log.Debug($"Saved grid settings for layout '{activeName}'");
            };
        }

        private void WireInteractionCallbacks()
        {
            if (_contentContainer == null) return;

            // Wire drag/resize state changes from content container to StateService
            _contentContainer.OnDraggingChanged = (dragging) =>
            {
                _stateService.IsDragging = dragging;
            };

            _contentContainer.OnResizingChanged = (resizing) =>
            {
                _stateService.IsResizing = resizing;
            };

            // Wire main window interaction state so container can block tool interactions
            _contentContainer.IsMainWindowInteracting = () => _stateService.IsMainWindowInteracting;
        }

        private void PersistCurrentLayout()
        {
            var layouts = Config.Layouts ??= new List<ContentLayoutState>();
            var activeName = !string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName) ? Config.ActiveWindowedLayoutName : null;
            ContentLayoutState? layout = null;
            
            if (activeName != null)
                layout = layouts.Where(x => x.Type == LayoutType.Windowed).FirstOrDefault(x => x.Name == activeName);
            layout ??= layouts.Where(x => x.Type == LayoutType.Windowed).FirstOrDefault();

            if (layout == null)
            {
                layout = new ContentLayoutState() { Name = activeName ?? "Default", Type = LayoutType.Windowed };
                layouts.Add(layout);
            }
            layout.Tools = _contentContainer?.ExportLayout() ?? new List<ToolLayoutState>();
            _configService.Save();
        }

        // Expose an explicit exit fullscreen helper so TopBar can call it.
        public void ExitFullscreen()
        {
            ImGui.SetNextWindowPos(_savedPos);
            ImGui.SetNextWindowSize(_savedSize);
            
            if (_stateService.IsLocked)
            {
                Config.MainWindowPos = _savedPos;
                Config.MainWindowSize = _savedSize;
                _configService.Save();
            }
        }

        // Called by host to apply a layout stored in Config by name.
        public void ApplyLayoutByName(string name)
        {
            var layout = Config.Layouts?.Find(x => x.Name == name) ?? Config.Layouts?.FirstOrDefault();
            if (layout != null && _contentContainer != null)
            {
                _contentContainer.ApplyLayout(layout.Tools);
            }
        }

        public override void PreDraw()
        {
            // Prevent the main window from being moved/resized when locked or when
            // a contained tool is currently being dragged or resized.
            var preventMoveResize = _stateService.IsLocked || _stateService.IsDragging || _stateService.IsResizing;
            if (preventMoveResize)
            {
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
                ImGui.SetNextWindowPos(Config.MainWindowPos);
                ImGui.SetNextWindowSize(Config.MainWindowSize);
            }
            else
            {
                Flags &= ~ImGuiWindowFlags.NoMove;
                Flags &= ~ImGuiWindowFlags.NoResize;
            }

            // Only force the main window position/size when the window is locked.
            if (_stateService.IsLocked)
            {
                ImGui.SetNextWindowPos(Config.MainWindowPos);
                ImGui.SetNextWindowSize(Config.MainWindowSize);
            }

            Flags &= ~ImGuiWindowFlags.NoTitleBar;

            if (lockButton != null)
            {
                lockButton.Icon = _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }

            // If exclusive fullscreen is enabled, switch to fullscreen immediately
            if (Config.ExclusiveFullscreen)
            {
                IsOpen = false;
                _stateService.EnterFullscreen();
                _windowService?.RequestShowFullscreen();
            }
        }

        public override void Draw()
        {
            // Detect if main window is being moved or resized by comparing frame-to-frame position/size
            try
            {
                var curPos = ImGui.GetWindowPos();
                var curSize = ImGui.GetWindowSize();
                var io = ImGui.GetIO();
                const float eps = 0.5f;

                if (_prevFrameInitialized)
                {
                    var posChanging = Math.Abs(curPos.X - _prevFramePos.X) > eps || Math.Abs(curPos.Y - _prevFramePos.Y) > eps;
                    var sizeChanging = Math.Abs(curSize.X - _prevFrameSize.X) > eps || Math.Abs(curSize.Y - _prevFrameSize.Y) > eps;

                    if (io.MouseDown[0])
                    {
                        // Once we detect moving/resizing started, keep the state true until mouse is released
                        // (latch the state on, only clear when mouse is released)
                        if (posChanging)
                            _stateService.IsMainWindowMoving = true;
                        if (sizeChanging)
                            _stateService.IsMainWindowResizing = true;
                    }
                    else
                    {
                        // Mouse released, clear main window interaction state
                        _stateService.IsMainWindowMoving = false;
                        _stateService.IsMainWindowResizing = false;
                    }
                }

                _prevFramePos = curPos;
                _prevFrameSize = curSize;
                _prevFrameInitialized = true;
            }
            catch (Exception ex) { _log.Debug($"[MainWindow] Window interaction detection failed: {ex.Message}"); }

            // Main content drawing: render the HUD content container
            try
            {
                _contentContainer?.Draw(_stateService.IsEditMode);
                // If the container reports layout changes, persist them into the active layout
                    try
                    {
                        if (_contentContainer != null && _contentContainer.TryConsumeLayoutDirty())
                        {
                            _log.Information("Detected layout dirty, persisting active layout");
                            var activeName = !string.IsNullOrWhiteSpace(Config.ActiveWindowedLayoutName)
                                ? Config.ActiveWindowedLayoutName
                                : (Config.Layouts?.Where(x => x.Type == LayoutType.Windowed).FirstOrDefault()?.Name ?? "Default");
                            var layouts = Config.Layouts ??= new List<ContentLayoutState>();
                            var existing = layouts.Where(x => x.Type == LayoutType.Windowed).FirstOrDefault(x => x.Name == activeName);
                            if (existing == null)
                            {
                                existing = new ContentLayoutState { Name = activeName, Type = LayoutType.Windowed };
                                layouts.Add(existing);
                            }
                            existing.Tools = _contentContainer.ExportLayout();
                            Config.ActiveWindowedLayoutName = activeName;
                            _configService.Save();
                        }
                    }
                    catch (Exception ex) { LogService.Debug($"[MainWindow] Layout auto-save failed: {ex.Message}"); }
                // Detect main window position/size changes and persist them promptly (throttled)
                try
                {
                    var curPos = ImGui.GetWindowPos();
                    var curSize = ImGui.GetWindowSize();
                    const float eps = 0.5f;
                    var posChanged = Math.Abs(curPos.X - _lastSavedPos.X) > eps || Math.Abs(curPos.Y - _lastSavedPos.Y) > eps;
                    var sizeChanged = Math.Abs(curSize.X - _lastSavedSize.X) > eps || Math.Abs(curSize.Y - _lastSavedSize.Y) > eps;
                    if ((posChanged || sizeChanged) && !_stateService.IsLocked && !_stateService.IsDragging && !_stateService.IsResizing)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastSaveTime).TotalMilliseconds > SaveThrottleMs)
                        {
                            Config.MainWindowPos = curPos;
                            Config.MainWindowSize = curSize;
                            _configService.Save();
                            _lastSavedPos = curPos;
                            _lastSavedSize = curSize;
                            _lastSaveTime = now;
                            _log.Verbose($"Saved main window pos/size: {curPos}, {curSize}");
                        }
                    }
                }
                catch (Exception ex) { _log.Debug($"[MainWindow] Window pos/size auto-save failed: {ex.Message}"); }
            }
            catch (Exception ex) { LogService.Debug($"[MainWindow] OnLayoutChanged failed: {ex.Message}"); }
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
