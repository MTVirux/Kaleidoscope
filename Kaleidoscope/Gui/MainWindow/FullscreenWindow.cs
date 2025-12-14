namespace Kaleidoscope.Gui.MainWindow
{
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using Dalamud.Plugin.Services;
    using Kaleidoscope.Services;


    public class FullscreenWindow : Window
    {
        private readonly IPluginLog _log;
        private readonly ConfigurationService _configService;
        private readonly FilenameService _filenameService;
        private readonly SamplerService _samplerService;
        private readonly StateService _stateService;
        private readonly GilTrackerComponent _moneyTracker;
        private readonly WindowContentContainer _contentContainer;

        // Reference to WindowService for window coordination (set after construction due to circular dependency)
        private WindowService? _windowService;
        public void SetWindowService(WindowService ws)
        {
            _windowService = ws;
            // Wire OnManageLayouts callback now that we have WindowService
            _contentContainer.OnManageLayouts = () => _windowService?.OpenLayoutsConfig();
        }

        private Configuration Config => _configService.Config;

        public FullscreenWindow(
            IPluginLog log,
            ConfigurationService configService,
            SamplerService samplerService,
            FilenameService filenameService,
            StateService stateService) : base("Kaleidoscope Fullscreen", ImGuiWindowFlags.NoDecoration)
        {
            _log = log;
            _configService = configService;
            _filenameService = filenameService;
            _samplerService = samplerService;
            _stateService = stateService;
            // Use the shared gil tracker from the sampler service
            _moneyTracker = new GilTrackerComponent(filenameService, samplerService);

            // Create a content container similar to the main window so HUD tools
            // can be reused in fullscreen mode. Keep registrations minimal — the
            // gil tracker reuses the shared tracker instance.
            _contentContainer = new WindowContentContainer(() => Config.ContentGridCellWidthPercent, () => Config.ContentGridCellHeightPercent, () => Config.GridSubdivisions);

            try
            {
                // Register the same toolset as the main window. Registrar will
                // construct concrete tool instances; each instance is independent.
                WindowToolRegistrar.RegisterTools(_contentContainer, _filenameService, _samplerService);

                // Add a default independent GilTracker tool instance (connected to same DB file)
                try
                {
                    var defaultGt = WindowToolRegistrar.CreateToolInstance("GilTracker", new System.Numerics.Vector2(20, 50), _filenameService, _samplerService);
                    if (defaultGt != null) _contentContainer.AddTool(defaultGt);
                }
                catch (Exception ex) { LogService.Debug($"[FullscreenWindow] GilTracker creation failed: {ex.Message}"); }
                // Attempt to apply a saved layout if present (use Config.Layouts like main window)
                try
                {
                    var layouts = Config.Layouts ?? new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
                    // Filter to only fullscreen layouts for the fullscreen window
                    var fullscreenLayouts = layouts.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).ToList();
                    var activeName = !string.IsNullOrWhiteSpace(Config.ActiveLayoutName) ? Config.ActiveLayoutName : null;
                    Kaleidoscope.ContentLayoutState? layout = null;
                    if (activeName != null)
                        layout = fullscreenLayouts.Find(x => x.Name == activeName);
                    layout ??= fullscreenLayouts.FirstOrDefault();

                    if (layout != null)
                    {
                        // Apply grid settings first
                        _contentContainer.SetGridSettingsFromLayout(layout);
                        
                        if (layout.Tools != null && layout.Tools.Count > 0)
                        {
                            _contentContainer.ApplyLayout(layout.Tools);
                        }
                    }
                }
                catch (Exception ex) { LogService.Debug($"[FullscreenWindow] Layout apply failed: {ex.Message}"); }

                // Wire layout persistence callbacks for fullscreen window as well
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
                        Config.ActiveLayoutName = name;
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
                            // Apply grid settings first
                            _contentContainer.SetGridSettingsFromLayout(found);
                            // Then apply tool layout
                            _contentContainer.ApplyLayout(found.Tools);
                            Config.ActiveLayoutName = name;
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
                    catch (Exception ex) { LogService.Debug($"[FullscreenWindow] GetAvailableLayoutNames failed: {ex.Message}"); return new System.Collections.Generic.List<string>(); }
                };

                _contentContainer.OnLayoutChanged = (tools) =>
                {
                    try
                    {
                        var activeName = !string.IsNullOrWhiteSpace(Config.ActiveLayoutName)
                            ? Config.ActiveLayoutName
                            : (Config.Layouts?.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).FirstOrDefault()?.Name ?? "Default");
                        var layouts = Config.Layouts ??= new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
                        var existing = layouts.Find(x => x.Name == activeName);
                        if (existing == null)
                        {
                            existing = new Kaleidoscope.ContentLayoutState { Name = activeName, Type = Kaleidoscope.LayoutType.Fullscreen };
                            layouts.Add(existing);
                        }
                        existing.Tools = tools ?? new System.Collections.Generic.List<Kaleidoscope.ToolLayoutState>();
                        Config.ActiveLayoutName = activeName;
                        _configService.Save();
                        _log.Information($"Auto-saved active layout '{activeName}' ({existing.Tools.Count} tools) [fullscreen]");
                    }
                    catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnLayoutChanged failed: {ex.Message}"); }
                };

                // Wire grid settings change callback
                _contentContainer.OnGridSettingsChanged = (gridSettings) =>
                {
                    try
                    {
                        var activeName = !string.IsNullOrWhiteSpace(Config.ActiveLayoutName)
                            ? Config.ActiveLayoutName
                            : (Config.Layouts?.Where(x => x.Type == Kaleidoscope.LayoutType.Fullscreen).FirstOrDefault()?.Name ?? "Default");
                        var layouts = Config.Layouts ??= new System.Collections.Generic.List<Kaleidoscope.ContentLayoutState>();
                        var existing = layouts.Find(x => x.Name == activeName);
                        if (existing == null)
                        {
                            existing = new Kaleidoscope.ContentLayoutState { Name = activeName, Type = Kaleidoscope.LayoutType.Fullscreen };
                            layouts.Add(existing);
                        }
                        gridSettings.ApplyToLayoutState(existing);
                        Config.ActiveLayoutName = activeName;
                        _configService.Save();
                        _log.Debug($"Saved grid settings for layout '{activeName}' [fullscreen]");
                    }
                    catch (Exception ex) { LogService.Debug($"[FullscreenWindow] OnGridSettingsChanged failed: {ex.Message}"); }
                };

                // Wire interaction state callbacks to StateService
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
            catch (Exception ex) { LogService.Error("[FullscreenWindow] Content container initialization failed", ex); }
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
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] PreDraw size setup failed: {ex.Message}"); }
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
                    _contentContainer?.Draw(fsEdit || _stateService.IsEditMode);
                }
                catch (Exception ex)
                {
                    // Fall back to StateService value if IO access fails for any reason
                    LogService.Debug($"[FullscreenWindow] Draw IO check failed: {ex.Message}");
                    _contentContainer?.Draw(_stateService.IsEditMode);
                }
            }
            catch (Exception ex) { LogService.Debug($"[FullscreenWindow] Draw failed: {ex.Message}"); }
        }
    }
}
