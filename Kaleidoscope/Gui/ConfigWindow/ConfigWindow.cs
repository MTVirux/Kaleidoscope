namespace Kaleidoscope.Gui.ConfigWindow
{
    using Dalamud.Interface.Windowing;
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using OtterGui.Text;
    using Dalamud.Interface;

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
        private bool _clearDbOpen = false;
        private bool _sanitizeDbOpen = false;
        private int _selectedTab = 0; // 0=General,1=Data,2=Sampler,3=Windows

        private TitleBarButton? lockButton;

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
            ImGui.TextUnformatted("Settings");
            ImGui.Separator();
            if (ImGui.Selectable("General", _selectedTab == 0)) _selectedTab = 0;
            if (ImGui.Selectable("Data", _selectedTab == 1)) _selectedTab = 1;
            if (ImGui.Selectable("Sampler", _selectedTab == 2)) _selectedTab = 2;
            if (ImGui.Selectable("Windows", _selectedTab == 3)) _selectedTab = 3;
            ImGui.EndChild();

            ImGui.SameLine();

            // Content area
            ImGui.BeginChild("##config_content", new System.Numerics.Vector2(fullSize.X - sidebarWidth, 0), false);
            if (_selectedTab == 0)
            {
                ImGui.TextUnformatted("General");
                ImGui.Separator();
                var showOnStart = this.config.ShowOnStart;
                if (ImGui.Checkbox("Show on start", ref showOnStart))
                {
                    this.config.ShowOnStart = showOnStart;
                    this.saveConfig();
                }
            }
            else if (_selectedTab == 1)
            {
                ImGui.TextUnformatted("Data Management");
                ImGui.Separator();
                var hasDb = this._hasDb != null ? this._hasDb() : false;
                if (ImGui.Button("Export CSV") && hasDb)
                {
                    try
                    {
                        var fileName = this._exportCsv != null ? this._exportCsv() : null;
                        if (!string.IsNullOrEmpty(fileName)) ImGui.TextUnformatted($"Exported to {fileName}");
                    }
                    catch { }
                }

                if (hasDb)
                {
                    if (ImGui.Button("Clear DB"))
                    {
                        ImGui.OpenPopup("config_clear_db_confirm");
                        _clearDbOpen = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Sanitize DB Data"))
                    {
                        ImGui.OpenPopup("config_sanitize_db_confirm");
                        _sanitizeDbOpen = true;
                    }
                }

                if (ImGui.BeginPopupModal("config_clear_db_confirm", ref _clearDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.TextUnformatted("This will permanently delete all saved Money Tracker data from the DB for all characters. Proceed?");
                    if (ImGui.Button("Yes"))
                    {
                        try { this._clearAllData?.Invoke(); } catch { }
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("No"))
                    {
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }

                if (ImGui.BeginPopupModal("config_sanitize_db_confirm", ref _sanitizeDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.TextUnformatted("This will remove Money Tracker data for characters that do not have a stored name association. Proceed?");
                    if (ImGui.Button("Yes"))
                    {
                        try { this._cleanUnassociatedCharacters?.Invoke(); } catch { }
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("No"))
                    {
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
            else if (_selectedTab == 2)
            {
                ImGui.TextUnformatted("Sampler");
                ImGui.Separator();
                if (this.getSamplerEnabled != null && this.setSamplerEnabled != null)
                {
                    var enabled = this.getSamplerEnabled();
                    if (ImGui.Checkbox("Enable sampler", ref enabled))
                    {
                        this.setSamplerEnabled(enabled);
                        this.saveConfig();
                    }
                }

                if (this.getSamplerIntervalMs != null && this.setSamplerIntervalMs != null)
                {
                    var interval = this.getSamplerIntervalMs();
                    if (ImGui.InputInt("Sampler interval (ms)", ref interval))
                    {
                        if (interval < 1) interval = 1;
                        this.setSamplerIntervalMs(interval);
                        this.saveConfig();
                    }
                }
            }
            else if (_selectedTab == 3)
            {
                ImGui.TextUnformatted("Windows");
                ImGui.Separator();
                var pinMain = this.config.PinMainWindow;
                if (ImGui.Checkbox("Pin main window", ref pinMain))
                {
                    this.config.PinMainWindow = pinMain;
                    this.saveConfig();
                }
                var pinConfig = this.config.PinConfigWindow;
                if (ImGui.Checkbox("Pin config window", ref pinConfig))
                {
                    this.config.PinConfigWindow = pinConfig;
                    this.saveConfig();
                }
            }

            ImGui.EndChild();
        }
    }
}
