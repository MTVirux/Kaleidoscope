using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Windows configuration category in the config window.
/// Controls window background colors.
/// </summary>
public class WindowsCategory
{
    private readonly Kaleidoscope.Configuration config;
    private readonly Action saveConfig;
    
    // Default ImGui theme background color
    private static readonly Vector4 DefaultBackgroundColor = new(0.06f, 0.06f, 0.06f, 0.94f);

    public WindowsCategory(Kaleidoscope.Configuration config, Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Window Backgrounds");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Main Window");
        var mainBgColor = this.config.MainWindowBackgroundColor;
        if (ImGui.ColorEdit4("##MainWindowBg", ref mainBgColor, ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            this.config.MainWindowBackgroundColor = mainBgColor;
            this.saveConfig();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset##MainWindowBgReset"))
        {
            this.config.MainWindowBackgroundColor = DefaultBackgroundColor;
            this.saveConfig();
        }

        ImGui.Spacing();

        ImGui.TextUnformatted("Fullscreen");
        var fsBgColor = this.config.FullscreenBackgroundColor;
        if (ImGui.ColorEdit4("##FullscreenBg", ref fsBgColor, ImGuiColorEditFlags.AlphaPreviewHalf))
        {
            this.config.FullscreenBackgroundColor = fsBgColor;
            this.saveConfig();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset##FullscreenBgReset"))
        {
            this.config.FullscreenBackgroundColor = DefaultBackgroundColor;
            this.saveConfig();
        }
    }
}
