namespace Kaleidoscope.Gui.ConfigWindow
{
    using Dalamud.Interface.Windowing;
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using OtterGui.Text;
    using Dalamud.Interface;
    using Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

    public class ConfigWindow : Window, IDisposable
    {
        private readonly Kaleidoscope.KaleidoscopePlugin plugin;
        private readonly Kaleidoscope.Configuration config;
        private readonly Action saveConfig;
        private readonly Func<bool>? getSamplerEnabled;
        private readonly Action<bool>? setSamplerEnabled;
        private readonly Func<int>? getSamplerIntervalMs;
        private readonly Action<int>? setSamplerIntervalMs;
        private readonly Func<bool>? _hasDb;
        private readonly Action? _clearAllData;
        private readonly Func<int>? _cleanUnassociatedCharacters;
        private readonly Func<string?>? _exportCsv;
        private int _selectedTab = 0; // 0=General,1=Data,2=Sampler

        private TitleBarButton? lockButton;
        private GeneralCategory? generalCategory;
        private DataCategory? dataCategory;
        private SamplerCategory? samplerCategory;
        

        public ConfigWindow(Kaleidoscope.KaleidoscopePlugin plugin, Kaleidoscope.Configuration config, Action saveConfig,
            Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null,
            Func<int>? getSamplerIntervalMs = null, Action<int>? setSamplerIntervalMs = null,
            Func<bool>? hasDb = null, Action? clearAllData = null, Func<int>? cleanUnassociatedCharacters = null, Func<string?>? exportCsv = null)
            : base("Kaleidoscope Configuration")
        {
            this.plugin = plugin;
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.saveConfig = saveConfig ?? throw new ArgumentNullException(nameof(saveConfig));
            this.getSamplerEnabled = getSamplerEnabled;
            this.setSamplerEnabled = setSamplerEnabled;
            this.getSamplerIntervalMs = getSamplerIntervalMs;
            this.setSamplerIntervalMs = setSamplerIntervalMs;
            this._hasDb = hasDb;
            this._clearAllData = clearAllData;
            this._cleanUnassociatedCharacters = cleanUnassociatedCharacters;
            this._exportCsv = exportCsv;

            var lockTb = new TitleBarButton
            {
                Icon = plugin.Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                IconOffset = new System.Numerics.Vector2(3, 2),
                ShowTooltip = () => ImGui.SetTooltip("Lock window position and size"),
            };

            lockTb.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    // Toggle pinned state. When enabling pin, capture the current window
                    // position and size so the config window remains where the user placed it.
                    var newPinned = !plugin.Config.PinConfigWindow;
                    plugin.Config.PinConfigWindow = newPinned;
                    if (newPinned)
                    {
                        try
                        {
                            plugin.Config.ConfigWindowPos = ImGui.GetWindowPos();
                            plugin.Config.ConfigWindowSize = ImGui.GetWindowSize();
                        }
                        catch { }
                    }
                    try { plugin.SaveConfig(); } catch { }
                    lockTb.Icon = plugin.Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
                }
            };

            lockButton = lockTb;
            TitleBarButtons.Add(lockButton);

            // Create category renderers
            this.generalCategory = new GeneralCategory(this.plugin, this.config, this.saveConfig);
            this.dataCategory = new DataCategory(this._hasDb, this._clearAllData, this._cleanUnassociatedCharacters, this._exportCsv);
            this.samplerCategory = new SamplerCategory(this.getSamplerEnabled, this.setSamplerEnabled, this.getSamplerIntervalMs, this.setSamplerIntervalMs, () => this.saveConfig());

            this.SizeConstraints = new WindowSizeConstraints() { MinimumSize = new System.Numerics.Vector2(300, 200) };
        }

        public void Dispose() { }

        public override void PreDraw()
        {
            // Ensure the config window is resizable in all states
            Flags &= ~ImGuiWindowFlags.NoResize;

            if (this.plugin.Config.PinConfigWindow)
            {
                Flags |= ImGuiWindowFlags.NoMove;
                ImGui.SetNextWindowPos(this.plugin.Config.ConfigWindowPos);
            }
            else
            {
                Flags &= ~ImGuiWindowFlags.NoMove;
            }

            if (this.lockButton != null)
            {
                this.lockButton.Icon = this.plugin.Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
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

            ImGui.EndChild();
        }
    }
}
