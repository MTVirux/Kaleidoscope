using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using OtterGui.Text;
using Dalamud.Interface;
using Kaleidoscope.Services;
using Kaleidoscope.Models;
using Kaleidoscope.Gui.Widgets;
using OtterGui.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Main plugin window containing the HUD layout.
/// </summary>
/// <remarks>
/// This window follows the Glamourer pattern for complex plugin windows with
/// title bar buttons, content containers, and state management.
/// </remarks>
public sealed class MainWindow : Window, IService, IDisposable
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly FilenameService _filenameService;
    private readonly StateService _stateService;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly TrackedDataRegistry _trackedDataRegistry;
    private readonly InventoryChangeService _inventoryChangeService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly ItemDataService _itemDataService;
    private readonly IDataManager _dataManager;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly ProfilerService _profilerService;
    private readonly AutoRetainerIpcService _autoRetainerIpc;
    private readonly ITextureProvider _textureProvider;
    private readonly FavoritesService _favoritesService;
    private readonly CharacterDataService _characterDataService;
    private readonly SalePriceCacheService _salePriceCacheService;
    private readonly FrameLimiterService _frameLimiterService;
    private WindowContentContainer? _contentContainer;
    private TitleBarButton? _editModeButton;
    
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
    private bool _prevFrameInitialized;

    // Flag to track if this is the first PreDraw call (used for initial positioning)
    private bool _firstPreDraw = true;

    // Fullscreen mode state - when true, window fills viewport with no decorations
    private bool _isFullscreenMode;

    // Reference to WindowService for window coordination (set after construction due to circular dependency)
    private WindowService? _windowService;
    
    // Title bar buttons
    private TitleBarButton? _lockButton;
    private TitleBarButton? _fullscreenButton;
    
    // Quick access bar widget (appears when CTRL+ALT is held)
    private QuickAccessBarWidget? _quickAccessBar;

    /// <summary>
    /// Sets the WindowService reference. Required due to circular dependency.
    /// </summary>
    public void SetWindowService(WindowService ws) => _windowService = ws;

    /// <summary>
    /// Gets whether the window is currently in fullscreen mode.
    /// </summary>
    public bool IsFullscreenMode => _isFullscreenMode;

    /// <summary>
    /// Gets whether the database is available.
    /// </summary>
    public bool HasDb => _currencyTrackerService.HasDb;

    public void ClearAllData()
    {
        try { _currencyTrackerService.ClearAllData(); }
        catch (Exception ex) { _log.Error($"ClearAllData failed: {ex.Message}"); }
    }

    public int CleanUnassociatedCharacters()
    {
        try { return _currencyTrackerService.CleanUnassociatedCharacters(); }
        catch (Exception ex) { _log.Error($"CleanUnassociatedCharacters failed: {ex.Message}"); return 0; }
    }

    public string? ExportCsv()
    {
        try { return _currencyTrackerService.ExportCsv(TrackedDataType.Gil); }
        catch (Exception ex) { _log.Error($"ExportCsv failed: {ex.Message}"); return null; }
    }

    public MainWindow(
        IPluginLog log,
        ConfigurationService configService,
        CurrencyTrackerService currencyTrackerService,
        FilenameService filenameService,
        StateService stateService,
        LayoutEditingService layoutEditingService,
        TrackedDataRegistry trackedDataRegistry,
        InventoryChangeService inventoryChangeService,
        UniversalisWebSocketService webSocketService,
        PriceTrackingService priceTrackingService,
        ItemDataService itemDataService,
        IDataManager dataManager,
        InventoryCacheService inventoryCacheService,
        ProfilerService profilerService,
        AutoRetainerIpcService autoRetainerIpc,
        ITextureProvider textureProvider,
        FavoritesService favoritesService,
        CharacterDataService characterDataService,
        SalePriceCacheService salePriceCacheService,
        FrameLimiterService frameLimiterService) 
        : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _log = log;
        _configService = configService;
        _currencyTrackerService = currencyTrackerService;
        _filenameService = filenameService;
        _stateService = stateService;
        _layoutEditingService = layoutEditingService;
        _trackedDataRegistry = trackedDataRegistry;
        _inventoryChangeService = inventoryChangeService;
        _webSocketService = webSocketService;
        _priceTrackingService = priceTrackingService;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _inventoryCacheService = inventoryCacheService;
        _profilerService = profilerService;
        _autoRetainerIpc = autoRetainerIpc;
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        _characterDataService = characterDataService;
        _salePriceCacheService = salePriceCacheService;
        _frameLimiterService = frameLimiterService;

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
        
        // Use the same logic as OnLoadLayout callback
        _contentContainer?.OnLoadLayout?.Invoke(layoutName);
    }

    public void Dispose()
    {
        _layoutEditingService.OnDirtyStateChanged -= OnDirtyStateChanged;
        _layoutEditingService.OnLayoutReverted -= OnLayoutReverted;
        _configService.OnActiveLayoutChanged -= OnActiveLayoutChangedFromConfig;
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

        // Wire up external padding source for real-time config window updates
        _contentContainer.GetExternalToolInternalPadding = () =>
        {
            return _layoutEditingService.WorkingGridSettings?.ToolInternalPaddingPx ?? -1;
        };

        WindowToolRegistrar.RegisterTools(_contentContainer, _filenameService, _currencyTrackerService, _configService, _characterDataService, _inventoryChangeService, _trackedDataRegistry, _webSocketService, _priceTrackingService, _itemDataService, _dataManager, _inventoryCacheService, _autoRetainerIpc, _textureProvider, _favoritesService, _salePriceCacheService);

        ApplyInitialLayout();

        // If no tools were restored from a layout, add the Getting Started guide
        // Use AddToolInstanceWithoutDirty since this is initial setup, not a user change
        try
        {
            var exported = _contentContainer?.ExportLayout() ?? new List<ToolLayoutState>();
            if (exported.Count == 0)
            {
                var ctx = new ToolCreationContext(
                    _filenameService, _currencyTrackerService, _configService, _characterDataService,
                    _inventoryChangeService, _trackedDataRegistry, _webSocketService,
                    _priceTrackingService, _itemDataService, _dataManager,
                    _inventoryCacheService, _autoRetainerIpc, _textureProvider, _favoritesService, _salePriceCacheService);
                var gettingStarted = WindowToolRegistrar.CreateToolFromId("GettingStarted", new Vector2(20, 50), ctx);
                if (gettingStarted != null) _contentContainer?.AddToolInstanceWithoutDirty(gettingStarted);
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"Failed to add default tool after layout apply: {ex.Message}");
        }

        WireLayoutCallbacks();
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
            
            if (layout.Tools is { Count: > 0 })
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
            
            // Determine the layout type based on current mode
            var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
            
            var layouts = Config.Layouts ??= new List<ContentLayoutState>();
            var existing = layouts.Find(x => x.Name == name && x.Type == targetType);
            if (existing == null)
            {
                existing = new ContentLayoutState { Name = name, Type = targetType };
                layouts.Add(existing);
            }
            existing.Tools = tools ?? new List<ToolLayoutState>();
            
            // Update the appropriate active layout name
            if (_isFullscreenMode)
                Config.ActiveFullscreenLayoutName = name;
            else
                Config.ActiveWindowedLayoutName = name;
                
            _configService.Save();
            _configService.SaveLayouts();
            
            // After saving as new name, initialize the editing service for this layout
            _layoutEditingService.InitializeFromPersisted(name, targetType, tools, _contentContainer.GridSettings);
            UpdateWindowTitle();
            
            _log.Information($"Saved {targetType} layout '{name}' ({existing.Tools.Count} tools)");
        };

        _contentContainer.OnLoadLayout = name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            
            // Determine the layout type based on current mode
            var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
            
            // Use LayoutEditingService to handle dirty check and layout switch
            // TrySwitchLayout returns true if action proceeds immediately (not dirty),
            // or false if dirty (in which case dialog will be shown and action deferred)
            _layoutEditingService.TrySwitchLayout(name, targetType, () =>
            {
                var layouts = Config.Layouts ?? new List<ContentLayoutState>();
                var found = layouts.Find(x => x.Name == name && x.Type == targetType);
                if (found != null)
                {
                    // Apply grid settings first
                    _contentContainer.SetGridSettingsFromLayout(found);
                    // Then apply tool layout
                    _contentContainer.ApplyLayout(found.Tools);
                    
                    // Update the appropriate active layout name
                    if (_isFullscreenMode)
                        Config.ActiveFullscreenLayoutName = name;
                    else
                        Config.ActiveWindowedLayoutName = name;
                        
                    _configService.Save();
                    
                    // Initialize the editing service for the new layout
                    _layoutEditingService.InitializeFromPersisted(name, targetType, found.Tools, _contentContainer.GridSettings);
                    UpdateWindowTitle();
                    
                    _log.Information($"Loaded {targetType} layout '{name}' ({found.Tools.Count} tools)");
                }
            });
        };

        _contentContainer.GetAvailableLayoutNames = () =>
        {
            // Filter layouts by current mode (windowed or fullscreen)
            var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
            return (Config.Layouts ?? new List<ContentLayoutState>())
                .Where(x => x.Type == targetType)
                .Select(x => x.Name)
                .ToList();
        };

        _contentContainer.OnManageLayouts = () =>
        {
            _windowService?.OpenLayoutsConfig();
        };

        // Wire layout change callback - marks dirty instead of auto-saving
        _contentContainer.OnLayoutChanged = tools =>
        {
            _layoutEditingService.MarkDirty(tools, _contentContainer.GridSettings);
        };

        // Wire grid settings change callback - marks dirty instead of auto-saving
        _contentContainer.OnGridSettingsChanged = gridSettings =>
        {
            _layoutEditingService.MarkDirty(_contentContainer.ExportLayout(), gridSettings);
        };
        
        // Wire explicit save callback
        _contentContainer.OnSaveLayoutExplicit = () =>
        {
            _layoutEditingService.Save();
            UpdateWindowTitle();
        };
        
        // Wire discard changes callback (for explicit Discard Changes menu item)
        _contentContainer.OnDiscardChanges = () =>
        {
            _layoutEditingService.DiscardChanges();
            // OnLayoutReverted event will trigger UI refresh
        };
        
        // Wire dirty state query
        _contentContainer.GetIsDirty = () => _layoutEditingService.IsDirty;
        
        // Wire current layout name query
        _contentContainer.GetCurrentLayoutName = () => _layoutEditingService.CurrentLayoutName;
        
        // Wire unsaved changes dialog callbacks (state is managed by LayoutEditingService)
        _contentContainer.GetShowUnsavedChangesDialog = () => _layoutEditingService.ShowUnsavedChangesDialog;
        _contentContainer.GetPendingActionDescription = () => _layoutEditingService.PendingAction?.Description ?? "";
        _contentContainer.HandleUnsavedChangesChoice = choice => _layoutEditingService.HandleUnsavedChangesChoice(choice);
        
        // Wire save preset callback
        _contentContainer.OnSavePreset = (toolType, presetName, settings) =>
        {
            var preset = new UserToolPreset
            {
                Name = presetName,
                ToolType = toolType,
                Settings = settings,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };
            
            Config.UserToolPresets ??= new List<UserToolPreset>();
            Config.UserToolPresets.Add(preset);
            _configService.MarkDirty();
            
            _log.Information($"Saved user preset '{presetName}' for tool type '{toolType}'");
        };
    }

    private void WireInteractionCallbacks()
    {
        if (_contentContainer == null) return;

        // Wire drag/resize state changes from content container to StateService
        _contentContainer.OnDraggingChanged = dragging =>
        {
            _stateService.IsDragging = dragging;
        };

        _contentContainer.OnResizingChanged = resizing =>
        {
            _stateService.IsResizing = resizing;
        };

        // Wire main window interaction state so container can block tool interactions
        _contentContainer.IsMainWindowInteracting = () => _stateService.IsMainWindowInteracting;
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
                _contentContainer?.OnLoadLayout?.Invoke(layoutName);
            });
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

    /// <summary>
    /// Restores window position/size after exiting fullscreen mode.
    /// </summary>
    public void ExitFullscreen()
    {
        ImGui.SetNextWindowPos(_savedPos);
        ImGui.SetNextWindowSize(_savedSize);
        
        if (_stateService.IsLocked)
        {
            Config.MainWindowPos = _savedPos;
            Config.MainWindowSize = _savedSize;
            _configService.MarkDirty();
        }
    }

    /// <summary>
    /// Enters fullscreen mode - window fills viewport with no decorations.
    /// Loads the active fullscreen layout.
    /// </summary>
    public void EnterFullscreenMode()
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
        
        _isFullscreenMode = true;
        _stateService.IsFullscreen = true;
        
        // Load the fullscreen layout
        LoadLayoutForCurrentMode();
        
        _log.Debug("Entered fullscreen mode");
    }

    /// <summary>
    /// Exits fullscreen mode - restores windowed appearance and position.
    /// Loads the active windowed layout.
    /// </summary>
    public void ExitFullscreenMode()
    {
        if (!_isFullscreenMode) return;
        
        _isFullscreenMode = false;
        _stateService.IsFullscreen = false;
        
        // Restore windowed position/size
        ExitFullscreen();
        
        // Load the windowed layout
        LoadLayoutForCurrentMode();
        
        _log.Debug("Exited fullscreen mode");
    }

    /// <summary>
    /// Loads the appropriate layout for the current mode (windowed or fullscreen).
    /// </summary>
    private void LoadLayoutForCurrentMode()
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
            
            _log.Information($"Loaded {targetType} layout '{layout.Name}' ({layout.Tools.Count} tools)");
        }
        else
        {
            // No layout exists for this mode, initialize with defaults
            _layoutEditingService.InitializeFromPersisted(
                "Default", 
                targetType, 
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
        var layout = Config.Layouts?.Find(x => x.Name == name) ?? Config.Layouts?.FirstOrDefault();
        if (layout != null && _contentContainer != null)
        {
            _contentContainer.ApplyLayout(layout.Tools);
        }
    }

    public override void PreDraw()
    {
        // Handle fullscreen mode - fill viewport with no decorations
        if (_isFullscreenMode)
        {
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
