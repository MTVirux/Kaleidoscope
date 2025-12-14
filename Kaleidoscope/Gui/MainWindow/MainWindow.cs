namespace Kaleidoscope.Gui.MainWindow
{
    
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using Dalamud.Plugin.Services;
    
    using OtterGui.Text;
    using System.Linq;
    using Dalamud.Interface;
    using Kaleidoscope.Services;

    public class MainWindow : Window
    {
        private readonly IPluginLog _log;
        private readonly ConfigurationService _configService;
        private readonly SamplerService _samplerService;
        private readonly FilenameService _filenameService;
        private readonly GilTrackerComponent _moneyTracker;
        private WindowContentContainer? _contentContainer;
        private TitleBarButton? editModeButton;
        // Saved (non-fullscreen) position/size so we can restore after exiting fullscreen
        private Vector2 _savedPos = ConfigStatic.DefaultWindowPosition;
        private Vector2 _savedSize = ConfigStatic.DefaultWindowSize;

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
            GilTrackerComponent gilTrackerComponent) 
            : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _log = log;
            _configService = configService;
            _samplerService = samplerService;
            _filenameService = filenameService;
            _moneyTracker = gilTrackerComponent;

            SizeConstraints = new WindowSizeConstraints() { MinimumSize = ConfigStatic.MinimumWindowSize };

            InitializeTitleBarButtons();
            InitializeContentContainer();

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
                    _windowService?.RequestShowFullscreen();
                }
                catch (Exception ex) { _log.Error($"Fullscreen toggle failed: {ex.Message}"); }
            };
            TitleBarButtons.Add(fullscreenButton);

            // Lock button
            lockButton = new TitleBarButton
            {
                Icon = Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                IconOffset = new Vector2(3, 2),
                ShowTooltip = () => ImGui.SetTooltip("Lock window position and size"),
            };
            lockButton.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    var newPinned = !Config.PinMainWindow;
                    Config.PinMainWindow = newPinned;
                    if (newPinned)
                    {
                        Config.MainWindowPos = ImGui.GetWindowPos();
                        Config.MainWindowSize = ImGui.GetWindowSize();
                    }
                    _configService.Save();
                    lockButton!.Icon = Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
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
                    var newState = !Config.EditMode;
                    if (!newState)
                    {
                        // Turning off edit mode - persist layout
                        PersistCurrentLayout();
                    }
                    else
                    {
                        // Turning on edit mode - pin window
                        Config.PinMainWindow = true;
                        Config.MainWindowPos = ImGui.GetWindowPos();
                        Config.MainWindowSize = ImGui.GetWindowSize();
                    }
                    Config.EditMode = newState;
                    _configService.Save();
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
        }

        private void ApplyInitialLayout()
        {
            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            var activeName = !string.IsNullOrWhiteSpace(Config.ActiveLayoutName) ? Config.ActiveLayoutName : null;
            ContentLayoutState? layout = null;
            
            if (activeName != null)
                layout = layouts.Find(x => x.Name == activeName);
            layout ??= layouts.FirstOrDefault();

            if (layout != null && layout.Tools != null && layout.Tools.Count > 0)
            {
                _contentContainer?.ApplyLayout(layout.Tools);
                if (string.IsNullOrWhiteSpace(Config.ActiveLayoutName)) 
                    Config.ActiveLayoutName = layout.Name;
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
                    existing = new ContentLayoutState { Name = name };
                    layouts.Add(existing);
                }
                existing.Tools = tools ?? new List<ToolLayoutState>();
                Config.ActiveLayoutName = name;
                _configService.Save();
                _configService.SaveLayouts();
                _log.Information($"Saved layout '{name}' ({existing.Tools.Count} tools)");
            };

            _contentContainer.OnLoadLayout = (name) =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                
                var layouts = Config.Layouts ?? new List<ContentLayoutState>();
                var found = layouts.Find(x => x.Name == name);
                if (found != null)
                {
                    _contentContainer.ApplyLayout(found.Tools);
                    Config.ActiveLayoutName = name;
                    _configService.Save();
                    _configService.SaveLayouts();
                    _log.Information($"Loaded layout '{name}' ({found.Tools.Count} tools)");
                }
            };

            _contentContainer.GetAvailableLayoutNames = () =>
            {
                return (Config.Layouts ?? new List<ContentLayoutState>()).Select(x => x.Name).ToList();
            };

            _contentContainer.OnLayoutChanged = (tools) =>
            {
                var activeName = !string.IsNullOrWhiteSpace(Config.ActiveLayoutName)
                    ? Config.ActiveLayoutName
                    : (Config.Layouts?.FirstOrDefault()?.Name ?? "Default");
                var layouts = Config.Layouts ??= new List<ContentLayoutState>();
                var existing = layouts.Find(x => x.Name == activeName);
                if (existing == null)
                {
                    existing = new ContentLayoutState { Name = activeName };
                    layouts.Add(existing);
                }
                existing.Tools = tools ?? new List<ToolLayoutState>();
                Config.ActiveLayoutName = activeName;
                _configService.Save();
                _configService.SaveLayouts();
                _log.Debug($"Auto-saved active layout '{activeName}' ({existing.Tools.Count} tools)");
            };
        }

        private void PersistCurrentLayout()
        {
            var layouts = Config.Layouts ??= new List<ContentLayoutState>();
            var activeName = !string.IsNullOrWhiteSpace(Config.ActiveLayoutName) ? Config.ActiveLayoutName : null;
            ContentLayoutState? layout = null;
            
            if (activeName != null)
                layout = layouts.Find(x => x.Name == activeName);
            layout ??= layouts.FirstOrDefault();

            if (layout == null)
            {
                layout = new ContentLayoutState() { Name = activeName ?? "Default" };
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
            
            if (Config.PinMainWindow)
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
            if (Config.PinMainWindow)
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

            Flags &= ~ImGuiWindowFlags.NoTitleBar;

            if (lockButton != null)
            {
                lockButton.Icon = Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }

            // If exclusive fullscreen is enabled, switch to fullscreen immediately
            if (Config.ExclusiveFullscreen)
            {
                IsOpen = false;
                _windowService?.RequestShowFullscreen();
            }
        }

        public override void Draw()
        {
            // Main content drawing: render the HUD content container
            try
            {
                _contentContainer?.Draw(Config.EditMode);
                // If the container reports layout changes, persist them into the active layout
                    try
                    {
                        if (_contentContainer != null && _contentContainer.TryConsumeLayoutDirty())
                        {
                            _log.Information("Detected layout dirty, persisting active layout");
                            var activeName = !string.IsNullOrWhiteSpace(Config.ActiveLayoutName)
                                ? Config.ActiveLayoutName
                                : (Config.Layouts?.FirstOrDefault()?.Name ?? "Default");
                            var layouts = Config.Layouts ??= new List<ContentLayoutState>();
                            var existing = layouts.Find(x => x.Name == activeName);
                            if (existing == null)
                            {
                                existing = new ContentLayoutState { Name = activeName };
                                layouts.Add(existing);
                            }
                            existing.Tools = _contentContainer.ExportLayout();
                            Config.ActiveLayoutName = activeName;
                            _configService.Save();
                        }
                    }
                    catch (Exception ex) { LogService.Debug($"[MainWindow] Layout auto-save failed: {ex.Message}"); }
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
