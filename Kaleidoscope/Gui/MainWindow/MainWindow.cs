using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using OtterGui.Text;
using Dalamud.Interface;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Main plugin window containing the HUD layout.
/// </summary>
public class MainWindow : Window
{
        private readonly IPluginLog _log;
        private readonly ConfigurationService _configService;
        private readonly SamplerService _samplerService;
        private readonly FilenameService _filenameService;
        private readonly StateService _stateService;
        private readonly LayoutEditingService _layoutEditingService;
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

        // Flag to track if this is the first PreDraw call (used for initial positioning)
        private bool _firstPreDraw = true;

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
            LayoutEditingService layoutEditingService,
            GilTrackerComponent gilTrackerComponent) 
            : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _log = log;
            _configService = configService;
            _samplerService = samplerService;
            _filenameService = filenameService;
            _stateService = stateService;
            _layoutEditingService = layoutEditingService;
            _moneyTracker = gilTrackerComponent;

            SizeConstraints = new WindowSizeConstraints() { MinimumSize = ConfigStatic.MinimumWindowSize };

            InitializeTitleBarButtons();
            InitializeContentContainer();

            // Initialize last-saved pos/size from config so change detection starts correct
            _lastSavedPos = Config.MainWindowPos;
            _lastSavedSize = Config.MainWindowSize;
            
            // Update window title when dirty state changes
            _layoutEditingService.OnDirtyStateChanged += (isDirty) =>
            {
                UpdateWindowTitle();
            };

            _log.Debug("MainWindow initialized");
        }
        
        /// <summary>
        /// Updates the window title to reflect the current layout and dirty state.
        /// </summary>
        private void UpdateWindowTitle()
        {
            var baseTitle = GetDisplayTitle();
            var layoutName = _layoutEditingService.CurrentLayoutName;
            if (!string.IsNullOrWhiteSpace(layoutName))
            {
                var suffix = _layoutEditingService.IsDirty ? " *" : "";
                // In debug builds show the full, more descriptive label. In release
                // builds keep the title short ("Kaleidoscope x.x.x.x - {LayoutName}").
#if DEBUG
                WindowName = $"{baseTitle} - Layout: {layoutName}{suffix}";
#else
                WindowName = $"{baseTitle} - {layoutName}{suffix}";
#endif
            }
            else
            {
                WindowName = baseTitle;
            }
        }

        private Configuration Config => _configService.Config;

        private void InitializeTitleBarButtons()
        {
            // Save layout button (appears only when dirty and in edit mode)
            TitleBarButtons.Add(new TitleBarButton
            {
                Click = (m) => 
                { 
                    if (m == ImGuiMouseButton.Left && _layoutEditingService.IsDirty)
                    {
                        _layoutEditingService.Save();
                        UpdateWindowTitle();
                    }
                },
                Icon = FontAwesomeIcon.Save,
                IconOffset = new Vector2(2, 2),
                ShowTooltip = () => 
                {
                    if (_layoutEditingService.IsDirty)
                        ImGui.SetTooltip("Save layout changes");
                    else
                        ImGui.SetTooltip("No unsaved changes");
                },
            });
            
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
                ShowTooltip = () => 
                {
                    var dirty = _layoutEditingService.IsDirty ? " (unsaved changes)" : "";
                    ImGui.SetTooltip($"Toggle HUD edit mode{dirty}");
                },
            };
            editModeButton.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    if (_stateService.IsEditMode)
                    {
                        // Turning off edit mode - prompt to save if dirty
                        if (_layoutEditingService.IsDirty)
                        {
                            _contentContainer?.ShowUnsavedChangesDialog("exit edit mode", () =>
                            {
                                _stateService.ToggleEditMode();
                            });
                        }
                        else
                        {
                            _stateService.ToggleEditMode();
                        }
                    }
                    else
                    {
                        // Turning on edit mode - save window position before locking
                        Config.MainWindowPos = ImGui.GetWindowPos();
                        Config.MainWindowSize = ImGui.GetWindowSize();
                        _stateService.ToggleEditMode();
                    }
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

            // If no tools were restored from a layout, add the Getting Started guide
            try
            {
                var exported = _contentContainer?.ExportLayout() ?? new System.Collections.Generic.List<ToolLayoutState>();
                if (exported.Count == 0)
                {
                    var gettingStarted = WindowToolRegistrar.CreateToolInstance("GettingStarted", new Vector2(20, 50), _filenameService, _samplerService);
                    if (gettingStarted != null) _contentContainer?.AddTool(gettingStarted);
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
                
                // Initialize the layout editing service with the loaded layout
                _layoutEditingService.InitializeFromPersisted(
                    layout.Name, 
                    LayoutType.Windowed, 
                    layout.Tools, 
                    _contentContainer?.GridSettings);
                UpdateWindowTitle();
            }
            else
            {
                // No layout exists, initialize with defaults
                _layoutEditingService.InitializeFromPersisted(
                    "Default", 
                    LayoutType.Windowed, 
                    new List<ToolLayoutState>(), 
                    _contentContainer?.GridSettings);
                UpdateWindowTitle();
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
                
                // After saving as new name, initialize the editing service for this layout
                _layoutEditingService.InitializeFromPersisted(name, LayoutType.Windowed, tools, _contentContainer.GridSettings);
                UpdateWindowTitle();
                
                _log.Information($"Saved layout '{name}' ({existing.Tools.Count} tools)");
            };

            _contentContainer.OnLoadLayout = (name) =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                
                // Check for unsaved changes before switching layouts
                if (!_layoutEditingService.TrySwitchLayout(name, LayoutType.Windowed, () =>
                {
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
                        
                        // Initialize the editing service for the new layout
                        _layoutEditingService.InitializeFromPersisted(name, LayoutType.Windowed, found.Tools, _contentContainer.GridSettings);
                        UpdateWindowTitle();
                        
                        _log.Information($"Loaded layout '{name}' ({found.Tools.Count} tools)");
                    }
                }))
                {
                    // If dirty, the dialog will be shown and the action will be deferred
                    _contentContainer.ShowUnsavedChangesDialog($"switch to layout '{name}'", () =>
                    {
                        var layouts = Config.Layouts ?? new List<ContentLayoutState>();
                        var found = layouts.Find(x => x.Name == name && x.Type == LayoutType.Windowed);
                        if (found != null)
                        {
                            _contentContainer.SetGridSettingsFromLayout(found);
                            _contentContainer.ApplyLayout(found.Tools);
                            Config.ActiveWindowedLayoutName = name;
                            _configService.Save();
                            _layoutEditingService.InitializeFromPersisted(name, LayoutType.Windowed, found.Tools, _contentContainer.GridSettings);
                            UpdateWindowTitle();
                            _log.Information($"Loaded layout '{name}' ({found.Tools.Count} tools)");
                        }
                    });
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

            // Wire layout change callback - marks dirty instead of auto-saving
            _contentContainer.OnLayoutChanged = (tools) =>
            {
                _layoutEditingService.MarkDirty(tools, _contentContainer.GridSettings);
            };

            // Wire grid settings change callback - marks dirty instead of auto-saving
            _contentContainer.OnGridSettingsChanged = (gridSettings) =>
            {
                _layoutEditingService.MarkDirty(_contentContainer.ExportLayout(), gridSettings);
            };
            
            // Wire explicit save callback
            _contentContainer.OnSaveLayoutExplicit = () =>
            {
                _layoutEditingService.Save();
                UpdateWindowTitle();
            };
            
            // Wire discard changes callback
            _contentContainer.OnDiscardChanges = () =>
            {
                _layoutEditingService.DiscardChanges();
                
                // Reload the layout from persisted state
                var layouts = Config.Layouts ?? new List<ContentLayoutState>();
                var found = layouts.Find(x => x.Name == _layoutEditingService.CurrentLayoutName && x.Type == LayoutType.Windowed);
                if (found != null)
                {
                    _contentContainer.SetGridSettingsFromLayout(found);
                    _contentContainer.ApplyLayout(found.Tools);
                }
                
                UpdateWindowTitle();
            };
            
            // Wire dirty state query
            _contentContainer.GetIsDirty = () => _layoutEditingService.IsDirty;
            
            // Wire current layout name query
            _contentContainer.GetCurrentLayoutName = () => _layoutEditingService.CurrentLayoutName;
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
            // Use the LayoutEditingService to save instead of manually persisting
            if (_layoutEditingService.IsDirty)
            {
                _layoutEditingService.Save();
                UpdateWindowTitle();
            }
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
            // On first PreDraw, apply the saved position/size from config so the window
            // opens where it was last closed, regardless of lock state.
            if (_firstPreDraw)
            {
                _firstPreDraw = false;
                ImGui.SetNextWindowPos(Config.MainWindowPos);
                ImGui.SetNextWindowSize(Config.MainWindowSize);
                // Also sync the tracking variables so we don't detect a spurious change
                _savedPos = Config.MainWindowPos;
                _savedSize = Config.MainWindowSize;
                _lastSavedPos = Config.MainWindowPos;
                _lastSavedSize = Config.MainWindowSize;
            }

            // Prevent the main window from being moved/resized when locked or when
            // a contained tool is currently being dragged or resized.
            // When tools are being dragged/resized, we lock the window position but use
            // the CURRENT position (not Config) to avoid snapping the window.
            if (_stateService.IsLocked)
            {
                // Window is locked: force position from config
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
                ImGui.SetNextWindowPos(Config.MainWindowPos);
                ImGui.SetNextWindowSize(Config.MainWindowSize);
            }
            else if (_stateService.IsDragging || _stateService.IsResizing)
            {
                // Tool is being dragged/resized: prevent window movement but keep current position
                // Use _prevFramePos/_prevFrameSize which track the current window state
                Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
                if (_prevFrameInitialized)
                {
                    ImGui.SetNextWindowPos(_prevFramePos);
                    ImGui.SetNextWindowSize(_prevFrameSize);
                }
                else
                {
                    // Fallback to config values if we haven't tracked position yet
                    ImGui.SetNextWindowPos(Config.MainWindowPos);
                    ImGui.SetNextWindowSize(Config.MainWindowSize);
                }
            }
            else
            {
                // Normal mode: allow movement and resize
                Flags &= ~ImGuiWindowFlags.NoMove;
                Flags &= ~ImGuiWindowFlags.NoResize;
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
                
                // Note: Layout changes are now tracked by LayoutEditingService.
                // The dirty flag is consumed internally and marked in the service.
                // Auto-save has been removed - user must explicitly save.
                if (_contentContainer != null)
                {
                    _contentContainer.TryConsumeLayoutDirty(); // Clear internal flag if set
                }
                
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
            catch (Exception ex) { LogService.Debug($"[MainWindow] Draw failed: {ex.Message}"); }
        }

    private static string GetDisplayTitle()
    {
        var asm = Assembly.GetExecutingAssembly();
        // Show the full informational version (may include commit/branch metadata)
        // only in DEBUG builds. For release builds prefer the assembly version
        // to keep the title concise.
    #if DEBUG
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var asmVer = asm.GetName().Version?.ToString();
        var ver = !string.IsNullOrEmpty(infoVer) ? infoVer : (!string.IsNullOrEmpty(asmVer) ? asmVer : "0.0.0");
        return $"Kaleidoscope {ver}";
    #else
        var asmVer = asm.GetName().Version?.ToString() ?? "VERSION_RESOLUTION_ERROR";
        return $"Kaleidoscope {asmVer}";
    #endif
    }
}
