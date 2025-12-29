using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// General settings category in the config window.
/// </summary>
public sealed class GeneralCategory
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
        }
    }
}
