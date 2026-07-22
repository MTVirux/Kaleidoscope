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
    private readonly GameStateService _gameState;
    private readonly IKeyState _keyState;
    private readonly IFramework _framework;
    private readonly ToolFactory _toolFactory;
    private readonly UniversalisWebSocketService? _webSocketService;
    private readonly AutoRetainerService? _autoRetainerIpc;
    private readonly WindowPositionPersister _positionPersister;
    private readonly FullscreenController _fullscreenController;
    private WindowContentContainer? _contentContainer;
    private TitleBarButton? _editModeButton;

    private bool _suppressEscClose;
    private bool _escPressedThisFrame;

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
    public bool IsFullscreenMode => _fullscreenController.IsFullscreenMode;

    public MainWindow(
        IPluginLog log,
        ConfigurationService configService,
        CurrencyTrackerService currencyTrackerService,
        StateService stateService,
        LayoutEditingService layoutEditingService,
        ProfilerService profilerService,
        FrameLimiterService frameLimiterService,
        GameStateService gameState,
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
        _gameState = gameState;
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

        _positionPersister = new WindowPositionPersister(_configService, _stateService, _log);
        _fullscreenController = new FullscreenController(
            _configService,
            _layoutEditingService,
            _stateService,
            _log,
            _positionPersister,
            () => _contentContainer,
            UpdateWindowTitle);

        InitializeTitleBarButtons();
        InitializeContentContainer();
        InitializeQuickAccessBar();

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
        var targetType = _fullscreenController.IsFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
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
        var currentType = _fullscreenController.IsFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
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

        var targetType = _fullscreenController.IsFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        var layouts = Config.Layouts ??= new List<ContentLayoutState>();
        var existing = layouts.Find(x => x.Name == name && x.Type == targetType);
        if (existing == null)
        {
            existing = new ContentLayoutState { Name = name, Type = targetType };
            layouts.Add(existing);
        }
        existing.Tools = tools ?? new List<ToolLayoutState>();

        _contentContainer?.GridSettings?.ApplyToLayoutState(existing);

        // Preserve the current auto-layout arrangement from the container
        if (_contentContainer != null)
            existing.Arrangement = _contentContainer.CurrentArrangement;

        if (_fullscreenController.IsFullscreenMode)
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

        var targetType = _fullscreenController.IsFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;

        _layoutEditingService.TrySwitchLayout(name, targetType, () =>
        {
            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            var found = layouts.Find(x => x.Name == name && x.Type == targetType);
            if (found != null)
            {
                _contentContainer?.SetGridSettingsFromLayout(found);
                _contentContainer?.ApplyLayout(found.Tools);

                if (_fullscreenController.IsFullscreenMode)
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
        var targetType = _fullscreenController.IsFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        return (Config.Layouts ?? new List<ContentLayoutState>())
            .Where(x => x.Type == targetType)
            .Select(x => x.Name)
            .ToList();
    }

    string ILayoutHost.GetCurrentLayoutName() => _layoutEditingService.CurrentLayoutName;

    bool ILayoutHost.IsDirty => _layoutEditingService.IsDirty;

    void ILayoutHost.SaveLayoutExplicit()
    {
        SyncArrangementToEditingService();
        _layoutEditingService.Save();
        UpdateWindowTitle();
    }

    void ILayoutHost.DiscardChanges() => _layoutEditingService.DiscardChanges();

    void ILayoutHost.MarkLayoutDirty(List<ToolLayoutState> tools)
    {
        SyncArrangementToEditingService();
        _layoutEditingService.MarkDirty(tools, _contentContainer?.GridSettings);
    }

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

    /// <summary>
    /// Copies the container's current arrangement to the editing service so it is persisted on save.
    /// </summary>
    private void SyncArrangementToEditingService()
    {
        if (_contentContainer != null)
            _layoutEditingService.WorkingArrangement = _contentContainer.CurrentArrangement;
    }

    #endregion

    /// <summary>
    /// Clears the game's key buffer while in fullscreen mode so the game doesn't
    /// process any keyboard input (movement, system menu, chat, etc.).
    /// ImGui receives input through a separate Win32 message path and is unaffected.
    /// </summary>
    private void OnFrameworkUpdate(IFramework _)
    {
        if (!_fullscreenController.IsFullscreenMode) return;

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
            ShowTooltip = () => ImGui.SetTooltip(_fullscreenController.IsFullscreenMode ? "Exit fullscreen" : "Enter fullscreen"),
        };
        _fullscreenButton.Click = m =>
        {
            if (m != ImGuiMouseButton.Left) return;
            try
            {
                if (_fullscreenController.IsFullscreenMode)
                    _fullscreenController.ExitFullscreenMode();
                else
                    _fullscreenController.EnterFullscreenMode();
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
                    // About to lock - save current position/size.
                    // A collapsed window reports title-bar-only height, so keep the
                    // last-known expanded size to avoid restoring to a collapsed size.
                    Config.MainWindowPos = ImGui.GetWindowPos();
                    if (!ImGui.IsWindowCollapsed())
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
            _gameState,
            _configService,
            _currencyTrackerService,
            _webSocketService,
            _autoRetainerIpc,
            _frameLimiterService,
            onFullscreenToggle: () =>
            {
                try
                {
                    if (_fullscreenController.IsFullscreenMode)
                        _fullscreenController.ExitFullscreenMode();
                    else
                        _fullscreenController.EnterFullscreenMode();
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
    /// Enters fullscreen mode - window fills viewport with no decorations.
    /// Loads the active fullscreen layout.
    /// </summary>
    public void EnterFullscreenMode() => _fullscreenController.EnterFullscreenMode();

    /// <summary>
    /// Exits fullscreen mode - restores windowed appearance and position.
    /// Loads the active windowed layout.
    /// </summary>
    public void ExitFullscreenMode() => _fullscreenController.ExitFullscreenMode();

    /// <summary>
    /// Applies a layout by name.
    /// </summary>
    public void ApplyLayoutByName(string name) => _fullscreenController.ApplyLayoutByName(name);

    public override void PreDraw()
    {
        // Process deferred fullscreen transitions. These are deferred from the
        // unsaved-changes dialog callback which runs inside Draw(); executing
        // them here (before Draw) avoids modifying the tool list mid-iteration.
        _fullscreenController.ProcessPendingTransitions();

        // Handle fullscreen mode - fill viewport with no decorations
        if (_fullscreenController.IsFullscreenMode)
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

        // Apply the first-frame and post-fullscreen position restores
        _positionPersister.ApplyPendingPositioning();

        // Apply custom background color
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Config.MainWindowBackgroundColor);

        // Apply move/resize constraint flags and forced positioning based on lock/drag/resize state
        _positionPersister.ApplyConstraintPositioning(this);

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
        if (Config.ExclusiveFullscreen && !_fullscreenController.IsFullscreenMode)
        {
            _fullscreenController.EnterFullscreenMode();
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
        if (_fullscreenController.IsFullscreenMode
            && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)
            && isThisWindowFocused)
        {
            var window = ImGuiP.GetCurrentWindow();
            ImGuiP.BringWindowToDisplayFront(window);
        }

        // ESC key exits fullscreen mode (only when focused and no popups are open)
        // Uses the ESC state snapshot from Framework.Update (the game's key buffer is
        // cleared each frame while in fullscreen, so we can't read it here directly)
        if (_fullscreenController.IsFullscreenMode
            && isThisWindowFocused
            && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)
            && _escPressedThisFrame)
        {
            _escPressedThisFrame = false;
            _suppressEscClose = true;
            _fullscreenController.ExitFullscreenMode();
        }

        // In fullscreen mode, skip window interaction detection since the window can't be moved/resized
        if (!_fullscreenController.IsFullscreenMode)
        {
            _positionPersister.TrackWindowInteraction();
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
            if (!_fullscreenController.IsFullscreenMode)
            {
                _positionPersister.PersistWindowPositionIfChanged();
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
