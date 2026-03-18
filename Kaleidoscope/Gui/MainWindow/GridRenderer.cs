using System.Numerics;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Renders the grid overlay in edit mode.
/// Extracted from WindowContentContainer.Drawing.cs for single-responsibility.
/// </summary>
internal static class GridRenderer
{
    /// <summary>
    /// Draws the grid overlay with major (cell) and minor (subdivision) lines.
    /// Only called when edit mode is active.
    /// </summary>
    public static void DrawGrid(DrawContext ctx, LayoutGridSettings gridSettings)
    {
        try
        {
            var dl = ctx.DrawList;
            var subdivisions = Math.Max(1, gridSettings.Subdivisions);
            
            // Minor (subdivision) lines — very faint
            var minorColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.03f));
            // Major (cell) lines — slightly stronger
            var majorColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f));

            var subW = ctx.CellW / subdivisions;
            var subH = ctx.CellH / subdivisions;

            // Cap the number of lines to avoid heavy rendering
            const int MaxLines = ConfigStatic.MaxGridLines;

            // Vertical lines
            var totalV = ctx.EffectiveCols * subdivisions + 1;
            var vStep = 1;
            if (totalV > MaxLines) vStep = (int)MathF.Ceiling((float)totalV / MaxLines);
            var vx = ctx.ContentMin.X;
            for (var iV = 0; iV <= totalV; iV++, vx += subW)
            {
                if (iV % vStep != 0) continue;
                var isMajor = (iV % subdivisions == 0);
                dl.AddLine(new Vector2(vx, ctx.ContentMin.Y), new Vector2(vx, ctx.ContentMax.Y),
                    isMajor ? majorColor : minorColor, 1f);
            }

            // Horizontal lines
            var totalH = ctx.EffectiveRows * subdivisions + 1;
            var hStep = 1;
            if (totalH > MaxLines) hStep = (int)MathF.Ceiling((float)totalH / MaxLines);
            var hy = ctx.ContentMin.Y;
            for (var iH = 0; iH <= totalH; iH++, hy += subH)
            {
                if (iH % hStep != 0) continue;
                var isMajor = (iH % subdivisions == 0);
                dl.AddLine(new Vector2(ctx.ContentMin.X, hy), new Vector2(ctx.ContentMax.X, hy),
                    isMajor ? majorColor : minorColor, 1f);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"Grid drawing error: {ex.Message}");
        }
    }
}
