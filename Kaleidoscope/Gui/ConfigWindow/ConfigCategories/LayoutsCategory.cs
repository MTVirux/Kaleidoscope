using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using System.Text.Json;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Layouts management category in the config window.
/// Allows viewing, editing, and importing layouts.
/// </summary>
public class LayoutsCategory
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
                    Config.ActiveWindowedLayoutName = l.Name;
                    _configService.Save();
                },
                onDelete: () =>
                {
                    Config.Layouts?.Remove(l);
                    if (string.Equals(Config.ActiveWindowedLayoutName, l.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Config.ActiveWindowedLayoutName = string.Empty;
                    }
                    _configService.Save();
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
                    Config.ActiveFullscreenLayoutName = l.Name;
                    _configService.Save();
                },
                onDelete: () =>
                {
                    Config.Layouts?.Remove(l);
                    if (string.Equals(Config.ActiveFullscreenLayoutName, l.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Config.ActiveFullscreenLayoutName = string.Empty;
                    }
                    _configService.Save();
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
                var imported = JsonSerializer.Deserialize<ContentLayoutState>(s);
                if (imported != null)
                {
                    imported.Type = targetType;
                    Config.Layouts ??= new List<ContentLayoutState>();
                    Config.Layouts.Add(imported);

                    // Force rebuild of widgets
                    if (targetType == LayoutType.Windowed)
                        _lastWindowedCount = -1;
                    else
                        _lastFullscreenCount = -1;

                    _configService.Save();
                }
            }
        }
        catch (Exception ex) { LogService.Debug($"[LayoutsCategory] Import JSON failed: {ex.Message}"); }
    }
}
