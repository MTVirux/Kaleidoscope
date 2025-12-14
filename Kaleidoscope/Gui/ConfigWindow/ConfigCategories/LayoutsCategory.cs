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
        private int _selectedWindowedIndex = -1;
        private int _selectedFullscreenIndex = -1;
        private string _renameBuffer = string.Empty;
        // Track which section is being edited: 0 = Windowed, 1 = Fullscreen
        private int _editingSection = 0;

        public LayoutsCategory(ConfigurationService configService)
        {
            _configService = configService;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Layouts");
            ImGui.Separator();

            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            
            // Split layouts by type
            var windowedLayouts = layouts.Where(l => l.Type == LayoutType.Windowed).ToList();
            var fullscreenLayouts = layouts.Where(l => l.Type == LayoutType.Fullscreen).ToList();

            // === Windowed Layouts Section ===
            ImGui.TextUnformatted("Windowed Layouts");
            ImGui.BeginChild("##windowed_layouts_list", new System.Numerics.Vector2(0, 120), true);
            if (windowedLayouts.Count == 0)
            {
                ImGui.TextDisabled("No windowed layouts.");
            }
            else
            {
                for (var i = 0; i < windowedLayouts.Count; i++)
                {
                    var l = windowedLayouts[i];
                    var isActive = string.Equals(l.Name, Config.ActiveLayoutName, StringComparison.OrdinalIgnoreCase);
                    var label = isActive ? $"{l.Name} (Active)" : l.Name;
                    if (ImGui.Selectable(label, _editingSection == 0 && _selectedWindowedIndex == i))
                    {
                        _selectedWindowedIndex = i;
                        _selectedFullscreenIndex = -1;
                        _editingSection = 0;
                        _renameBuffer = l.Name;
                    }
                }
            }
            ImGui.EndChild();

            ImGui.Spacing();

            // === Fullscreen Layouts Section ===
            ImGui.TextUnformatted("Fullscreen Layouts");
            ImGui.BeginChild("##fullscreen_layouts_list", new System.Numerics.Vector2(0, 120), true);
            if (fullscreenLayouts.Count == 0)
            {
                ImGui.TextDisabled("No fullscreen layouts.");
            }
            else
            {
                for (var i = 0; i < fullscreenLayouts.Count; i++)
                {
                    var l = fullscreenLayouts[i];
                    var isActive = string.Equals(l.Name, Config.ActiveLayoutName, StringComparison.OrdinalIgnoreCase);
                    var label = isActive ? $"{l.Name} (Active)" : l.Name;
                    if (ImGui.Selectable(label, _editingSection == 1 && _selectedFullscreenIndex == i))
                    {
                        _selectedFullscreenIndex = i;
                        _selectedWindowedIndex = -1;
                        _editingSection = 1;
                        _renameBuffer = l.Name;
                    }
                }
            }
            ImGui.EndChild();

            ImGui.Separator();

            // Determine which layout is selected based on section
            ContentLayoutState? selected = null;
            List<ContentLayoutState>? sourceList = null;
            int sourceIndex = -1;
            
            if (_editingSection == 0 && _selectedWindowedIndex >= 0 && _selectedWindowedIndex < windowedLayouts.Count)
            {
                selected = windowedLayouts[_selectedWindowedIndex];
                sourceList = windowedLayouts;
                sourceIndex = _selectedWindowedIndex;
            }
            else if (_editingSection == 1 && _selectedFullscreenIndex >= 0 && _selectedFullscreenIndex < fullscreenLayouts.Count)
            {
                selected = fullscreenLayouts[_selectedFullscreenIndex];
                sourceList = fullscreenLayouts;
                sourceIndex = _selectedFullscreenIndex;
            }

            // Actions for selected layout
            if (selected != null)
            {
                var typeLabel = selected.Type == LayoutType.Windowed ? "Windowed" : "Fullscreen";
                ImGui.TextUnformatted($"Selected: {selected.Name} ({typeLabel})");
                
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

                // Type conversion
                if (selected.Type == LayoutType.Windowed)
                {
                    if (ImGui.Button("Convert to Fullscreen"))
                    {
                        selected.Type = LayoutType.Fullscreen;
                        _selectedWindowedIndex = -1;
                        _configService.Save();
                    }
                }
                else
                {
                    if (ImGui.Button("Convert to Windowed"))
                    {
                        selected.Type = LayoutType.Windowed;
                        _selectedFullscreenIndex = -1;
                        _configService.Save();
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Set Active"))
                {
                    Config.ActiveLayoutName = selected.Name;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button("Delete"))
                {
                    layouts.Remove(selected);
                    _selectedWindowedIndex = -1;
                    _selectedFullscreenIndex = -1;
                    _configService.Save();
                }

                // Import/Export
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
                if (ImGui.Button("Import JSON (Windowed)"))
                {
                    ImportLayoutFromClipboard(LayoutType.Windowed);
                }

                ImGui.SameLine();
                if (ImGui.Button("Import JSON (Fullscreen)"))
                {
                    ImportLayoutFromClipboard(LayoutType.Fullscreen);
                }
            }
            else
            {
                ImGui.TextDisabled("Select a layout to edit.");
                
                // Import buttons when nothing is selected
                if (ImGui.Button("Import JSON (Windowed)"))
                {
                    ImportLayoutFromClipboard(LayoutType.Windowed);
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Import JSON (Fullscreen)"))
                {
                    ImportLayoutFromClipboard(LayoutType.Fullscreen);
                }
            }
        }

        private void ImportLayoutFromClipboard(LayoutType targetType)
        {
            try
            {
                var s = ImGui.GetClipboardText() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var imported = JsonSerializer.Deserialize<ContentLayoutState>(s);
                    if (imported != null)
                    {
                        imported.Type = targetType;
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
