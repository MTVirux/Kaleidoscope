namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using System.Text.Json;
    using Kaleidoscope.Services;

    public class LayoutsCategory
    {
        private readonly ConfigurationService _configService;

        private Configuration Config => _configService.Config;
        private int _selectedIndex = -1;
        private string _renameBuffer = string.Empty;

        public LayoutsCategory(ConfigurationService configService)
        {
            _configService = configService;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Layouts");
            ImGui.Separator();

            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            if (layouts.Count == 0)
            {
                ImGui.TextUnformatted("No saved layouts.");
                return;
            }

            // Show list
            ImGui.BeginChild("##layouts_list", new System.Numerics.Vector2(0, 200), true);
            for (var i = 0; i < layouts.Count; i++)
            {
                var l = layouts[i];
                if (ImGui.Selectable(l.Name, _selectedIndex == i))
                {
                    _selectedIndex = i;
                    _renameBuffer = l.Name;
                }
            }
            ImGui.EndChild();

            ImGui.Separator();

            // Actions
                if (_selectedIndex >= 0 && _selectedIndex < layouts.Count)
            {
                var selected = layouts[_selectedIndex];
                ImGui.TextUnformatted($"Selected: {selected.Name}");
                ImGui.InputText("Rename##layout", ref _renameBuffer, 128);
                ImGui.SameLine();
                if (ImGui.Button("Rename"))
                {
                    if (!string.IsNullOrWhiteSpace(_renameBuffer))
                    {
                        selected.Name = _renameBuffer;
                        _configService.Save();
                    }
                }

                // 2nd line
                if (ImGui.Button("Set Active"))
                {
                    Config.ActiveLayoutName = selected.Name;
                    // Note: Layout application is handled through LayoutService or MainWindow
                    _configService.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button("Make Default"))
                {
                    Config.ActiveLayoutName = selected.Name;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button("Delete"))
                {
                    layouts.RemoveAt(_selectedIndex);
                    _selectedIndex = -1;
                    _configService.Save();
                }

                //Import/Export
                if (ImGui.Button("Export JSON"))
                {
                    try
                    {
                        var s = JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true });
                        ImGui.SetClipboardText(s);
                    }
                    catch (Exception ex) { LogService.Debug($"[LayoutsCategory] Export JSON failed: {ex.Message}"); }
                }

                ImGui.SameLine();
                if (ImGui.Button("Import JSON"))
                {
                    try
                    {
                        var s = ImGui.GetClipboardText() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            var imported = JsonSerializer.Deserialize<ContentLayoutState>(s);
                            if (imported != null)
                            {
                                Config.Layouts ??= new List<ContentLayoutState>();
                                Config.Layouts.Add(imported);
                                _configService.Save();
                            }
                        }
                    }
                    catch (Exception ex) { LogService.Debug($"[LayoutsCategory] Import JSON failed: {ex.Message}"); }
                }
            }
        }
    }
}
