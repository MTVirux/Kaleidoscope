using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Interface;
using Kaleidoscope.Services;
using Kaleidoscope.Models;
using Kaleidoscope.Gui.Widgets;
using OtterGui.Services;
using Kaleidoscope.Services.Universalis;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Main plugin window containing the HUD layout.
/// </summary>
public sealed class MainWindow : Window, IService, IDisposable, ILayoutHost
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly StateService _stateService;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly ProfilerService _profilerService;
    private readonly FrameLimiterService _frameLimiterService;
    private readonly IKeyState _keyState;
    private readonly IFramework _framework;
    private readonly ToolFactory _toolFactory;
    private readonly UniversalisWebSocketService? _webSocketService;
    private readonly AutoRetainerService? _autoRetainerIpc;
    private WindowContentContainer? _contentContainer;
    private TitleBarButton? _editModeButton;
    
    private Vector2 _savedPos = ConfigStatic.DefaultWindowPosition;
    private Vector2 _savedSize = ConfigStatic.DefaultWindowSize;

    private Vector2 _lastSavedPos = ConfigStatic.DefaultWindowPosition;
    private Vector2 _lastSavedSize = ConfigStatic.DefaultWindowSize;
    private DateTime _lastSaveTime = DateTime.MinValue;
    private const int SaveThrottleMs = 500;

    private Vector2 _prevFramePos = Vector2.Zero;
    private Vector2 _prevFrameSize = Vector2.Zero;
    private bool _prevFrameInitialized;

    private bool _firstPreDraw = true;
    private bool _pendingWindowRestore;
    private bool _suppressEscClose;
    private bool _escPressedThisFrame;
    private bool _isFullscreenMode;
    private bool _pendingEnterFullscreen;
    private bool _pendingExitFullscreen;

    /// <summary>
    /// Set after construction due to circular dependency with WindowService.
    /// </summary>
    private WindowService? _windowService;
    
    private TitleBarButton? _lockButton;
    private TitleBarButton? _fullscreenButton;
    private QuickAccessBarWidget? _quickAccessBar;

    /// <summary>
    /// Sets the WindowService reference. Required due to circular dependency.
    /// </summary>
    public void SetWindowService(WindowService ws) => _windowService = ws;

    /// <summary>
    /// Gets whether the window is currently in fullscreen mode.
    /// </summary>
    public bool IsFullscreenMode => _isFullscreenMode;

    public MainWindow(
        IPluginLog log,
        ConfigurationService configService,
        CurrencyTrackerService currencyTrackerService,
        StateService stateService,
        LayoutEditingService layoutEditingService,
        ProfilerService profilerService,
        FrameLimiterService frameLimiterService,
        ToolFactory toolFactory,
        IKeyState keyState,
        IFramework framework,
        UniversalisWebSocketService? webSocketService = null,
        AutoRetainerService? autoRetainerIpc = null) 
        : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _log = log;
        _configService = configService;
        _currencyTrackerService = currencyTrackerService;
        _stateService = stateService;
        _layoutEditingService = layoutEditingService;
        _profilerService = profilerService;
        _frameLimiterService = frameLimiterService;
        _toolFactory = toolFactory;
        _webSocketService = webSocketService;
        _autoRetainerIpc = autoRetainerIpc;
        _keyState = keyState;
        _framework = framework;
        
        // Suppress game keyboard input while in fullscreen mode.
        // Framework.Update fires before the game processes its key buffer, so clearing
        // IKeyState here prevents the game from acting on any keys (movement, menus, etc.)
        // while ImGui still receives input normally via Win32 messages.
        _framework.Update += OnFrameworkUpdate;
        
        SizeConstraints = new WindowSizeConstraints { MinimumSize = ConfigStatic.MinimumWindowSize };

        InitializeTitleBarButtons();
        InitializeContentContainer();
        InitializeQuickAccessBar();

        // Initialize last-saved pos/size from config so change detection starts correct
        _lastSavedPos = Config.MainWindowPos;
        _lastSavedSize = Config.MainWindowSize;
        
        // Update window title when dirty state changes
        _layoutEditingService.OnDirtyStateChanged += OnDirtyStateChanged;
        
        // Reload layout when changes are discarded/reverted
        _layoutEditingService.OnLayoutReverted += OnLayoutReverted;
        
        // Handle active layout changes from config (e.g., from layouts config panel)
        _configService.OnActiveLayoutChanged += OnActiveLayoutChangedFromConfig;

        _log.Debug("MainWindow initialized");
    }
    
    private void OnDirtyStateChanged(bool isDirty)
    {
        UpdateWindowTitle();
    }
    
    private void OnLayoutReverted()
    {
        // Reload the layout from persisted state after discard
        var layouts = Config.Layouts ?? new List<ContentLayoutState>();
        var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        var found = layouts.Find(x => x.Name == _layoutEditingService.CurrentLayoutName && x.Type == targetType);
        if (found != null && _contentContainer != null)
        {
            _contentContainer.SetGridSettingsFromLayout(found);
            _contentContainer.ApplyLayout(found.Tools);
        }
        UpdateWindowTitle();
    }
    
    private void OnActiveLayoutChangedFromConfig(string layoutName, LayoutType layoutType)
    {
        // Handle layout changes for the current mode
        var currentType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        if (layoutType != currentType) return;
        
        // Use the ILayoutHost.LoadLayout implementation
        ((ILayoutHost)this).LoadLayout(layoutName);
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _layoutEditingService.OnDirtyStateChanged -= OnDirtyStateChanged;
        _layoutEditingService.OnLayoutReverted -= OnLayoutReverted;
        _configService.OnActiveLayoutChanged -= OnActiveLayoutChangedFromConfig;
    }

    #region ILayoutHost Implementation

    void ILayoutHost.SaveLayout(string name, List<ToolLayoutState> tools)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        var layouts = Config.Layouts ??= new List<ContentLayoutState>();
        var existing = layouts.Find(x => x.Name == name && x.Type == targetType);
        if (existing == null)
        {
            existing = new ContentLayoutState { Name = name, Type = targetType };
            layouts.Add(existing);
        }
        existing.Tools = tools ?? new List<ToolLayoutState>();

        _contentContainer?.GridSettings?.ApplyToLayoutState(existing);

        if (_isFullscreenMode)
            Config.ActiveFullscreenLayoutName = name;
        else
            Config.ActiveWindowedLayoutName = name;

        _configService.Save();
        _configService.SaveLayouts();

        _layoutEditingService.InitializeFromPersisted(name, targetType, tools, _contentContainer?.GridSettings);
        UpdateWindowTitle();

        _log.Information($"Saved {targetType} layout '{name}' ({existing.Tools.Count} tools)");
    }

    void ILayoutHost.LoadLayout(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;

        _layoutEditingService.TrySwitchLayout(name, targetType, () =>
        {
            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            var found = layouts.Find(x => x.Name == name && x.Type == targetType);
            if (found != null)
            {
                _contentContainer?.SetGridSettingsFromLayout(found);
                _contentContainer?.ApplyLayout(found.Tools);

                if (_isFullscreenMode)
                    Config.ActiveFullscreenLayoutName = name;
                else
                    Config.ActiveWindowedLayoutName = name;

                _configService.Save();

                _layoutEditingService.InitializeFromPersisted(name, targetType, found.Tools, _contentContainer?.GridSettings);
                UpdateWindowTitle();

                _log.Information($"Loaded {targetType} layout '{name}' ({found.Tools.Count} tools)");
            }
        });
    }

    List<string> ILayoutHost.GetAvailableLayoutNames()
    {
        var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        return (Config.Layouts ?? new List<ContentLayoutState>())
            .Where(x => x.Type == targetType)
            .Select(x => x.Name)
            .ToList();
    }

    string ILayoutHost.GetCurrentLayoutName() => _layoutEditingService.CurrentLayoutName;

    bool ILayoutHost.IsDirty => _layoutEditingService.IsDirty;

    void ILayoutHost.SaveLayoutExplicit()
    {
        _layoutEditingService.Save();
        UpdateWindowTitle();
    }

    void ILayoutHost.DiscardChanges() => _layoutEditingService.DiscardChanges();

    void ILayoutHost.MarkLayoutDirty(List<ToolLayoutState> tools)
        => _layoutEditingService.MarkDirty(tools, _contentContainer?.GridSettings);

    bool ILayoutHost.ShowUnsavedChangesDialog => _layoutEditingService.ShowUnsavedChangesDialog;

    string ILayoutHost.PendingActionDescription => _layoutEditingService.PendingAction?.Description ?? "";

    void ILayoutHost.HandleUnsavedChangesChoice(UnsavedChangesChoice choice)
        => _layoutEditingService.HandleUnsavedChangesChoice(choice);

    void ILayoutHost.SavePreset(string toolType, string presetName, Dictionary<string, object?> settings, string? description)
    {
        var preset = new UserToolPreset
        {
            Name = presetName,
            ToolType = toolType,
            Description = description ?? string.Empty,
            Settings = settings,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        Config.UserToolPresets ??= new List<UserToolPreset>();
        Config.UserToolPresets.Add(preset);
        _configService.MarkDirty();

        _log.Information($"Saved user preset '{presetName}' for tool type '{toolType}'");
    }

    bool ILayoutHost.CanSavePresets => true;

    void ILayoutHost.OpenLayoutsManager() => _windowService?.OpenLayoutsConfig();

    bool ILayoutHost.IsMainWindowInteracting => _stateService.IsMainWindowInteracting;

    // IsFullscreenMode is already a public property — serves as implicit implementation

    void ILayoutHost.NotifyDraggingChanged(bool dragging) => _stateService.IsDragging = dragging;

    void ILayoutHost.NotifyResizingChanged(bool resizing) => _stateService.IsResizing = resizing;

    void ILayoutHost.NotifyGridSettingsChanged(LayoutGridSettings settings)
        => _layoutEditingService.MarkDirty(_contentContainer?.ExportLayout() ?? new List<ToolLayoutState>(), settings);

    ConfigurationService? ILayoutHost.ConfigService => _configService;

    int ILayoutHost.GetExternalToolInternalPadding()
        => _layoutEditingService.WorkingGridSettings?.ToolInternalPaddingPx ?? -1;

    #endregion
    
    /// <summary>
    /// Clears the game's key buffer while in fullscreen mode so the game doesn't
    /// process any keyboard input (movement, system menu, chat, etc.).
    /// ImGui receives input through a separate Win32 message path and is unaffected.
    /// </summary>
    private void OnFrameworkUpdate(IFramework _)
    {
        if (!_isFullscreenMode) return;
        
        // Snapshot ESC state before clearing so Draw() can still detect it for fullscreen exit
        _escPressedThisFrame = _keyState[(int)Dalamud.Game.ClientState.Keys.VirtualKey.ESCAPE];
        
        // Clear the entire game key buffer — prevents the game from acting on any key
        _keyState.ClearAll();
    }
    
    /// <summary>
    /// Updates the window title to reflect the current layout and dirty state.
    /// Uses ### separator to maintain a stable ImGui window ID regardless of title changes,
    /// preventing ImGui from restoring old window positions when the title changes.
    /// </summary>
    private void UpdateWindowTitle()
    {
        var baseTitle = GetDisplayTitle();
        var layoutName = _layoutEditingService.CurrentLayoutName;
        // Use ###KaleidoscopeMain to keep a stable window ID - ImGui uses this ID to persist
        // window position/size. Without it, changing the title (e.g., adding/removing the dirty
        // asterisk) would cause ImGui to restore a previously-saved position for that title.
        const string stableId = "###KaleidoscopeMain";
        if (!string.IsNullOrWhiteSpace(layoutName))
        {
            var suffix = _layoutEditingService.IsDirty ? " *" : "";
#if DEBUG
            WindowName = $"{baseTitle} - Layout: {layoutName}{suffix}{stableId}";
#else
            WindowName = $"{baseTitle} - {layoutName}{suffix}{stableId}";
#endif
        }
        else
        {
            WindowName = $"{baseTitle}{stableId}";
        }
    }

    private Configuration Config => _configService.Config;

    private void InitializeTitleBarButtons()
    {
        // Save layout button (appears only when dirty and in edit mode)
        TitleBarButtons.Add(new TitleBarButton
        {
            Click = m => 
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
            Click = m => { if (m == ImGuiMouseButton.Left) _windowService?.OpenConfigWindow(); },
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 2),
            ShowTooltip = () => ImGui.SetTooltip("Open settings"),
        });

        // Fullscreen toggle button
        _fullscreenButton = new TitleBarButton
        {
            Icon = FontAwesomeIcon.ArrowsUpDownLeftRight,
            IconOffset = new Vector2(2, 2),
            ShowTooltip = () => ImGui.SetTooltip(_isFullscreenMode ? "Exit fullscreen" : "Enter fullscreen"),
        };
        _fullscreenButton.Click = m =>
        {
            if (m != ImGuiMouseButton.Left) return;
            try
            {
                if (_isFullscreenMode)
                    ExitFullscreenMode();
                else
                    EnterFullscreenMode();
            }
            catch (Exception ex) { _log.Error($"Fullscreen toggle failed: {ex.Message}"); }
        };
        TitleBarButtons.Add(_fullscreenButton);

        // Lock button
        _lockButton = new TitleBarButton
        {
            Icon = _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            IconOffset = new Vector2(3, 2),
            ShowTooltip = () => ImGui.SetTooltip("Lock window position and size"),
        };
        _lockButton.Click = m =>
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
                _lockButton!.Icon = _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }
        };
        TitleBarButtons.Add(_lockButton);

        // Edit mode toggle
        _editModeButton = new TitleBarButton
        {
            Icon = FontAwesomeIcon.Edit,
            IconOffset = new Vector2(2, 2),
            ShowTooltip = () => 
            {
                var dirty = _layoutEditingService.IsDirty ? " (unsaved changes)" : "";
                ImGui.SetTooltip($"Toggle HUD edit mode{dirty}");
            },
        };
        _editModeButton.Click = m =>
        {
            if (m == ImGuiMouseButton.Left)
            {
                if (_stateService.IsEditMode)
                {
                    // Turning off edit mode - prompt to save if dirty
                    if (!_layoutEditingService.TryPerformDestructiveAction("exit edit mode", () =>
                    {
                        _stateService.ToggleEditMode();
                    }))
                    {
                        // Dialog will be shown by LayoutEditingService, action deferred
                    }
                }
                else
                {
                    // Turning on edit mode
                    _stateService.ToggleEditMode();
                }
            }
        };
        TitleBarButtons.Add(_editModeButton);
    }

    private void InitializeContentContainer()
    {
        // Create content container
        _contentContainer = new WindowContentContainer(
            () => Config.ContentGridCellWidthPercent,
            () => Config.ContentGridCellHeightPercent,
            () => Config.GridSubdivisions);

        // Wire the ILayoutHost interface instead of individual callbacks
        _contentContainer.Host = this;
        _contentContainer.Factory = _toolFactory;

        WindowToolRegistrar.RegisterTools(_contentContainer, _toolFactory);

        ApplyInitialLayout();

        // If no tools were restored from a layout, add the Getting Started guide
        // Use AddToolInstanceWithoutDirty since this is initial setup, not a user change
        try
        {
            var exported = _contentContainer?.ExportLayout() ?? new List<ToolLayoutState>();
            if (exported.Count == 0)
            {
                var gettingStarted = WindowToolRegistrar.CreateToolFromId("GettingStarted", new Vector2(20, 50), _toolFactory);
                if (gettingStarted != null) _contentContainer?.AddToolInstanceWithoutDirty(gettingStarted);
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"Failed to add default tool after layout apply: {ex.Message}");
        }
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
            
            if (layout.Tools is { Count: > 0 })
                _contentContainer?.ApplyLayout(layout.Tools);
            else
                _contentContainer?.ClearAllTools();
            
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

    private void InitializeQuickAccessBar()
    {
        _quickAccessBar = new QuickAccessBarWidget(
            _stateService,
            _layoutEditingService,
            _configService,
            _currencyTrackerService,
            _webSocketService,
            _autoRetainerIpc,
            _frameLimiterService,
            onFullscreenToggle: () =>
            {
                try
                {
                    if (_isFullscreenMode)
                        ExitFullscreenMode();
                    else
                        EnterFullscreenMode();
                }
                catch (Exception ex) { _log.Error($"Quick access fullscreen toggle failed: {ex.Message}"); }
            },
            onSave: () =>
            {
                if (_layoutEditingService.IsDirty)
                {
                    _layoutEditingService.Save();
                    UpdateWindowTitle();
                }
            },
            onOpenSettings: () => _windowService?.OpenConfigWindow(),
            onExitEditModeWithDirtyCheck: () =>
            {
                if (!_layoutEditingService.TryPerformDestructiveAction("exit edit mode", () =>
                {
                    _stateService.ToggleEditMode();
                }))
                {
                    return true; // Handled - dialog will be shown
                }
                return false; // Action proceeded immediately
            },
            onLayoutChanged: layoutName =>
            {
                ((ILayoutHost)this).LoadLayout(layoutName);
            });
    }

    /// <summary>
    /// Restores window position/size after exiting fullscreen mode.
    /// Validates the restored position is within the current viewport to handle
    /// monitor changes that occurred while in fullscreen.
    /// </summary>
    public void ExitFullscreen()
    {
        var restoredPos = _savedPos;
        var restoredSize = _savedSize;
        
        // Validate the restored position is within the current viewport.
        // Monitors may have been disconnected or resolution may have changed
        // while in fullscreen, leaving the saved position off-screen.
        try
        {
            var viewport = ImGui.GetMainViewport();
            var vpMin = viewport.Pos;
            var vpMax = new Vector2(viewport.Pos.X + viewport.Size.X, viewport.Pos.Y + viewport.Size.Y);
            
            // Clamp size to not exceed viewport
            restoredSize = new Vector2(
                MathF.Min(restoredSize.X, viewport.Size.X),
                MathF.Min(restoredSize.Y, viewport.Size.Y));
            
            // Ensure at least part of the window is visible (top-left corner within viewport)
            if (restoredPos.X + restoredSize.X < vpMin.X + 50 || restoredPos.X > vpMax.X - 50 ||
                restoredPos.Y + restoredSize.Y < vpMin.Y + 50 || restoredPos.Y > vpMax.Y - 50)
            {
                // Window would be mostly off-screen — reset to center
                restoredPos = new Vector2(
                    vpMin.X + (viewport.Size.X - restoredSize.X) * 0.5f,
                    vpMin.Y + (viewport.Size.Y - restoredSize.Y) * 0.5f);
                _log.Debug("ExitFullscreen: saved position was off-screen, centering window");
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"ExitFullscreen: viewport bounds check failed: {ex.Message}");
        }
        
        // Store the validated position/size for the next PreDraw to apply
        // (SetNextWindowPos/Size can't be called here — Draw() runs after the window's Begin)
        _savedPos = restoredPos;
        _savedSize = restoredSize;
        _pendingWindowRestore = true;
        
        // Always persist the restored windowed position/size to config
        Config.MainWindowPos = restoredPos;
        Config.MainWindowSize = restoredSize;
        _lastSavedPos = restoredPos;
        _lastSavedSize = restoredSize;
        _configService.MarkDirty();
    }

    /// <summary>
    /// Enters fullscreen mode - window fills viewport with no decorations.
    /// Loads the active fullscreen layout.
    /// </summary>
    public void EnterFullscreenMode()
    {
        if (_isFullscreenMode) return;
        
        if (!_layoutEditingService.TryPerformDestructiveAction("enter fullscreen mode", () =>
        {
            // Defer to PreDraw — this callback may run inside Draw() (from the
            // unsaved-changes dialog), and EnterFullscreenModeInternal modifies
            // the tool list which would corrupt the in-progress iteration.
            _pendingEnterFullscreen = true;
        }))
        {
            // Dialog will be shown, action deferred
            return;
        }
        // Not dirty — action ran immediately from a safe call site (title bar
        // button or PreDraw), so process the flag right away.
        if (_pendingEnterFullscreen)
        {
            _pendingEnterFullscreen = false;
            EnterFullscreenModeInternal();
        }
    }

    private void EnterFullscreenModeInternal()
    {
        if (_isFullscreenMode) return;
        
        // Save current windowed position/size for restoration later
        try
        {
            _savedPos = ImGui.GetWindowPos();
            _savedSize = ImGui.GetWindowSize();
        }
        catch
        {
            _savedPos = Config.MainWindowPos;
            _savedSize = Config.MainWindowSize;
        }
        
        // Persist windowed pos/size to config so it survives plugin restarts
        Config.MainWindowPos = _savedPos;
        Config.MainWindowSize = _savedSize;
        _lastSavedPos = _savedPos;
        _lastSavedSize = _savedSize;
        
        // Also persist to the active windowed layout so the position is layout-specific
        PersistWindowedPosToActiveLayout(_savedPos, _savedSize);
        
        _configService.MarkDirty();
        
        _isFullscreenMode = true;
        _stateService.IsFullscreen = true;
        
        try
        {
            // Load the fullscreen layout
            LoadLayoutForCurrentMode();
        }
        catch (Exception ex)
        {
            // Revert fullscreen state so the window isn't stuck half-transitioned
            _isFullscreenMode = false;
            _stateService.IsFullscreen = false;
            _log.Error($"Failed to enter fullscreen mode, reverting: {ex.Message}");
            return;
        }
        
        _log.Debug("Entered fullscreen mode");
    }

    /// <summary>
    /// Exits fullscreen mode - restores windowed appearance and position.
    /// Loads the active windowed layout.
    /// </summary>
    public void ExitFullscreenMode()
    {
        if (!_isFullscreenMode) return;
        
        if (!_layoutEditingService.TryPerformDestructiveAction("exit fullscreen mode", () =>
        {
            // Defer to PreDraw — see EnterFullscreenMode for rationale.
            _pendingExitFullscreen = true;
        }))
        {
            // Dialog will be shown, action deferred
            return;
        }
        // Not dirty — safe to process immediately.
        if (_pendingExitFullscreen)
        {
            _pendingExitFullscreen = false;
            ExitFullscreenModeInternal();
        }
    }

    private void ExitFullscreenModeInternal()
    {
        if (!_isFullscreenMode) return;
        
        _isFullscreenMode = false;
        _stateService.IsFullscreen = false;
        
        // Restore windowed position/size
        ExitFullscreen();
        
        try
        {
            // Load the windowed layout
            LoadLayoutForCurrentMode();
        }
        catch (Exception ex)
        {
            // Revert fullscreen state so the window isn't stuck half-transitioned
            _isFullscreenMode = true;
            _stateService.IsFullscreen = true;
            _log.Error($"Failed to exit fullscreen mode, reverting: {ex.Message}");
            return;
        }
        
        _log.Debug("Exited fullscreen mode");
    }

    /// <summary>
    /// Loads the appropriate layout for the current mode (windowed or fullscreen).
    /// </summary>
    private void LoadLayoutForCurrentMode()
    {
        try
        {
            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
            var activeName = _isFullscreenMode 
                ? Config.ActiveFullscreenLayoutName 
                : Config.ActiveWindowedLayoutName;
            
            var filteredLayouts = layouts.Where(x => x.Type == targetType).ToList();
            ContentLayoutState? layout = null;
            
            if (!string.IsNullOrWhiteSpace(activeName))
                layout = filteredLayouts.Find(x => x.Name == activeName);
            layout ??= filteredLayouts.FirstOrDefault();
            
            if (layout != null && _contentContainer != null)
            {
                _contentContainer.SetGridSettingsFromLayout(layout);
                if (layout.Tools is { Count: > 0 })
                    _contentContainer.ApplyLayout(layout.Tools);
                else
                    _contentContainer.ClearAllTools();
                
                // When switching to a windowed layout, restore its saved window position/size
                if (!_isFullscreenMode && layout.WindowedPos.HasValue && layout.WindowedSize.HasValue)
                {
                    _savedPos = layout.WindowedPos.Value;
                    _savedSize = layout.WindowedSize.Value;
                    Config.MainWindowPos = _savedPos;
                    Config.MainWindowSize = _savedSize;
                    _lastSavedPos = _savedPos;
                    _lastSavedSize = _savedSize;
                    
                    // Schedule the position restore for the next PreDraw
                    _pendingWindowRestore = true;
                }
                
                // Update the active layout name if we fell back to a different one
                if (_isFullscreenMode && Config.ActiveFullscreenLayoutName != layout.Name)
                {
                    Config.ActiveFullscreenLayoutName = layout.Name;
                    _configService.MarkDirty();
                }
                else if (!_isFullscreenMode && Config.ActiveWindowedLayoutName != layout.Name)
                {
                    Config.ActiveWindowedLayoutName = layout.Name;
                    _configService.MarkDirty();
                }
                
                _layoutEditingService.InitializeFromPersisted(
                    layout.Name, 
                    targetType, 
                    layout.Tools, 
                    _contentContainer.GridSettings);
                
                _log.Information($"Loaded {targetType} layout '{layout.Name}' ({layout.Tools?.Count ?? 0} tools)");
            }
            else
            {
                // No layout exists for this mode — clear any leftover tools and initialize with defaults
                _contentContainer?.ClearAllTools();
                _layoutEditingService.InitializeFromPersisted(
                    "Default", 
                    targetType, 
                    new List<ToolLayoutState>(), 
                    _contentContainer?.GridSettings);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load layout for current mode: {ex.Message}");
            
            // Fall back to empty defaults so the window remains usable
            try
            {
                _contentContainer?.ClearAllTools();
            }
            catch { /* best effort */ }
            
            var fallbackType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
            _layoutEditingService.InitializeFromPersisted(
                "Default",
                fallbackType,
                new List<ToolLayoutState>(),
                _contentContainer?.GridSettings);
        }
        
        UpdateWindowTitle();
    }

    /// <summary>
    /// Applies a layout by name.
    /// </summary>
    public void ApplyLayoutByName(string name)
    {
        var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        var layout = Config.Layouts?.Find(x => x.Name == name && x.Type == targetType)
                  ?? Config.Layouts?.Where(x => x.Type == targetType).FirstOrDefault();
        if (layout != null && _contentContainer != null)
        {
            _contentContainer.SetGridSettingsFromLayout(layout);
            if (layout.Tools is { Count: > 0 })
                _contentContainer.ApplyLayout(layout.Tools);
            else
                _contentContainer.ClearAllTools();
        }
    }

    public override void PreDraw()
    {
        // Process deferred fullscreen transitions. These are deferred from the
        // unsaved-changes dialog callback which runs inside Draw(); executing
        // them here (before Draw) avoids modifying the tool list mid-iteration.
        if (_pendingEnterFullscreen)
        {
            _pendingEnterFullscreen = false;
            EnterFullscreenModeInternal();
        }
        if (_pendingExitFullscreen)
        {
            _pendingExitFullscreen = false;
            ExitFullscreenModeInternal();
        }

        // Handle fullscreen mode - fill viewport with no decorations
        if (_isFullscreenMode)
        {
            // Disable ESC-to-close so our ESC handler can exit fullscreen without hiding the window
            RespectCloseHotkey = false;
            
            // Fullscreen mode: force fullscreen positioning and disable move/resize/title
            // NoBringToFrontOnFocus is required so popups, context menus, and combo dropdowns render on top
            Flags = ImGuiWindowFlags.NoDecoration 
                  | ImGuiWindowFlags.NoMove 
                  | ImGuiWindowFlags.NoResize 
                  | ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoScrollbar 
                  | ImGuiWindowFlags.NoScrollWithMouse
                  | ImGuiWindowFlags.NoBringToFrontOnFocus;
            
            try
            {
                // Use main viewport for proper fullscreen sizing (accounts for taskbars, multi-monitor, etc.)
                var viewport = ImGui.GetMainViewport();
                ImGui.SetNextWindowPos(viewport.Pos);
                ImGui.SetNextWindowSize(viewport.Size);
            }
            catch (Exception ex)
            {
                _log.Debug($"[MainWindow] Fullscreen viewport setup failed: {ex.Message}");
            }
            
            // Apply fullscreen background color
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Config.FullscreenBackgroundColor);
            return;
        }
        
        // Windowed mode logic below
        
        // Clear ESC suppression once the key is released after a fullscreen exit
        if (_suppressEscClose && !_keyState[(int)Dalamud.Game.ClientState.Keys.VirtualKey.ESCAPE])
            _suppressEscClose = false;
        
        // Re-enable ESC-to-close for windowed mode, but suppress it while ESC is
        // still held from the fullscreen exit to prevent closing the window
        RespectCloseHotkey = !_suppressEscClose;
        
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
        // After exiting fullscreen, restore the saved windowed position/size
        else if (_pendingWindowRestore)
        {
            _pendingWindowRestore = false;
            ImGui.SetNextWindowPos(_savedPos);
            ImGui.SetNextWindowSize(_savedSize);
        }

        // Apply custom background color
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Config.MainWindowBackgroundColor);

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
        Flags &= ~ImGuiWindowFlags.NoCollapse;
        Flags &= ~ImGuiWindowFlags.NoScrollbar;
        Flags &= ~ImGuiWindowFlags.NoScrollWithMouse;
        
        // Prevent the main window from coming in front of the config window when clicked
        Flags |= ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (_lockButton != null)
        {
            _lockButton.Icon = _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
        }

        // If exclusive fullscreen is enabled and we're not already in fullscreen, switch to fullscreen
        if (Config.ExclusiveFullscreen && !_isFullscreenMode)
        {
            EnterFullscreenMode();
        }
    }

    public override void Draw()
    {
        // In fullscreen mode, bring window to the front of the display order so it renders over other plugins.
        // But skip this when:
        // - Any popup is open (context menus, dropdowns, modals)
        // - Another window is focused (ConfigWindow, tool settings, etc.) so they can receive clicks
        // We check if this window (including child windows) is NOT focused, meaning another window has focus.
        var isThisWindowFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (_isFullscreenMode 
            && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)
            && isThisWindowFocused)
        {
            var window = ImGuiP.GetCurrentWindow();
            ImGuiP.BringWindowToDisplayFront(window);
        }

        // ESC key exits fullscreen mode (only when focused and no popups are open)
        // Uses the ESC state snapshot from Framework.Update (the game's key buffer is
        // cleared each frame while in fullscreen, so we can't read it here directly)
        if (_isFullscreenMode 
            && isThisWindowFocused
            && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)
            && _escPressedThisFrame)
        {
            _escPressedThisFrame = false;
            _suppressEscClose = true;
            ExitFullscreenMode();
        }

        // In fullscreen mode, skip window interaction detection since the window can't be moved/resized
        if (!_isFullscreenMode)
        {
            // Detect if main window is being moved or resized by comparing frame-to-frame position/size
            // But only track position changes when the window is not locked and not being constrained
            try
            {
                var curPos = ImGui.GetWindowPos();
                var curSize = ImGui.GetWindowSize();
                var io = ImGui.GetIO();
                const float eps = 0.5f;

                // Only detect movement/resizing when in free mode (not locked, not edit mode)
                // Title bar button clicks should not trigger window movement detection
                var isConstrained = _stateService.IsLocked || _stateService.IsDragging || _stateService.IsResizing;

                if (_prevFrameInitialized && !isConstrained)
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
                else if (!io.MouseDown[0])
                {
                    // Always clear interaction state when mouse is released
                    _stateService.IsMainWindowMoving = false;
                    _stateService.IsMainWindowResizing = false;
                }

                // Always track the current position/size
                _prevFramePos = curPos;
                _prevFrameSize = curSize;
                _prevFrameInitialized = true;
            }
            catch (Exception ex) { _log.Debug($"[MainWindow] Window interaction detection failed: {ex.Message}"); }
        }

        // Main content drawing: render the HUD content container
        try
        {
            // Allow CTRL+SHIFT to temporarily enable edit mode (like fullscreen window)
            var io = ImGui.GetIO();
            var tempEdit = io.KeyCtrl && io.KeyShift;
            
            using (_profilerService.BeginMainWindowScope())
            {
                _contentContainer?.Draw(tempEdit || _stateService.IsEditMode, _profilerService);
            }
            
            // Detect main window position/size changes and persist them promptly (throttled)
            // Only in windowed mode - fullscreen position doesn't need persisting
            if (!_isFullscreenMode)
            {
                PersistWindowPositionIfChanged();
            }
        }
        catch (Exception ex) { LogService.Debug(LogCategory.UI, $"[MainWindow] Draw failed: {ex.Message}"); }
        
        // Draw quick access bar if CTRL+ALT is held (drawn after window content)
        try
        {
            _quickAccessBar?.Draw();
        }
        catch (Exception ex) { _log.Debug($"[MainWindow] Quick access bar draw failed: {ex.Message}"); }
    }

    /// <summary>
    /// Persists the given windowed position/size to the currently active windowed layout.
    /// This ensures the position is remembered per-layout and survives plugin restarts.
    /// </summary>
    private void PersistWindowedPosToActiveLayout(Vector2 pos, Vector2 size)
    {
        try
        {
            var layouts = Config.Layouts;
            if (layouts == null) return;
            
            var activeName = Config.ActiveWindowedLayoutName;
            var layout = !string.IsNullOrWhiteSpace(activeName)
                ? layouts.Find(x => x.Type == LayoutType.Windowed && x.Name == activeName)
                : layouts.Find(x => x.Type == LayoutType.Windowed);
            
            if (layout != null)
            {
                layout.WindowedPos = pos;
                layout.WindowedSize = size;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[MainWindow] Failed to persist windowed pos to layout: {ex.Message}");
        }
    }

    private void PersistWindowPositionIfChanged()
    {
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
                    
                    // Also persist to the active windowed layout for per-layout position memory
                    PersistWindowedPosToActiveLayout(curPos, curSize);
                    
                    _configService.MarkDirty();
                    _lastSavedPos = curPos;
                    _lastSavedSize = curSize;
                    _lastSaveTime = now;
                    _log.Verbose($"Saved main window pos/size: {curPos}, {curSize}");
                }
            }
        }
        catch (Exception ex) { _log.Debug($"[MainWindow] Window pos/size auto-save failed: {ex.Message}"); }
    }

    public override void PostDraw()
    {
        // Pop the background color that was pushed in PreDraw
        ImGui.PopStyleColor();
    }

    private static string GetDisplayTitle()
    {
        var asm = Assembly.GetExecutingAssembly();
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
