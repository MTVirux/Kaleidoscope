namespace Kaleidoscope.Gui.ConfigWindow
{
    using Dalamud.Interface.Windowing;
    using Dalamud.Bindings.ImGui;
    using Dalamud.Plugin.Services;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using OtterGui.Text;
    using Dalamud.Interface;
    using Kaleidoscope.Gui.ConfigWindow.ConfigCategories;
    using Kaleidoscope.Services;

    public class ConfigWindow : Window, IDisposable
    {
        private readonly IPluginLog _log;
        private readonly ConfigurationService _configService;
        private readonly SamplerService _samplerService;

        private Configuration Config => _configService.Config;
        private int _selectedTab = 0; // 0=General,1=Data,2=Sampler

        private TitleBarButton? lockButton;
        private GeneralCategory? generalCategory;
        private DataCategory? dataCategory;
        private SamplerCategory? samplerCategory;
        private LayoutsCategory? layoutsCategory;
        

        public ConfigWindow(
            IPluginLog log,
            ConfigurationService configService,
            SamplerService samplerService)
            : base("Kaleidoscope Configuration")
        {
            _log = log;
            _configService = configService;
            _samplerService = samplerService;

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
                        catch { }
                    }
                    _configService.Save();
                    lockTb.Icon = Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
                }
            };

            lockButton = lockTb;
            TitleBarButtons.Add(lockButton);

            // Create category renderers
            this.generalCategory = new GeneralCategory(_configService);
            this.dataCategory = new DataCategory(_samplerService);
            this.samplerCategory = new SamplerCategory(_samplerService, _configService);
            this.layoutsCategory = new LayoutsCategory(_configService);

            this.SizeConstraints = new WindowSizeConstraints() { MinimumSize = new System.Numerics.Vector2(300, 200) };
        }

        public void Dispose() { }

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

            if (this.lockButton != null)
            {
                this.lockButton.Icon = Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
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
            ImGui.EndChild();

            ImGui.SameLine();

            // Content area
            ImGui.BeginChild("##config_content", new System.Numerics.Vector2(fullSize.X - sidebarWidth, 0), false);
            if (_selectedTab == 0)
            {
                this.generalCategory?.Draw();
            }
            else if (_selectedTab == 1)
            {
                this.dataCategory?.Draw();
            }
            else if (_selectedTab == 2)
            {
                this.samplerCategory?.Draw();
            }
            else if (_selectedTab == 3)
            {
                this.layoutsCategory?.Draw();
            }

            ImGui.EndChild();
        }
    }
}
