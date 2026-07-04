using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Kaleidoscope.Gui.ConfigWindow;
using Kaleidoscope.Gui.ConfigWindow.ConfigCategories;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Services.Universalis;
using OtterGui.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.DeveloperWindow;

/// <summary>
/// Standalone window hosting the developer-only categories (Tests, SQL Query, Profiler, Caches,
/// Logging). It is opened from the config window under the same CTRL+ALT / developer-mode gating
/// that previously revealed those categories inline, and reuses the existing
/// <see cref="IConfigCategory"/> renderers unchanged.
/// </summary>
public sealed class DeveloperWindow : Window, IService, IDisposable
{
    private readonly StateService _stateService;

    // Developer categories in sidebar display order.
    private readonly List<IConfigCategory> _categories = new();
    private int _selectedTab;

    // Flag to bring window to front on the first frame after opening.
    private bool _bringToFrontOnNextDraw;
    // Track focus state for rising-edge detection (only bring to front on focus-change).
    private bool _wasFocused;

    public DeveloperWindow(
        StateService stateService,
        ConfigurationService configService,
        CurrencyTrackerService currencyTrackerService,
        AutoRetainerService arIpc,
        UniversalisService universalisService,
        UniversalisWebSocketService webSocketService,
        LayoutEditingService layoutEditingService,
        ProfilerService profilerService,
        InventoryCacheService inventoryCacheService,
        ListingsService listingsService,
        CharacterDataService characterDataService,
        FilenameService filenameService,
        FileDialogService fileDialogService,
        ResourceObservationService resourcesService,
        ResourceStore resourceStore,
        ResourceDbWriter resourceWriter)
        : base("Kaleidoscope Developer")
    {
        _stateService = stateService;

        _categories.Add(new TestsCategory(
            currencyTrackerService,
            arIpc,
            universalisService,
            webSocketService,
            configService,
            layoutEditingService,
            resourcesService,
            resourceStore,
            resourceWriter));
        _categories.Add(new SqlQueryCategory(currencyTrackerService));
        _categories.Add(new ProfilerCategory(profilerService, configService, currencyTrackerService));
        _categories.Add(new CachesCategory(currencyTrackerService, inventoryCacheService, listingsService, characterDataService));
        _categories.Add(new LoggingCategory(configService, filenameService, fileDialogService));

        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(300, 200) };
    }

    /// <summary>Opens the developer window and brings it to the front on the next draw frame.</summary>
    public void Open()
    {
        IsOpen = true;
        _bringToFrontOnNextDraw = true;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        _bringToFrontOnNextDraw = true;
    }

    public override void Draw()
    {
        // Mirror the config window's front-most behavior so the developer window stays visible in
        // exclusive fullscreen mode, while never stealing focus from an open popup (dropdowns/modals).
        var isPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);
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

        // Sidebar layout: left navigation, right content.
        var sidebarWidth = 160f;
        var fullSize = ImGui.GetContentRegionAvail();

        ImGui.BeginChild("##developer_sidebar", new Vector2(sidebarWidth, 0), true);
        for (var i = 0; i < _categories.Count; i++)
        {
            if (ImGui.Selectable(_categories[i].Label, _selectedTab == i))
                _selectedTab = i;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##developer_content", new Vector2(fullSize.X - sidebarWidth, 0), false);
        if (_selectedTab >= 0 && _selectedTab < _categories.Count)
            _categories[_selectedTab].Draw();
        ImGui.EndChild();
    }

    /// <summary>
    /// Disposes category instances that hold resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var category in _categories)
            (category as IDisposable)?.Dispose();
    }
}
