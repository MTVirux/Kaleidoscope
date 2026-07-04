using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Interface;
using Kaleidoscope.Gui.ConfigWindow.ConfigCategories;
using Kaleidoscope.Services;
using OtterGui.Classes;
using OtterGui.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Universalis;
using Kaleidoscope.Services.Resources;

namespace Kaleidoscope.Gui.ConfigWindow;

/// <summary>
/// Configuration window for plugin settings.
/// </summary>
/// <remarks>
/// Provides a sidebar-based navigation between General, Data, Characters, Currencies, and Layouts configuration categories.
/// </remarks>
public sealed class ConfigWindow : Window, IService, IDisposable
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly AutoRetainerService _arIpc;
    private readonly TrackedDataRegistry _registry;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly UniversalisService _universalisService;
    private readonly ProfilerService _profilerService;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly MarketDataCacheService _marketDataCacheService;
    private readonly ITextureProvider _textureProvider;
    private readonly FavoritesService _favoritesService;
    private readonly MessageService _messageService;
    private readonly StateService _stateService;

    private Configuration Config => _configService.Config;
    private int _selectedTab;

    private TitleBarButton? _lockButton;

    // Ordered category registry in sidebar display order. Each entry pairs a stable TabIndex
    // (used by OpenToTab / external navigation) with the category renderer; IConfigCategory.IsDeveloper
    // gates whether the entry is shown behind CTRL+ALT / developer mode.
    private readonly List<(int Index, IConfigCategory Category)> _categories = new();

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
        public const int Layouts = 5;
        public const int Customization = 6;
        public const int Universalis = 7;
        public const int Storage = 8;
        public const int Profiler = 9; // Hidden tab, only shown with CTRL+ALT
        public const int Tests = 10; // Hidden tab, only shown with CTRL+ALT
        public const int Caches = 11; // Hidden tab, only shown with CTRL+ALT
        public const int Logging = 12; // Hidden tab, only shown with CTRL+ALT
        public const int SqlQuery = 13; // Hidden tab, only shown with CTRL+ALT
        public const int Integrations = 14;
    }

    /// <summary>
    /// Opens the config window to a specific tab.
    /// </summary>
    public void OpenToTab(int tabIndex)
    {
        _selectedTab = tabIndex;
        IsOpen = true;
        _bringToFrontOnNextDraw = true;
    }

    /// <summary>
    /// Brings the config window to the front on the next draw frame.
    /// If the window is not open, it will be opened first.
    /// </summary>
    public new void BringToFront()
    {
        IsOpen = true;
        _bringToFrontOnNextDraw = true;
    }

    public ConfigWindow(
        IPluginLog log,
        ConfigurationService configService,
        CurrencyTrackerService currencyTrackerService,
        AutoRetainerService arIpc,
        TrackedDataRegistry registry,
        PriceTrackingService priceTrackingService,
        UniversalisWebSocketService webSocketService,
        UniversalisService universalisService,
        ProfilerService profilerService,
        LayoutEditingService layoutEditingService,
        ItemDataService itemDataService,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        FavoritesService favoritesService,
        InventoryCacheService inventoryCacheService,
        ListingsService listingsService,
        CharacterDataService characterDataService,
        MarketDataCacheService marketDataCacheService,
        FrameLimiterService frameLimiterService,
        IUiBuilder uiBuilder,
        MessageService messageService,
        StateService stateService,
        FilenameService filenameService,
        FileDialogService fileDialogService,
        ResourceObservationService resourcesService,
        ResourceStore resourceStore,
        ResourceDbWriter resourceWriter,
        AutoRetainerFcPointsSyncService fcPointsSync)
        : base("Kaleidoscope Configuration")
    {
        _log = log;
        _configService = configService;
        _currencyTrackerService = currencyTrackerService;
        _arIpc = arIpc;
        _registry = registry;
        _priceTrackingService = priceTrackingService;
        _webSocketService = webSocketService;
        _universalisService = universalisService;
        _profilerService = profilerService;
        _layoutEditingService = layoutEditingService;
        _marketDataCacheService = marketDataCacheService;
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        _messageService = messageService;
        _stateService = stateService;

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
                    catch (Exception ex) { LogService.Debug(LogCategory.UI, $"[ConfigWindow] Failed to capture window position: {ex.Message}"); }
                }
                _configService.MarkDirty();
                lockTb.Icon = Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }
        };

        _lockButton = lockTb;
        TitleBarButtons.Add(_lockButton);

        // Register category renderers in sidebar display order (non-developer first, then developer).
        _categories.Add((TabIndex.General, new GeneralCategory(_configService, frameLimiterService, uiBuilder)));
        _categories.Add((TabIndex.Characters, new CharactersCategory(_currencyTrackerService, _currencyTrackerService.CacheService, _configService, _arIpc)));
        _categories.Add((TabIndex.GameItems, new ItemsCategory(_configService, itemDataService, dataManager, textureProvider, favoritesService, _currencyTrackerService)));
        _categories.Add((TabIndex.Currencies, new CurrenciesCategory(_configService, _registry, textureProvider, itemDataService)));
        _categories.Add((TabIndex.Layouts, new LayoutsCategory(_configService)));
        _categories.Add((TabIndex.Customization, new CustomizationCategory(Config, _configService.Save, _layoutEditingService)));
        _categories.Add((TabIndex.Universalis, new UniversalisCategory(_configService, _priceTrackingService, _webSocketService)));
        _categories.Add((TabIndex.Storage, new StorageCategory(
            _configService,
            _currencyTrackerService,
            _textureProvider,
            dataManager,
            _favoritesService,
            _messageService,
            _arIpc,
            _priceTrackingService)));
        _categories.Add((TabIndex.Integrations, new IntegrationsCategory(_arIpc, _currencyTrackerService, _currencyTrackerService.DbService, resourcesService, fcPointsSync)));
        _categories.Add((TabIndex.Data, new DataCategory(_currencyTrackerService, _configService, resourceStore)));
        _categories.Add((TabIndex.Profiler, new ProfilerCategory(_profilerService, _configService, _currencyTrackerService)));
        _categories.Add((TabIndex.Caches, new CachesCategory(_currencyTrackerService, inventoryCacheService, listingsService, characterDataService)));
        _categories.Add((TabIndex.Logging, new LoggingCategory(_configService, filenameService, fileDialogService)));
        _categories.Add((TabIndex.SqlQuery, new SqlQueryCategory(_currencyTrackerService)));
        _categories.Add((TabIndex.Tests, new TestsCategory(_currencyTrackerService, _arIpc, _universalisService, _webSocketService, _configService, _marketDataCacheService, _layoutEditingService, resourcesService, resourceStore, resourceWriter)));

        SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(300, 200) };
    }

    // Flag to bring window to front on the first frame after opening
    private bool _bringToFrontOnNextDraw;
    // Track focus state for rising-edge detection (only bring to front on focus-change)
    private bool _wasFocused;

    public override void OnOpen()
    {
        base.OnOpen();
        _bringToFrontOnNextDraw = true;
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
        // Check if any popup is open (combo dropdowns, context menus, modals)
        // We must NOT bring the window to front when a popup is open, as that would
        // render the window above the popup, making dropdowns appear "under" the window
        var isPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);
        
        // In fullscreen mode, always bring config window to front so it stays above the main window.
        // In windowed mode, only bring to front on first open or when focus is gained.
        // Skip when popups are open so dropdowns render above this window.
        var isFullscreen = _stateService.IsFullscreen;
        var isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        var shouldBringToFront = _bringToFrontOnNextDraw || isFullscreen || (isFocused && !_wasFocused);
        if (shouldBringToFront && !isPopupOpen)
        {
            _bringToFrontOnNextDraw = false;
            var window = ImGuiP.GetCurrentWindow();
            ImGuiP.BringWindowToDisplayFront(window);
        }
        _wasFocused = isFocused;

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
        var developerHeaderDrawn = false;
        foreach (var (index, category) in _categories)
        {
            if (category.IsDeveloper)
            {
                // Only show developer categories when CTRL+ALT are held or developer mode is enabled.
                if (!showProfiler)
                    continue;
                if (!developerHeaderDrawn)
                {
                    ImGui.Separator();
                    ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "Developer");
                    developerHeaderDrawn = true;
                }
            }
            if (ImGui.Selectable(category.Label, _selectedTab == index))
                _selectedTab = index;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Content area
        ImGui.BeginChild("##config_content", new System.Numerics.Vector2(fullSize.X - sidebarWidth, 0), false);
        foreach (var (index, category) in _categories)
        {
            if (_selectedTab != index)
                continue;
            // Developer categories require CTRL+ALT / dev mode still active; otherwise fall back to General.
            if (category.IsDeveloper && !showProfiler)
                _selectedTab = TabIndex.General;
            else
                category.Draw();
            break;
        }
        ImGui.EndChild();
    }
    
    /// <summary>
    /// Disposes category instances that hold resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var (_, category) in _categories)
            (category as IDisposable)?.Dispose();
    }
}
