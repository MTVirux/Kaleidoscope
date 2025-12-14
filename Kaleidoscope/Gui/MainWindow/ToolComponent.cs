using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Base class for draggable/resizable tool components in the main window.
/// </summary>
public abstract class ToolComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Tool";
    public Vector2 Position { get; set; } = new Vector2(50, 50);
    public Vector2 Size { get; set; } = new Vector2(300, 200);
    public bool Visible { get; set; } = true;

    /// <summary>
    /// When enabled, a background rectangle is drawn behind the tool.
    /// </summary>
    public bool BackgroundEnabled { get; set; } = true;

    /// <summary>
    /// Background color (RGBA, components in [0,1]). Default: Dalamud red with 50% alpha.
    /// </summary>
    public Vector4 BackgroundColor { get; set; } = new Vector4(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);

    /// <summary>
    /// Called when the tool should render its UI content.
    /// </summary>
    public abstract void DrawContent();

    /// <summary>
    /// Override to return true if the tool has a settings UI.
    /// When true, the container will show a "Settings..." context menu item.
    /// </summary>
    public virtual bool HasSettings => false;

    /// <summary>
    /// Override to draw the tool-specific settings UI.
    /// Called when the user opens the settings modal.
    /// </summary>
    public virtual void DrawSettings() { }

    /// <summary>
    /// Helper for tools to show a tooltip for the last item if hovered.
    /// </summary>
    /// <param name="description">The setting description.</param>
    /// <param name="defaultText">Text indicating the default value.</param>
    protected void ShowSettingTooltip(string description, string defaultText)
    {
        try
        {
            if (!ImGui.IsItemHovered())
                return;

            ImGui.BeginTooltip();
            if (!string.IsNullOrEmpty(description))
                ImGui.TextUnformatted(description);
            if (!string.IsNullOrEmpty(defaultText))
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Default: {defaultText}");
            }
            ImGui.EndTooltip();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ToolComponent] Tooltip error: {ex.Message}");
        }
    }
}
