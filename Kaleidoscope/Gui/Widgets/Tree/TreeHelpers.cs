using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Tree;

/// <summary>
/// Static helper methods for rendering collapsible sections in ImGui.
/// </summary>
public static class TreeHelpers
{
    /// <summary>
    /// Draws a collapsible section header. Returns true if the section is open.
    /// This is a simplified wrapper for common settings/section patterns.
    /// </summary>
    /// <param name="label">The section label.</param>
    /// <param name="defaultOpen">Whether the section is open by default.</param>
    /// <param name="id">Optional unique ID suffix.</param>
    /// <returns>True if the section is open and content should be rendered.</returns>
    public static bool DrawSection(string label, bool defaultOpen = false, string? id = null)
    {
        var flags = ImGuiTreeNodeFlags.None;
        if (defaultOpen)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        
        var fullLabel = id != null ? $"{label}###{id}" : label;
        return ImGui.TreeNodeEx(fullLabel, flags);
    }
    
    /// <summary>
    /// Ends a collapsible section opened with DrawSection.
    /// Only call this if DrawSection returned true.
    /// </summary>
    public static void EndSection()
    {
        ImGui.TreePop();
    }
    
    /// <summary>
    /// Draws a collapsible header section (CollapsingHeader style).
    /// Unlike DrawSection, this does NOT require a matching EndSection call.
    /// </summary>
    /// <param name="label">The section label.</param>
    /// <param name="defaultOpen">Whether the section is open by default.</param>
    /// <param name="id">Optional unique ID suffix.</param>
    /// <returns>True if the section is open and content should be rendered.</returns>
    public static bool DrawCollapsingSection(string label, bool defaultOpen = true, string? id = null)
    {
        var flags = ImGuiTreeNodeFlags.None;
        if (defaultOpen)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        
        var fullLabel = id != null ? $"{label}###{id}" : label;
        return ImGui.CollapsingHeader(fullLabel, flags);
    }
    
    /// <summary>
    /// Draws a collapsible header section with automatic indentation for content.
    /// </summary>
    /// <param name="label">The section label.</param>
    /// <param name="defaultOpen">Whether the section is open by default.</param>
    /// <param name="contentRenderer">The content to render when open.</param>
    /// <param name="indentContent">Whether to indent the content.</param>
    public static void DrawCollapsingSectionWithContent(
        string label,
        bool defaultOpen,
        Action contentRenderer,
        bool indentContent = true)
    {
        if (DrawCollapsingSection(label, defaultOpen))
        {
            if (indentContent)
                ImGui.Indent();
            
            contentRenderer();
            
            if (indentContent)
                ImGui.Unindent();
        }
    }
}
