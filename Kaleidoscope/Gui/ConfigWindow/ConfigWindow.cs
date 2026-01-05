using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Interface;
using Kaleidoscope.Gui.ConfigWindow.ConfigCategories;
using Kaleidoscope.Services;
using OtterGui.Classes;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow;

/// <summary>
/// Configuration window for plugin settings.
/// </summary>
/// <remarks>
/// Provides a sidebar-based navigation between General, Data, Characters, Currencies, and Layouts configuration categories.
/// </remarks>
public sealed class ConfigWindow : Window
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly AutoRetainerIpcService _arIpc;
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

    private Configuration Config => _configService.Config;
    private int _selectedTab;

    private TitleBarButton? _lockButton;
    private GeneralCategory? _generalCategory;
    private DataCategory? _dataCategory;
    private LayoutsCategory? _layoutsCategory;
    private CustomizationCategory? _customizationCategory;
    private UniversalisCategory? _universalisCategory;
    private ProfilerCategory? _profilerCategory;
    private CharactersCategory? _charactersCategory;
    private CurrenciesCategory? _currenciesCategory;
    private ItemsCategory? _itemsCategory;
    private ToolPresetsCategory? _toolPresetsCategory;
    private StorageCategory? _storageCategory;
    private TestsCategory? _testsCategory;
    private CachesCategory? _cachesCategory;
    private LoggingCategory? _loggingCategory;
    private SqlQueryCategory? _sqlQueryCategory;

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
        public const int ToolPresets = 6;
        public const int Customization = 7;
        public const int Universalis = 8;
        public const int Storage = 9;
        public const int Profiler = 10; // Hidden tab, only shown with CTRL+ALT
        public const int Tests = 11; // Hidden tab, only shown with CTRL+ALT
        public const int Caches = 12; // Hidden tab, only shown with CTRL+ALT
        public const int Logging = 13; // Hidden tab, only shown with CTRL+ALT
        public const int SqlQuery = 14; // Hidden tab, only shown with CTRL+ALT
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
        CurrencyTrackerService currencyTrackerService,
        AutoRetainerIpcService arIpc,
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
        MessageService messageService)
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

        // Create category renderers
        _generalCategory = new GeneralCategory(_configService, frameLimiterService, uiBuilder);
        _dataCategory = new DataCategory(_currencyTrackerService, _arIpc, _configService);
        _layoutsCategory = new LayoutsCategory(_configService);
        _customizationCategory = new CustomizationCategory(Config, _configService.Save, _layoutEditingService);
        _universalisCategory = new UniversalisCategory(_configService, _priceTrackingService, _webSocketService);
        _profilerCategory = new ProfilerCategory(_profilerService, _configService, _currencyTrackerService);
        _charactersCategory = new CharactersCategory(_currencyTrackerService, _currencyTrackerService.CacheService, _configService, _arIpc);
        _currenciesCategory = new CurrenciesCategory(_configService, _registry, textureProvider, itemDataService);
        _itemsCategory = new ItemsCategory(_configService, itemDataService, dataManager, textureProvider, favoritesService, _currencyTrackerService);
        _toolPresetsCategory = new ToolPresetsCategory(_configService);
        _storageCategory = new StorageCategory(
            _configService, 
            _currencyTrackerService, 
            _textureProvider, 
            dataManager,
            _favoritesService, 
            _messageService,
            _arIpc,
            _priceTrackingService);
        _testsCategory = new TestsCategory(_currencyTrackerService, _arIpc, _universalisService, _webSocketService, _configService, _marketDataCacheService, _layoutEditingService);
        _cachesCategory = new CachesCategory(_currencyTrackerService, inventoryCacheService, listingsService, characterDataService);
        _loggingCategory = new LoggingCategory(_configService);
        _sqlQueryCategory = new SqlQueryCategory(_currencyTrackerService);

        SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(300, 200) };
    }

    // Flag to bring window to front on the first frame after opening
    private bool _bringToFrontOnNextDraw;

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
        
        // Bring window to front when first opened (so it appears above the fullscreen main window)
        if (_bringToFrontOnNextDraw && !isPopupOpen)
        {
            _bringToFrontOnNextDraw = false;
            var window = ImGuiP.GetCurrentWindow();
            ImGuiP.BringWindowToDisplayFront(window);
        }
        
        // When focused, ensure this window stays above the fullscreen main window
        // This handles the case where user clicks on this window after interacting with main window
        // Skip when popups are open so dropdowns render above this window
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !isPopupOpen)
        {
            var window = ImGuiP.GetCurrentWindow();
            ImGuiP.BringWindowToDisplayFront(window);
        }

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
        if (ImGui.Selectable("Characters", _selectedTab == TabIndex.Characters)) _selectedTab = TabIndex.Characters;
        if (ImGui.Selectable("Items", _selectedTab == TabIndex.GameItems)) _selectedTab = TabIndex.GameItems;
        if (ImGui.Selectable("Currencies", _selectedTab == TabIndex.Currencies)) _selectedTab = TabIndex.Currencies;
        if (ImGui.Selectable("Layouts", _selectedTab == TabIndex.Layouts)) _selectedTab = TabIndex.Layouts;
        if (ImGui.Selectable("Tool Presets", _selectedTab == TabIndex.ToolPresets)) _selectedTab = TabIndex.ToolPresets;
        if (ImGui.Selectable("Customization", _selectedTab == TabIndex.Customization)) _selectedTab = TabIndex.Customization;
        if (ImGui.Selectable("Universalis", _selectedTab == TabIndex.Universalis)) _selectedTab = TabIndex.Universalis;
        if (ImGui.Selectable("Storage", _selectedTab == TabIndex.Storage)) _selectedTab = TabIndex.Storage;
        
        // Only show Developer section when CTRL+ALT are held or developer mode is enabled
        if (showProfiler)
        {
            ImGui.Separator();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "Developer");
            if (ImGui.Selectable("Data", _selectedTab == TabIndex.Data)) _selectedTab = TabIndex.Data;
            if (ImGui.Selectable("Profiler", _selectedTab == TabIndex.Profiler)) _selectedTab = TabIndex.Profiler;
            if (ImGui.Selectable("Caches", _selectedTab == TabIndex.Caches)) _selectedTab = TabIndex.Caches;
            if (ImGui.Selectable("Logging", _selectedTab == TabIndex.Logging)) _selectedTab = TabIndex.Logging;
            if (ImGui.Selectable("SQL Query", _selectedTab == TabIndex.SqlQuery)) _selectedTab = TabIndex.SqlQuery;
            if (ImGui.Selectable("Tests", _selectedTab == TabIndex.Tests)) _selectedTab = TabIndex.Tests;
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
            case TabIndex.Layouts:
                _layoutsCategory?.Draw();
                break;
            case TabIndex.ToolPresets:
                _toolPresetsCategory?.Draw();
                break;
            case TabIndex.Customization:
                _customizationCategory?.Draw();
                break;
            case TabIndex.Universalis:
                _universalisCategory?.Draw();
                break;
            case TabIndex.Storage:
                _storageCategory?.Draw();
                break;
            case TabIndex.Profiler:
                // Only draw profiler if CTRL+ALT are still held, otherwise reset to General
                if (showProfiler)
                    _profilerCategory?.Draw();
                else
                    _selectedTab = TabIndex.General;
                break;
            case TabIndex.Tests:
                // Only draw tests if CTRL+ALT are still held or dev mode enabled, otherwise reset to General
                if (showProfiler)
                    _testsCategory?.Draw();
                else
                    _selectedTab = TabIndex.General;
                break;
            case TabIndex.Caches:
                // Only draw caches if CTRL+ALT are still held or dev mode enabled, otherwise reset to General
                if (showProfiler)
                    _cachesCategory?.Draw();
                else
                    _selectedTab = TabIndex.General;
                break;
            case TabIndex.Logging:
                // Only draw logging if CTRL+ALT are still held or dev mode enabled, otherwise reset to General
                if (showProfiler)
                    _loggingCategory?.Draw();
                else
                    _selectedTab = TabIndex.General;
                break;
            case TabIndex.SqlQuery:
                // Only draw SQL query if CTRL+ALT are still held or dev mode enabled, otherwise reset to General
                if (showProfiler)
                    _sqlQueryCategory?.Draw();
                else
                    _selectedTab = TabIndex.General;
                break;
        }
        ImGui.EndChild();
    }
}
