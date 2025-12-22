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

    private Configuration Config => _configService.Config;
    private int _selectedTab;

    private TitleBarButton? _lockButton;
    private GeneralCategory? _generalCategory;
    private DataCategory? _dataCategory;
    private SamplerCategory? _samplerCategory;
    private LayoutsCategory? _layoutsCategory;
    private WindowsCategory? _windowsCategory;
    private UniversalisCategory? _universalisCategory;

    /// <summary>
    /// Tab indices for programmatic navigation.
    /// </summary>
    public static class TabIndex
    {
        public const int General = 0;
        public const int Data = 1;
        public const int Sampler = 2;
        public const int Layouts = 3;
        public const int Windows = 4;
        public const int Universalis = 5;
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
        TrackedDataRegistry registry)
        : base("Kaleidoscope Configuration")
    {
        _log = log;
        _configService = configService;
        _samplerService = samplerService;
        _arIpc = arIpc;
        _registry = registry;

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
        _dataCategory = new DataCategory(_samplerService, _arIpc);
        _samplerCategory = new SamplerCategory(_samplerService, _configService, _registry);
        _layoutsCategory = new LayoutsCategory(_configService);
        _windowsCategory = new WindowsCategory(Config, _configService.Save);
        _universalisCategory = new UniversalisCategory(_configService);

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
        // Sidebar layout: left navigation, right content
        var sidebarWidth = 160f;
        var fullSize = ImGui.GetContentRegionAvail();

        // Sidebar
        ImGui.BeginChild("##config_sidebar", new System.Numerics.Vector2(sidebarWidth, 0), true);
        if (ImGui.Selectable("General", _selectedTab == 0)) _selectedTab = 0;
        if (ImGui.Selectable("Data", _selectedTab == 1)) _selectedTab = 1;
        if (ImGui.Selectable("Sampler", _selectedTab == 2)) _selectedTab = 2;
        if (ImGui.Selectable("Layouts", _selectedTab == 3)) _selectedTab = 3;
        if (ImGui.Selectable("Windows", _selectedTab == 4)) _selectedTab = 4;
        if (ImGui.Selectable("Universalis", _selectedTab == 5)) _selectedTab = 5;
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
            case TabIndex.Sampler:
                _samplerCategory?.Draw();
                break;
            case TabIndex.Layouts:
                _layoutsCategory?.Draw();
                break;
            case TabIndex.Windows:
                _windowsCategory?.Draw();
                break;
            case TabIndex.Universalis:
                _universalisCategory?.Draw();
                break;
        }
        ImGui.EndChild();
    }
}
