using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Interface;
using Kaleidoscope.Gui.ConfigWindow.ConfigCategories;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow;

/// <summary>
/// Configuration window for plugin settings.
/// </summary>
/// <remarks>
/// Provides a sidebar-based navigation between General, Data, Sampler, and Layouts configuration categories.
/// </remarks>
public sealed class ConfigWindow : Window
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly SamplerService _samplerService;
    private readonly AutoRetainerIpcService _arIpc;
    private readonly TrackedDataRegistry _registry;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly ProfilerService _profilerService;

    private Configuration Config => _configService.Config;
    private int _selectedTab;

    private TitleBarButton? _lockButton;
    private GeneralCategory? _generalCategory;
    private DataCategory? _dataCategory;
    private SamplerCategory? _samplerCategory;
    private LayoutsCategory? _layoutsCategory;
    private WindowsCategory? _windowsCategory;
    private UniversalisCategory? _universalisCategory;
    private ProfilerCategory? _profilerCategory;
    private CharactersCategory? _charactersCategory;
    private CurrenciesCategory? _currenciesCategory;
    private ItemsCategory? _itemsCategory;
    private ToolPresetsCategory? _toolPresetsCategory;

    /// <summary>
    /// Tab indices for programmatic navigation.
    /// </summary>
    public static class TabIndex
    {
        public const int General = 0;
        public const int Data = 1;
        public const int Characters = 2;
        public const int GameItems = 3;
        public const int Currencies = 4;
        public const int Sampler = 5;
        public const int Layouts = 6;
        public const int ToolPresets = 7;
        public const int Windows = 8;
        public const int Universalis = 9;
        public const int Profiler = 10; // Hidden tab, only shown with CTRL+ALT
    }

    /// <summary>
    /// Opens the config window to a specific tab.
    /// </summary>
    public void OpenToTab(int tabIndex)
    {
        _selectedTab = tabIndex;
        IsOpen = true;
    }

    public ConfigWindow(
        IPluginLog log,
        ConfigurationService configService,
        SamplerService samplerService,
        AutoRetainerIpcService arIpc,
        TrackedDataRegistry registry,
        PriceTrackingService priceTrackingService,
        UniversalisWebSocketService webSocketService,
        ProfilerService profilerService,
        ItemDataService itemDataService,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        FavoritesService favoritesService)
        : base("Kaleidoscope Configuration")
    {
        _log = log;
        _configService = configService;
        _samplerService = samplerService;
        _arIpc = arIpc;
        _registry = registry;
        _priceTrackingService = priceTrackingService;
        _webSocketService = webSocketService;
        _profilerService = profilerService;

        var lockTb = new TitleBarButton
        {
            Icon = Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            IconOffset = new System.Numerics.Vector2(3, 2),
            ShowTooltip = () => ImGui.SetTooltip("Lock window position and size"),
        };

        lockTb.Click = (m) =>
        {
            if (m == ImGuiMouseButton.Left)
            {
                // Toggle pinned state. When enabling pin, capture the current window
                // position and size so the config window remains where the user placed it.
                var newPinned = !Config.PinConfigWindow;
                Config.PinConfigWindow = newPinned;
                if (newPinned)
                {
                    try
                    {
                        Config.ConfigWindowPos = ImGui.GetWindowPos();
                        Config.ConfigWindowSize = ImGui.GetWindowSize();
                    }
                    catch (Exception ex) { LogService.Debug($"[ConfigWindow] Failed to capture window position: {ex.Message}"); }
                }
                _configService.Save();
                lockTb.Icon = Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }
        };

        _lockButton = lockTb;
        TitleBarButtons.Add(_lockButton);

        // Create category renderers
        _generalCategory = new GeneralCategory(_configService);
        _dataCategory = new DataCategory(_samplerService, _arIpc, _configService);
        _samplerCategory = new SamplerCategory(_samplerService, _configService, _registry);
        _layoutsCategory = new LayoutsCategory(_configService);
        _windowsCategory = new WindowsCategory(Config, _configService.Save);
        _universalisCategory = new UniversalisCategory(_configService, _priceTrackingService, _webSocketService);
        _profilerCategory = new ProfilerCategory(_profilerService, _configService, _samplerService);
        _charactersCategory = new CharactersCategory(_samplerService, _samplerService.CacheService, _configService, _arIpc);
        _currenciesCategory = new CurrenciesCategory(_configService, _registry, textureProvider, itemDataService);
        _itemsCategory = new ItemsCategory(_configService, itemDataService, dataManager, textureProvider, favoritesService, _samplerService);
        _toolPresetsCategory = new ToolPresetsCategory(_configService);

        SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(300, 200) };
    }

    public override void PreDraw()
    {
        // Ensure the config window is resizable in all states
        Flags &= ~ImGuiWindowFlags.NoResize;

        if (Config.PinConfigWindow)
        {
            Flags |= ImGuiWindowFlags.NoMove;
            ImGui.SetNextWindowPos(Config.ConfigWindowPos);
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }

        if (_lockButton != null)
        {
            _lockButton.Icon = Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
        }
    }

    public override void Draw()
    {
        // Check if CTRL+ALT are held while this window is focused for profiler access
        // Or if developer mode is permanently enabled
        var io = ImGui.GetIO();
        var showProfiler = Config.DeveloperModeEnabled || 
            (io.KeyCtrl && io.KeyAlt && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows));

        // Sidebar layout: left navigation, right content
        var sidebarWidth = 160f;
        var fullSize = ImGui.GetContentRegionAvail();

        // Sidebar
        ImGui.BeginChild("##config_sidebar", new System.Numerics.Vector2(sidebarWidth, 0), true);
        if (ImGui.Selectable("General", _selectedTab == TabIndex.General)) _selectedTab = TabIndex.General;
        if (ImGui.Selectable("Data", _selectedTab == TabIndex.Data)) _selectedTab = TabIndex.Data;
        if (ImGui.Selectable("Characters", _selectedTab == TabIndex.Characters)) _selectedTab = TabIndex.Characters;
        if (ImGui.Selectable("Items", _selectedTab == TabIndex.GameItems)) _selectedTab = TabIndex.GameItems;
        if (ImGui.Selectable("Currencies", _selectedTab == TabIndex.Currencies)) _selectedTab = TabIndex.Currencies;
        if (ImGui.Selectable("Sampler", _selectedTab == TabIndex.Sampler)) _selectedTab = TabIndex.Sampler;
        if (ImGui.Selectable("Layouts", _selectedTab == TabIndex.Layouts)) _selectedTab = TabIndex.Layouts;
        if (ImGui.Selectable("Tool Presets", _selectedTab == TabIndex.ToolPresets)) _selectedTab = TabIndex.ToolPresets;
        if (ImGui.Selectable("Windows", _selectedTab == TabIndex.Windows)) _selectedTab = TabIndex.Windows;
        if (ImGui.Selectable("Universalis", _selectedTab == TabIndex.Universalis)) _selectedTab = TabIndex.Universalis;
        
        // Only show Profiler tab when CTRL+ALT are held
        if (showProfiler)
        {
            ImGui.Separator();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "Developer");
            if (ImGui.Selectable("Profiler", _selectedTab == TabIndex.Profiler)) _selectedTab = TabIndex.Profiler;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Content area
        ImGui.BeginChild("##config_content", new System.Numerics.Vector2(fullSize.X - sidebarWidth, 0), false);
        switch (_selectedTab)
        {
            case TabIndex.General:
                _generalCategory?.Draw();
                break;
            case TabIndex.Data:
                _dataCategory?.Draw();
                break;
            case TabIndex.Characters:
                _charactersCategory?.Draw();
                break;
            case TabIndex.Currencies:
                _currenciesCategory?.Draw();
                break;
            case TabIndex.GameItems:
                _itemsCategory?.Draw();
                break;
            case TabIndex.Sampler:
                _samplerCategory?.Draw();
                break;
            case TabIndex.Layouts:
                _layoutsCategory?.Draw();
                break;
            case TabIndex.ToolPresets:
                _toolPresetsCategory?.Draw();
                break;
            case TabIndex.Windows:
                _windowsCategory?.Draw();
                break;
            case TabIndex.Universalis:
                _universalisCategory?.Draw();
                break;
            case TabIndex.Profiler:
                // Only draw profiler if CTRL+ALT are still held, otherwise reset to General
                if (showProfiler)
                    _profilerCategory?.Draw();
                else
                    _selectedTab = TabIndex.General;
                break;
        }
        ImGui.EndChild();
    }
}
