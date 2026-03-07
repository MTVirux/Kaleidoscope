using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable graph type selector widget for choosing visualization styles.
/// </summary>
public static class GraphTypeSelectorWidget
{
    /// <summary>Display names for graph types.</summary>
    private static readonly string[] GraphTypeNames = { "Stairs", "Stairs Area" };
    
    /// <summary>Mapping from combo index to GraphType.</summary>
    private static readonly GraphType[] GraphTypeValues = { GraphType.Stairs, GraphType.StairsArea };

    /// <summary>
    /// Draws a graph type selector dropdown.
    /// </summary>
    /// <param name="label">Label for the dropdown.</param>
    /// <param name="graphType">Reference to the graph type value.</param>
    /// <param name="width">Width of the dropdown.</param>
    /// <returns>True if the selection changed.</returns>
    public static bool Draw(string label, ref GraphType graphType, float width = 150f)
    {
        bool changed = false;

        ImGui.SetNextItemWidth(width);
        var typeIndex = Array.IndexOf(GraphTypeValues, graphType);
        if (typeIndex < 0) typeIndex = 0; // Default to first option if not found
        if (ImGui.Combo(label, ref typeIndex, GraphTypeNames, GraphTypeNames.Length))
        {
            graphType = GraphTypeValues[typeIndex];
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Draws a graph type selector with tooltip.
    /// </summary>
    /// <param name="label">Label for the dropdown.</param>
    /// <param name="graphType">Reference to the graph type value.</param>
    /// <param name="width">Width of the dropdown.</param>
    /// <returns>True if the selection changed.</returns>
    public static bool DrawWithTooltip(string label, ref GraphType graphType, float width = 150f)
    {
        var changed = Draw(label, ref graphType, width);

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            ImGui.TextUnformatted(
                "Graph visualization style:\n" +
                "• Stairs: Step chart showing discrete changes\n" +
                "• Stairs Area: Step chart with filled area below");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        return changed;
    }

    /// <summary>
    /// Gets a description of the graph type.
    /// </summary>
    /// <param name="graphType">The graph type.</param>
    /// <returns>Human-readable description.</returns>
    public static string GetDescription(GraphType graphType)
    {
        return graphType switch
        {
            GraphType.Stairs => "Step chart showing discrete value changes",
            GraphType.StairsArea => "Step chart with filled area below",
            _ => "Unknown graph type"
        };
    }
}
