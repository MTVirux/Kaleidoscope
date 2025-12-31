using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Newtonsoft.Json;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Layouts management category in the config window.
/// Allows viewing, editing, and importing layouts.
/// </summary>
public sealed class LayoutsCategory
{
    private readonly ConfigurationService _configService;

    private Configuration Config => _configService.Config;

    // Cache of layout widgets, rebuilt when layouts change
    private List<LayoutItemWidget> _windowedWidgets = new();
    private List<LayoutItemWidget> _fullscreenWidgets = new();
    private int _lastWindowedCount = -1;
    private int _lastFullscreenCount = -1;

    public LayoutsCategory(ConfigurationService configService)
    {
        _configService = configService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Layouts");
        ImGui.Separator();

        // Auto-save setting
        var autoSave = Config.AutoSaveLayoutChanges;
        if (ImGui.Checkbox("Auto-save layout changes", ref autoSave))
        {
            Config.AutoSaveLayoutChanges = autoSave;
            _configService.MarkDirty();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically save layout changes without requiring manual save.\nDisable for explicit save/discard workflow.");
        }
        ImGui.Spacing();

        var layouts = Config.Layouts ?? new List<ContentLayoutState>();

        // Split layouts by type
        var windowedLayouts = layouts.Where(l => l.Type == LayoutType.Windowed).ToList();
        var fullscreenLayouts = layouts.Where(l => l.Type == LayoutType.Fullscreen).ToList();

        // Rebuild widgets if counts changed
        if (windowedLayouts.Count != _lastWindowedCount)
        {
            RebuildWindowedWidgets(windowedLayouts);
            _lastWindowedCount = windowedLayouts.Count;
        }

        if (fullscreenLayouts.Count != _lastFullscreenCount)
        {
            RebuildFullscreenWidgets(fullscreenLayouts);
            _lastFullscreenCount = fullscreenLayouts.Count;
        }

        // === Windowed Layouts Section ===
        ImGui.TextUnformatted("Windowed Layouts");
        ImGui.SameLine();
        if (ImGui.Button("New##windowed"))
        {
            CreateNewLayout(LayoutType.Windowed);
        }
        ImGui.Spacing();

        if (windowedLayouts.Count == 0)
        {
            ImGui.TextDisabled("No windowed layouts. Save a layout from the main window to create one.");
        }
        else
        {
            var toRemove = -1;
            for (var i = 0; i < _windowedWidgets.Count; i++)
            {
                if (_windowedWidgets[i].Draw())
                {
                    toRemove = i;
                }
            }

            if (toRemove >= 0)
            {
                _lastWindowedCount = -1; // Force rebuild
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === Fullscreen Layouts Section ===
        ImGui.TextUnformatted("Fullscreen Layouts");
        ImGui.SameLine();
        if (ImGui.Button("New##fullscreen"))
        {
            CreateNewLayout(LayoutType.Fullscreen);
        }
        ImGui.Spacing();

        if (fullscreenLayouts.Count == 0)
        {
            ImGui.TextDisabled("No fullscreen layouts. Save a layout from fullscreen mode to create one.");
        }
        else
        {
            var toRemove = -1;
            for (var i = 0; i < _fullscreenWidgets.Count; i++)
            {
                if (_fullscreenWidgets[i].Draw())
                {
                    toRemove = i;
                }
            }

            if (toRemove >= 0)
            {
                _lastFullscreenCount = -1; // Force rebuild
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Import section
        ImGui.TextUnformatted("Import Layout");
        if (ImGui.Button("Import from Clipboard (Windowed)"))
        {
            ImportLayoutFromClipboard(LayoutType.Windowed);
        }

        ImGui.SameLine();

        if (ImGui.Button("Import from Clipboard (Fullscreen)"))
        {
            ImportLayoutFromClipboard(LayoutType.Fullscreen);
        }
    }

    private void RebuildWindowedWidgets(List<ContentLayoutState> windowedLayouts)
    {
        _windowedWidgets.Clear();
        foreach (var layout in windowedLayouts)
        {
            var l = layout; // Capture for closure
            _windowedWidgets.Add(new LayoutItemWidget(
                _configService,
                l,
                isActive: () => string.Equals(Config.ActiveWindowedLayoutName, l.Name, StringComparison.OrdinalIgnoreCase),
                onSetActive: () =>
                {
                    _configService.SetActiveLayout(l.Name, LayoutType.Windowed);
                },
                onDelete: () =>
                {
                    Config.Layouts?.Remove(l);
                    if (string.Equals(Config.ActiveWindowedLayoutName, l.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Config.ActiveWindowedLayoutName = string.Empty;
                    }
                    _configService.MarkDirty();
                }
            ));
        }
    }

    private void RebuildFullscreenWidgets(List<ContentLayoutState> fullscreenLayouts)
    {
        _fullscreenWidgets.Clear();
        foreach (var layout in fullscreenLayouts)
        {
            var l = layout; // Capture for closure
            _fullscreenWidgets.Add(new LayoutItemWidget(
                _configService,
                l,
                isActive: () => string.Equals(Config.ActiveFullscreenLayoutName, l.Name, StringComparison.OrdinalIgnoreCase),
                onSetActive: () =>
                {
                    _configService.SetActiveLayout(l.Name, LayoutType.Fullscreen);
                },
                onDelete: () =>
                {
                    Config.Layouts?.Remove(l);
                    if (string.Equals(Config.ActiveFullscreenLayoutName, l.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Config.ActiveFullscreenLayoutName = string.Empty;
                    }
                    _configService.MarkDirty();
                }
            ));
        }
    }

    private void ImportLayoutFromClipboard(LayoutType targetType)
    {
        try
        {
            var s = ImGui.GetClipboardText() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(s))
            {
                var imported = JsonConvert.DeserializeObject<ContentLayoutState>(s);
                if (imported != null)
                {
                    // When importing to a different layout type (windowed <-> fullscreen),
                    // the pixel positions are invalid because they were calculated for a
                    // different window size. Clear them so the grid coordinates are used
                    // to recalculate positions when the layout is loaded.
                    if (imported.Tools != null)
                    {
                        foreach (var tool in imported.Tools)
                        {
                            if (tool.HasGridCoords)
                            {
                                // Clear pixel positions - they'll be recalculated from grid coords on load
                                // The grid coordinates are proportional to the layout's grid settings,
                                // which are copied along with the layout, so positions will be correct.
                                tool.Position = Vector2.Zero;
                                tool.Size = Vector2.Zero;
                            }
                        }
                    }
                    
                    imported.Type = targetType;
                    Config.Layouts ??= new List<ContentLayoutState>();
                    
                    // Ensure unique name within the same layout type
                    var baseName = imported.Name;
                    if (string.IsNullOrWhiteSpace(baseName))
                        baseName = "Imported Layout";
                    var name = baseName;
                    var counter = 1;
                    while (Config.Layouts.Any(l => l.Type == targetType && 
                                                    string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        counter++;
                        name = $"{baseName} ({counter})";
                    }
                    imported.Name = name;
                    
                    Config.Layouts.Add(imported);

                    // Force rebuild of widgets
                    if (targetType == LayoutType.Windowed)
                        _lastWindowedCount = -1;
                    else
                        _lastFullscreenCount = -1;

                    _configService.MarkDirty();
                    _configService.SaveLayouts();
                }
            }
        }
        catch (Exception ex) { LogService.Debug($"[LayoutsCategory] Import JSON failed: {ex.Message}"); }
    }

    private void CreateNewLayout(LayoutType layoutType)
    {
        try
        {
            Config.Layouts ??= new List<ContentLayoutState>();
            
            // Generate a unique name
            var baseName = layoutType == LayoutType.Windowed ? "New Layout" : "New Fullscreen Layout";
            var name = baseName;
            var counter = 1;
            while (Config.Layouts.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                counter++;
                name = $"{baseName} {counter}";
            }
            
            var newLayout = new ContentLayoutState
            {
                Name = name,
                Type = layoutType,
                Tools = new List<ToolLayoutState>()
            };
            
            Config.Layouts.Add(newLayout);
            
            // Force rebuild of widgets
            if (layoutType == LayoutType.Windowed)
                _lastWindowedCount = -1;
            else
                _lastFullscreenCount = -1;
            
            _configService.MarkDirty();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[LayoutsCategory] Create layout failed: {ex.Message}");
        }
    }
}
