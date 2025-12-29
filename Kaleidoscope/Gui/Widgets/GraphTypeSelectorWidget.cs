using Dalamud.Bindings.ImGui;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable graph type selector widget for choosing visualization styles.
/// </summary>
public static class GraphTypeSelectorWidget
{
    /// <summary>Display names for graph types.</summary>
    private static readonly string[] GraphTypeNames = { "Area", "Line", "Stairs", "Bars" };

    /// <summary>
    /// Draws a graph type selector dropdown.
    /// </summary>
    /// <param name="label">Label for the dropdown.</param>
    /// <param name="graphType">Reference to the graph type value.</param>
    /// <param name="width">Width of the dropdown.</param>
    /// <returns>True if the selection changed.</returns>
    public static bool Draw(string label, ref MTGraphType graphType, float width = 150f)
    {
        bool changed = false;

        ImGui.SetNextItemWidth(width);
        var typeIndex = (int)graphType;
        if (ImGui.Combo(label, ref typeIndex, GraphTypeNames, GraphTypeNames.Length))
        {
            graphType = (MTGraphType)typeIndex;
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
    public static bool DrawWithTooltip(string label, ref MTGraphType graphType, float width = 150f)
    {
        var changed = Draw(label, ref graphType, width);

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            ImGui.TextUnformatted(
                "Graph visualization style:\n" +
                "• Area: Filled area chart - good for showing volume\n" +
                "• Line: Simple line chart\n" +
                "• Stairs: Step chart showing discrete changes\n" +
                "• Bars: Vertical bar chart");
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
    public static string GetDescription(MTGraphType graphType)
    {
        return graphType switch
        {
            MTGraphType.Area => "Filled area chart - good for showing volume over time",
            MTGraphType.Line => "Simple line chart",
            MTGraphType.Stairs => "Step chart showing discrete value changes",
            MTGraphType.Bars => "Vertical bar chart",
            _ => "Unknown graph type"
        };
    }
}
