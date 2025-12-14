using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// General settings category in the config window.
/// </summary>
public class GeneralCategory
{
    private readonly ConfigurationService _configService;

    private Configuration Config => _configService.Config;

    public GeneralCategory(ConfigurationService configService)
    {
        _configService = configService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("General");
        ImGui.Separator();
        var showOnStart = Config.ShowOnStart;
        if (ImGui.Checkbox("Show on start", ref showOnStart))
        {
            Config.ShowOnStart = showOnStart;
            _configService.Save();
        }

        var exclusiveFs = Config.ExclusiveFullscreen;
        if (ImGui.Checkbox("Exclusive fullscreen", ref exclusiveFs))
        {
            Config.ExclusiveFullscreen = exclusiveFs;
            _configService.Save();
            // Note: Fullscreen toggling now handled through WindowService
        }

        // Content container grid resolution (percentages 1-100)
        ImGui.Separator();
        ImGui.TextUnformatted("Content container grid");
        ImGui.Indent();
        var width = Config.ContentGridCellWidthPercent;
        if (ImGui.DragFloat("Cell width (%)##ContentGridWidth", ref width, 1f, 1f, 100f, "%.0f"))
        {
            if (width < 1f) width = 1f;
            if (width > 100f) width = 100f;
            Config.ContentGridCellWidthPercent = width;
            _configService.Save();
        }

        var height = Config.ContentGridCellHeightPercent;
        if (ImGui.DragFloat("Cell height (%)##ContentGridHeight", ref height, 1f, 1f, 100f, "%.0f"))
        {
            if (height < 1f) height = 1f;
            if (height > 100f) height = 100f;
            Config.ContentGridCellHeightPercent = height;
            _configService.Save();
        }
        ImGui.Unindent();
    }
}
