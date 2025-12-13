namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using System.Text.Json;

    public class LayoutsCategory
    {
        private readonly Kaleidoscope.KaleidoscopePlugin plugin;
        private readonly Kaleidoscope.Configuration config;
        private readonly Action saveConfig;

        private int _selectedIndex = -1;
        private string _renameBuffer = string.Empty;

        public LayoutsCategory(Kaleidoscope.KaleidoscopePlugin plugin, Kaleidoscope.Configuration config, Action saveConfig)
        {
            this.plugin = plugin;
            this.config = config;
            this.saveConfig = saveConfig;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Layouts");
            ImGui.Separator();

            var layouts = this.config.Layouts ?? new List<ContentLayoutState>();
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
                        this.saveConfig();
                    }
                }

                // 2nd line
                if (ImGui.Button("Set Active"))
                {
                    this.config.ActiveLayoutName = selected.Name;
                    try { this.plugin.ApplyLayout(selected.Name); } catch { }
                    this.saveConfig();
                }

                ImGui.SameLine();
                if (ImGui.Button("Make Default"))
                {
                    this.config.ActiveLayoutName = selected.Name;
                    this.saveConfig();
                }

                ImGui.SameLine();
                if (ImGui.Button("Delete"))
                {
                    layouts.RemoveAt(_selectedIndex);
                    _selectedIndex = -1;
                    this.saveConfig();
                }

                //Import/Export
                if (ImGui.Button("Export JSON"))
                {
                    try
                    {
                        var s = JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true });
                        ImGui.SetClipboardText(s);
                    }
                    catch { }
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
                                this.config.Layouts ??= new List<ContentLayoutState>();
                                this.config.Layouts.Add(imported);
                                this.saveConfig();
                            }
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
