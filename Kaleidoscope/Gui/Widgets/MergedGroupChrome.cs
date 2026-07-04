using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Shared rendering primitives for merged-group rows, used by both the column
/// management and source (row) merge widgets. The caller owns positioning
/// (table cell vs SameLine); these helpers only draw the individual chrome pieces.
/// </summary>
internal static class MergedGroupChrome
{
    /// <summary>Draws the "⊕" merged-group indicator with its hover tooltip.</summary>
    public static void DrawMergeIndicator()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.HqPrice);
        ImGui.TextUnformatted("⊕");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Merged group");
    }

    /// <summary>Draws the "[N merged]" count label with a tooltip listing member names.</summary>
    public static void DrawMergedCountLabel(int count, IEnumerable<string> memberNames)
    {
        ImGui.TextDisabled($"[{count} merged]");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(string.Join("\n", memberNames));
    }

    /// <summary>Draws the Unmerge button with the given tooltip. Returns true when clicked.</summary>
    public static bool DrawUnmergeButton(string tooltip)
    {
        var clicked = ImGuiHelpers.PrimaryButton("Unmerge##unmerge");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        return clicked;
    }
}
