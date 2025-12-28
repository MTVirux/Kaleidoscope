using Dalamud.Interface.Windowing;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Fullscreen overlay window for HUD display.
/// </summary>
/// <remarks>
/// Provides a fullscreen overlay mode where tools can be displayed without
/// window decorations. Supports CTRL+SHIFT to toggle edit mode.
/// </remarks>
public sealed class FullscreenWindow : Window
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly FilenameService _filenameService;
    private readonly SamplerService _samplerService;
    private readonly StateService _stateService;
    private readonly TrackedDataRegistry _trackedDataRegistry;
    private readonly InventoryChangeService _inventoryChangeService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly ItemDataService _itemDataService;
    private readonly IDataManager _dataManager;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly ProfilerService _profilerService;
    private readonly AutoRetainerIpcService _autoRetainerIpc;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly ITextureProvider _textureProvider;
    private readonly FavoritesService _favoritesService;
    private readonly CharacterDataService _characterDataService;
    private readonly WindowContentContainer _contentContainer;
    
    // Quick access bar widget (appears when CTRL+ALT is held)
    private QuickAccessBarWidget? _quickAccessBar;

    // Reference to WindowService for window coordination (set after construction due to circular dependency)
    private WindowService? _windowService;

    private Configuration Config => _configService.Config;

    public void SetWindowService(WindowService ws)
    {
        _windowService = ws;
        // Wire OnManageLayouts callback now that we have WindowService
        _contentContainer.OnManageLayouts = () => _windowService?.OpenLayoutsConfig();
    }

    public FullscreenWindow(
        IPluginLog log,
        ConfigurationService configService,
        SamplerService samplerService,
        FilenameService filenameService,
        StateService stateService,
        TrackedDataRegistry trackedDataRegistry,
        InventoryChangeService inventoryChangeService,
        UniversalisWebSocketService webSocketService,
        PriceTrackingService priceTrackingService,
        ItemDataService itemDataService,
        IDataManager dataManager,
        InventoryCacheService inventoryCacheService,
        ProfilerService profilerService,
        AutoRetainerIpcService autoRetainerIpc,
        LayoutEditingService layoutEditingService,
        ITextureProvider textureProvider,
        FavoritesService favoritesService,
        CharacterDataService characterDataService) : base("Kaleidoscope Fullscreen", ImGuiWindowFlags.NoDecoration)
    {
        _log = log;
        _configService = configService;
        _filenameService = filenameService;
        _samplerService = samplerService;
        _stateService = stateService;
        _trackedDataRegistry = trackedDataRegistry;
        _inventoryChangeService = inventoryChangeService;
        _webSocketService = webSocketService;
        _priceTrackingService = priceTrackingService;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _inventoryCacheService = inventoryCacheService;
        _profilerService = profilerService;
        _autoRetainerIpc = autoRetainerIpc;
        _layoutEditingService = layoutEditingService;
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        _characterDataService = characterDataService;

        // Create a content container similar to the main window so HUD tools
        // can be reused in fullscreen mode. Keep registrations minimal — the
        // gil tracker reuses the shared tracker instance.
        _contentContainer = new WindowContentContainer(
            () => Config.ContentGridCellWidthPercent,
            () => Config.ContentGridCellHeightPercent,
            () => Config.GridSubdivisions);

        InitializeContentContainer();
    }

    private void InitializeContentContainer()
    {
        try
        {
            // Register the same toolset as the main window. Registrar will
            // construct concrete tool instances; each instance is independent.
            WindowToolRegistrar.RegisterTools(_contentContainer, _filenameService, _samplerService, _configService, _characterDataService, _inventoryChangeService, _trackedDataRegistry, _webSocketService, _priceTrackingService, _itemDataService, _dataManager, _inventoryCacheService, _autoRetainerIpc, _textureProvider, _favoritesService);

            AddDefaultTools();
            ApplyInitialLayout();
            WireLayoutCallbacks();
            WireInteractionCallbacks();
            InitializeQuickAccessBar();
        }
        catch (Exception ex)
        {
            LogService.Error("[FullscreenWindow] Content container initialization failed", ex);
        }
    }

    private void AddDefaultTools()
    {
        try
        {
            var ctx = new ToolCreationContext(
                _filenameService, _samplerService, _configService, _characterDataService,
                _inventoryChangeService, _trackedDataRegistry, _webSocketService,
                _priceTrackingService, _itemDataService, _dataManager,
                _inventoryCacheService, _autoRetainerIpc, _textureProvider, _favoritesService);
            
            var gettingStarted = WindowToolRegistrar.CreateToolFromId(
                "GettingStarted",
                new System.Numerics.Vector2(20, 50),
                ctx);
            if (gettingStarted != null)
                _contentContainer.AddToolInstance(gettingStarted);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[FullscreenWindow] GettingStarted creation failed: {ex.Message}");
        }
    }

    private void ApplyInitialLayout()
    {
        try
        {
            var layouts = Config.Layouts ?? new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
            var fullscreenLayouts = layouts.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).ToList();
            var activeName = !string.IsNullOrWhiteSpace(Config.ActiveFullscreenLayoutName)
                ? Config.ActiveFullscreenLayoutName
                : null;

            Kaleidoscope.ContentLayoutState? layout = null;
            if (activeName != null)
                layout = fullscreenLayouts.Find(x => x.Name == activeName);
            layout ??= fullscreenLayouts.FirstOrDefault();

            if (layout != null)
            {
                _contentContainer.SetGridSettingsFromLayout(layout);

                if (layout.Tools != null && layout.Tools.Count > 0)
                    _contentContainer.ApplyLayout(layout.Tools);

                if (string.IsNullOrWhiteSpace(Config.ActiveFullscreenLayoutName))
                    Config.ActiveFullscreenLayoutName = layout.Name;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[FullscreenWindow] Layout apply failed: {ex.Message}");
        }
    }

    private void WireLayoutCallbacks()
    {
        _contentContainer.OnSaveLayout = (name, tools) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                var layouts = Config.Layouts ??= new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
                var existing = layouts.Find(x => x.Name == name);
                if (existing == null)
                {
                    existing = new Kaleidoscope.ContentLayoutState { Name = name, Type = Kaleidoscope.LayoutType.Fullscreen };
                    layouts.Add(existing);
                }
                existing.Tools = tools ?? new System.Collections.Generic.List<Kaleidoscope.ToolLayoutState>();
                Config.ActiveFullscreenLayoutName = name;
                _configService.Save();
                _log.Information($"Saved layout '{name}' ({existing.Tools.Count} tools) [fullscreen]");
            }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnSaveLayout failed: {ex.Message}"); }
        };

        _contentContainer.OnLoadLayout = (name) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                var layouts = Config.Layouts ?? new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
                var found = layouts.Find(x => x.Name == name && x.Type == Kaleidoscope.LayoutType.Fullscreen);
                if (found != null)
                {
                    _contentContainer.SetGridSettingsFromLayout(found);
                    _contentContainer.ApplyLayout(found.Tools);
                    Config.ActiveFullscreenLayoutName = name;
                    _configService.Save();
                    _log.Information($"Loaded layout '{name}' ({found.Tools.Count} tools) [fullscreen]");
                }
            }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnLoadLayout failed: {ex.Message}"); }
        };

        _contentContainer.GetAvailableLayoutNames = () =>
        {
            try
            {
                return (Config.Layouts ?? new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>())
                    .Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen)
                    .Select(x => x.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                LogService.Debug($"[FullscreenWindow] GetAvailableLayoutNames failed: {ex.Message}");
                return new System.Collections.Generic.List<string>();
            }
        };

        _contentContainer.OnLayoutChanged = (tools) =>
        {
            try
            {
                var activeName = !string.IsNullOrWhiteSpace(Config.ActiveFullscreenLayoutName)
                    ? Config.ActiveFullscreenLayoutName
                    : (Config.Layouts?.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).FirstOrDefault()?.Name ?? "Default");
                var layouts = Config.Layouts ??= new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
                var existing = layouts.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).FirstOrDefault(x => x.Name == activeName);
                if (existing == null)
                {
                    existing = new Kaleidoscope.ContentLayoutState { Name = activeName, Type = Kaleidoscope.LayoutType.Fullscreen };
                    layouts.Add(existing);
                }
                existing.Tools = tools ?? new System.Collections.Generic.List<Kaleidoscope.ToolLayoutState>();
                Config.ActiveFullscreenLayoutName = activeName;
                _configService.Save();
                _log.Information($"Auto-saved active layout '{activeName}' ({existing.Tools.Count} tools) [fullscreen]");
            }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnLayoutChanged failed: {ex.Message}"); }
        };

        _contentContainer.OnGridSettingsChanged = (gridSettings) =>
        {
            try
            {
                var activeName = !string.IsNullOrWhiteSpace(Config.ActiveFullscreenLayoutName)
                    ? Config.ActiveFullscreenLayoutName
                    : (Config.Layouts?.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).FirstOrDefault()?.Name ?? "Default");
                var layouts = Config.Layouts ??= new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
                var existing = layouts.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).FirstOrDefault(x => x.Name == activeName);
                if (existing == null)
                {
                    existing = new Kaleidoscope.ContentLayoutState { Name = activeName, Type = Kaleidoscope.LayoutType.Fullscreen };
                    layouts.Add(existing);
                }
                gridSettings.ApplyToLayoutState(existing);
                Config.ActiveFullscreenLayoutName = activeName;
                _configService.Save();
                _log.Debug($"Saved grid settings for layout '{activeName}' [fullscreen]");
            }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnGridSettingsChanged failed: {ex.Message}"); }
        };
        
        // Wire save preset callback
        _contentContainer.OnSavePreset = (toolType, presetName, settings) =>
        {
            try
            {
                var preset = new UserToolPreset
                {
                    Name = presetName,
                    ToolType = toolType,
                    Settings = settings,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                };
                
                Config.UserToolPresets ??= new System.Collections.Generic.List<UserToolPreset>();
                Config.UserToolPresets.Add(preset);
                _configService.Save();
                
                _log.Information($"Saved user preset '{presetName}' for tool type '{toolType}'");
            }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnSavePreset failed: {ex.Message}"); }
        };
    }

    private void WireInteractionCallbacks()
    {
        _contentContainer.OnDraggingChanged = (dragging) =>
        {
            try { _stateService.IsDragging = dragging; }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnDraggingChanged failed: {ex.Message}"); }
        };

        _contentContainer.OnResizingChanged = (resizing) =>
        {
            try { _stateService.IsResizing = resizing; }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnResizingChanged failed: {ex.Message}"); }
        };
    }

    private void InitializeQuickAccessBar()
    {
        _quickAccessBar = new QuickAccessBarWidget(
            _stateService,
            _layoutEditingService,
            _configService,
            _samplerService,
            _webSocketService,
            _autoRetainerIpc,
            onFullscreenToggle: () =>
            {
                try
                {
                    _windowService?.RequestExitFullscreen();
                }
                catch (Exception ex) { LogService.Debug($"[FullscreenWindow] Quick access exit fullscreen failed: {ex.Message}"); }
            },
            onSave: () =>
            {
                if (_layoutEditingService.IsDirty)
                {
                    _layoutEditingService.Save();
                }
            },
            onOpenSettings: () => _windowService?.OpenConfigWindow(),
            onExitEditModeWithDirtyCheck: () =>
            {
                // Fullscreen doesn't have the same dialog infrastructure, just toggle
                return false;
            },
            onLayoutChanged: layoutName =>
            {
                _contentContainer.OnLoadLayout?.Invoke(layoutName);
            });
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
            
            // Apply custom background color
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Config.FullscreenBackgroundColor);
        }
        catch (Exception ex) { LogService.Debug($"[FullscreenWindow] PreDraw size setup failed: {ex.Message}"); }
    }

    public override void PostDraw()
    {
        // Pop the background color that was pushed in PreDraw
        ImGui.PopStyleColor();
    }

    public override void Draw()
    {
        try
        {
            // Draw the content container occupying the fullscreen window.
            // In fullscreen, default to non-edit mode. Only enable edit mode
            // while the user is actively holding CTRL+SHIFT.
            try
            {
                var io = ImGui.GetIO();
                var fsEdit = io.KeyCtrl && io.KeyShift;
                using (_profilerService.BeginFullscreenWindowScope())
                {
                    _contentContainer?.Draw(fsEdit || _stateService.IsEditMode, _profilerService);
                }
            }
            catch (Exception ex)
            {
                // Fall back to StateService value if IO access fails for any reason
                LogService.Debug($"[FullscreenWindow] Draw IO check failed: {ex.Message}");
                _contentContainer?.Draw(_stateService.IsEditMode, _profilerService);
            }
        }
        catch (Exception ex) { LogService.Debug($"[FullscreenWindow] Draw failed: {ex.Message}"); }
        
        // Draw quick access bar if CTRL+ALT is held (drawn after window content)
        try
        {
            _quickAccessBar?.Draw();
        }
        catch (Exception ex) { LogService.Debug($"[FullscreenWindow] Quick access bar draw failed: {ex.Message}"); }
    }
}
