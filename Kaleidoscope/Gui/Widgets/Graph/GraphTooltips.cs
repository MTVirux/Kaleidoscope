using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Static utilities for drawing tooltips on ImPlot graphs.
/// </summary>
public static class GraphTooltips
{
    /// <summary>
    /// Draws a styled tooltip box at the given position.
    /// </summary>
    /// <param name="screenPos">Screen position for the tooltip (typically mouse position).</param>
    /// <param name="lines">Array of text lines to display.</param>
    /// <param name="accentColor">Color for the accent bar on the left side of the tooltip.</param>
    /// <param name="style">Optional style configuration.</param>
    public static void DrawTooltipBox(Vector2 screenPos, string[] lines, Vector4 accentColor, GraphStyleConfig? style = null)
    {
        style ??= GraphStyleConfig.Default;
        var colors = style.Colors;
        
        var drawList = ImPlot.GetPlotDrawList();
        
        // Calculate box size — cache text sizes to avoid calling CalcTextSize twice per line
        var maxWidth = 0f;
        var totalHeight = 0f;
        Span<float> lineHeights = lines.Length <= 16 ? stackalloc float[lines.Length] : new float[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var size = ImGui.CalcTextSize(lines[i]);
            maxWidth = Math.Max(maxWidth, size.X);
            lineHeights[i] = size.Y;
            totalHeight += size.Y + 2f;
        }
        
        var padding = style.TooltipPadding;
        var boxWidth = maxWidth + padding * 2 + style.TooltipAccentWidth + 1; // +1 for accent bar spacing
        var boxHeight = totalHeight + padding * 2 - 2f;
        
        // Offset to not overlap cursor
        var boxPos = new Vector2(screenPos.X + style.TooltipOffsetX, screenPos.Y - boxHeight / 2);
        
        // Background
        drawList.AddRectFilled(
            boxPos,
            new Vector2(boxPos.X + boxWidth, boxPos.Y + boxHeight),
            ImGui.GetColorU32(colors.TooltipBackground), style.TooltipRounding);
        
        // Border
        drawList.AddRect(
            boxPos,
            new Vector2(boxPos.X + boxWidth, boxPos.Y + boxHeight),
            ImGui.GetColorU32(colors.TooltipBorder), style.TooltipRounding, 0, 1f);
        
        // Accent bar on left
        drawList.AddRectFilled(
            new Vector2(boxPos.X, boxPos.Y),
            new Vector2(boxPos.X + style.TooltipAccentWidth, boxPos.Y + boxHeight),
            ImGui.GetColorU32(accentColor), style.TooltipRounding);
        
        // Text
        var textY = boxPos.Y + padding;
        for (var i = 0; i < lines.Length; i++)
        {
            drawList.AddText(
                new Vector2(boxPos.X + padding + style.TooltipAccentWidth + 1, textY), 
                ImGui.GetColorU32(colors.TextPrimary), 
                lines[i]);
            textY += lineHeights[i] + 2f;
        }
    }
    
    /// <summary>
    /// Draws a simple single-line tooltip.
    /// </summary>
    /// <param name="screenPos">Screen position for the tooltip.</param>
    /// <param name="text">Text to display.</param>
    /// <param name="accentColor">Color for the accent bar.</param>
    /// <param name="style">Optional style configuration.</param>
    public static void DrawTooltip(Vector2 screenPos, string text, Vector4 accentColor, GraphStyleConfig? style = null)
    {
        DrawTooltipBox(screenPos, new[] { text }, accentColor, style);
    }
}
