namespace Kaleidoscope.Gui.MainWindow
{
    using System.Reflection;
    using System;
    using Dalamud.Interface.Windowing;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Dalamud.Bindings.ImGui;
    using OtterGui.Text;
    using Dalamud.Interface;

        public class MainWindow : Window
    {
        private readonly MoneyTrackerComponent _moneyTracker;
        private bool _sanitizeDbOpen = false;

        public bool HasDb => _moneyTracker?.HasDb ?? false;

        public void ClearAllData()
        {
            try { _moneyTracker.ClearAllData(); } catch { }
        }

        public int CleanUnassociatedCharacters()
        {
            try { return _moneyTracker.CleanUnassociatedCharacters(); } catch { return 0; }
        }

        public string? ExportCsv()
        {
            try { return _moneyTracker.ExportCsv(); } catch { return null; }
        }

        private readonly Kaleidoscope.KaleidoscopePlugin plugin;
        private TitleBarButton? lockButton;

        public MainWindow(Kaleidoscope.KaleidoscopePlugin plugin, string? moneyTrackerDbPath = null, Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null, Func<int>? getSamplerInterval = null, Action<int>? setSamplerInterval = null) : base(GetDisplayTitle(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.plugin = plugin;
            Size = new System.Numerics.Vector2(600, 360);
            _moneyTracker = new MoneyTrackerComponent(moneyTrackerDbPath, getSamplerEnabled, setSamplerEnabled, getSamplerInterval, setSamplerInterval);

            // Create and add title bar buttons
            TitleBarButtons.Add(new TitleBarButton
            {
                Click = (m) => { if (m == ImGuiMouseButton.Left) plugin.OpenConfigUi(); },
                Icon = FontAwesomeIcon.Cog,
                IconOffset = new System.Numerics.Vector2(2, 2),
                ShowTooltip = () => ImGui.SetTooltip("Open settings"),
            });

            var lockTb = new TitleBarButton
            {
                Icon = plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                IconOffset = new System.Numerics.Vector2(3, 2),
                ShowTooltip = () => ImGui.SetTooltip("Lock window position and size"),
            };

            lockTb.Click = (m) =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    plugin.Config.PinMainWindow = !plugin.Config.PinMainWindow;
                    try { plugin.SaveConfig(); } catch { }
                    lockTb.Icon = plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
                }
            };

            lockButton = lockTb;
            TitleBarButtons.Add(lockButton);
        }

        public override void PreDraw()
        {
            if (this.plugin.Config.PinMainWindow)
            {
                Flags |= ImGuiWindowFlags.NoMove;
                Flags &= ~ImGuiWindowFlags.NoResize;
                ImGui.SetNextWindowPos(this.plugin.Config.MainWindowPos);
                ImGui.SetNextWindowSize(this.plugin.Config.MainWindowSize);
            }
            else
            {
                Flags &= ~ImGuiWindowFlags.NoMove;
            }

            // Ensure titlebar button icon reflects current configuration every frame
            if (this.lockButton != null)
            {
                this.lockButton.Icon = this.plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
            }
        }

        public override void Draw()
        {
            // Replace main content with four outlined containers stacked vertically.
            // They occupy the available content region height in percentages: 15%, 5%, 60%, 20%.
            // Use the current window size as a reliable fallback for layout calculations.
            // Use the available content region so layout accounts for titlebar/padding and avoids scrollbars
            var avail = ImGui.GetContentRegionAvail();
            var availWidth = avail.X;
            var availHeightRaw = avail.Y;
            // Use 90% of the available content width for all containers and center them horizontally
            var childWidth = availWidth * 0.9f;
            var totalHeight = availHeightRaw;

            // Ensure a sane minimum height (avoid referencing nullable external Size bindings)
            if (totalHeight <= 0f)
            {
                totalHeight = 1f;
            }

            var percents = new float[] { 0.15f, 0.05f, 0.60f, 0.20f };

            // Account for ImGui vertical spacings: we add top spacing, spacing between each container and bottom spacing.
            // Subtract total spacing height from the window height so the percentage heights apply to the remaining area.
            var style = ImGui.GetStyle();
            var spacingY = style.ItemSpacing.Y;
            var spacingCount = percents.Length + 1; // top + between each + bottom
            var totalSpacing = spacingY * spacingCount;
            var availHeight = MathF.Max(1f, totalHeight - totalSpacing);

            // add top spacing so spacing above first equals spacing between containers
            //ImGui.Spacing();

            for (var i = 0; i < percents.Length; ++i)
            {
                var h = MathF.Max(1f, availHeight * percents[i]);
                var childId = $"##kaleido_container_{i}";
                // center child horizontally (set absolute cursor X to avoid accumulating offsets)
                var indent = (availWidth - childWidth) / 2f;
                if (indent > 0f) ImGui.SetCursorPosX(indent);
                ImGui.BeginChild(childId, new System.Numerics.Vector2(childWidth, h), true);

                // Optional label (top-left) inside the container
                var label = $"Container {i + 1} - {percents[i] * 100:0}%";
                ImGui.TextUnformatted(label);

                ImGui.EndChild();

                // Add a small spacing between containers to visually separate them
                if (i < percents.Length - 1){
                    ImGui.Spacing();
                }
                else
                {
                    // add bottom spacing after the last container so top/bottom spacing match
                    //ImGui.Spacing();
                }
            }
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
