using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Kaleidoscope.Gui.Widgets.Common;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Static drawing utilities for ImPlot graphs.
/// Provides methods for drawing crosshairs, price lines, and axis formatters.
/// For tooltips, see <see cref="GraphTooltips"/>.
/// For value labels, see <see cref="GraphValueLabels"/>.
/// </summary>
public static class GraphDrawing
{
    #region Axis Formatters
    
    /// <summary>
    /// Formatter delegate for X-axis tick labels with time values.
    /// Shows only time (HH:mm) if all visible labels are on the same day, otherwise date+time (M/d HH:mm).
    /// </summary>
    /// <remarks>
    /// The userData pointer should contain the start time ticks (long cast to void*).
    /// Uses stackalloc + TryFormat to avoid per-tick string allocations.
    /// </remarks>
    public static readonly unsafe ImPlotFormatter XAxisTimeFormatter = (double value, byte* buff, int size, void* userData) =>
    {
        // value is seconds from the start time, userData contains the start time ticks
        var startTicks = (long)userData;
        var startTime = new DateTime(startTicks);
        var time = startTime.AddSeconds(value).ToLocalTime();
        
        // Check if the visible X-axis range spans a single day
        var plotLimits = ImPlot.GetPlotLimits();
        var visibleMinTime = startTime.AddSeconds(plotLimits.X.Min).ToLocalTime();
        var visibleMaxTime = startTime.AddSeconds(plotLimits.X.Max).ToLocalTime();
        var isSameDay = visibleMinTime.Date == visibleMaxTime.Date;
        
        // Use TryFormat with stackalloc to avoid string allocation
        ReadOnlySpan<char> format = isSameDay ? "HH:mm" : "M/d HH:mm";
        Span<char> charBuf = stackalloc char[32];
        if (time.TryFormat(charBuf, out var charsWritten, format))
        {
            var len = Math.Min(charsWritten, size - 1);
            for (var i = 0; i < len; i++)
                buff[i] = (byte)charBuf[i];
            buff[len] = 0;
            return len;
        }
        
        // Fallback if TryFormat fails
        buff[0] = (byte)'?';
        buff[1] = 0;
        return 1;
    };
    
    /// <summary>
    /// Formatter delegate for Y-axis tick labels with abbreviated notation (K, M, B).
    /// </summary>
    public static readonly unsafe ImPlotFormatter YAxisFormatter = (double value, byte* buff, int size, void* userData) =>
    {
        var formatted = NumberFormatter.FormatCompact(value);
        var len = Math.Min(formatted.Length, size - 1);
        for (var i = 0; i < len; i++)
            buff[i] = (byte)formatted[i];
        buff[len] = 0;
        return len;
    };
    
    #endregion
    
    #region Crosshair Drawing
    
    /// <summary>
    /// Draws crosshair lines at the current mouse position.
    /// </summary>
    /// <param name="mouseX">Mouse X position in plot coordinates.</param>
    /// <param name="mouseY">Mouse Y position in plot coordinates.</param>
    /// <param name="valueAtMouse">The Y value to display in the label.</param>
    /// <param name="style">Optional style configuration.</param>
    /// <param name="numberFormat">Optional number format configuration. If null, uses Compact format.</param>
    public static void DrawCrosshair(double mouseX, double mouseY, float valueAtMouse, GraphStyleConfig? style = null, NumberFormatConfig? numberFormat = null)
    {
        style ??= GraphStyleConfig.Default;
        numberFormat ??= NumberFormatConfig.Compact;
        var colors = style.Colors;
        
        var drawList = ImPlot.GetPlotDrawList();
        var plotLimits = ImPlot.GetPlotLimits();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        
        var colorU32 = ImGui.GetColorU32(colors.Crosshair);
        
        // Vertical line
        var vTop = ImPlot.PlotToPixels(mouseX, plotLimits.Y.Max);
        var vBottom = ImPlot.PlotToPixels(mouseX, plotLimits.Y.Min);
        
        // Draw dashed vertical line
        var dashLength = style.CrosshairDashLength;
        var gapLength = style.CrosshairGapLength;
        var y = vTop.Y;
        while (y < vBottom.Y)
        {
            var endY = Math.Min(y + dashLength, vBottom.Y);
            drawList.AddLine(new Vector2(vTop.X, y), new Vector2(vTop.X, endY), colorU32, style.CrosshairThickness);
            y += dashLength + gapLength;
        }
        
        // Horizontal line
        var hLeft = ImPlot.PlotToPixels(plotLimits.X.Min, mouseY);
        var hRight = ImPlot.PlotToPixels(plotLimits.X.Max, mouseY);
        
        // Draw dashed horizontal line
        var x = hLeft.X;
        while (x < hRight.X)
        {
            var endX = Math.Min(x + dashLength, hRight.X);
            drawList.AddLine(new Vector2(x, hLeft.Y), new Vector2(endX, hLeft.Y), colorU32, style.CrosshairThickness);
            x += dashLength + gapLength;
        }
        
        // Draw value label on Y axis (clipped to plot area so it goes under the Y-axis)
        var valueLabel = NumberFormatter.Format(valueAtMouse, numberFormat);
        var labelSize = ImGui.CalcTextSize(valueLabel);
        var labelPos = new Vector2(hRight.X - labelSize.X - 6, hRight.Y - labelSize.Y / 2);
        
        // Background box - clipped to plot area
        var bgPadding = 3f;
        drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
        drawList.AddRectFilled(
            new Vector2(labelPos.X - bgPadding, labelPos.Y - bgPadding),
            new Vector2(labelPos.X + labelSize.X + bgPadding, labelPos.Y + labelSize.Y + bgPadding),
            ImGui.GetColorU32(colors.TooltipBackground), 2f);
        drawList.AddRect(
            new Vector2(labelPos.X - bgPadding, labelPos.Y - bgPadding),
            new Vector2(labelPos.X + labelSize.X + bgPadding, labelPos.Y + labelSize.Y + bgPadding),
            ImGui.GetColorU32(colors.TooltipBorder), 2f);
        
        drawList.AddText(labelPos, ImGui.GetColorU32(colors.TextPrimary), valueLabel);
        drawList.PopClipRect();
    }
    
    #endregion
    
    #region Price Line Drawing
    
    /// <summary>
    /// Draws a horizontal price level line with label (trading platform style).
    /// </summary>
    /// <param name="yValue">The Y value (in plot coordinates) where the line should be drawn.</param>
    /// <param name="label">Optional label text to display on the line.</param>
    /// <param name="color">Line and label background color.</param>
    /// <param name="thickness">Line thickness. Default: 1</param>
    /// <param name="dashed">Whether to draw a dashed line. Default: false</param>
    /// <param name="style">Optional style configuration.</param>
    public static void DrawPriceLine(double yValue, string label, Vector4 color, float thickness = 1f, bool dashed = false, GraphStyleConfig? style = null)
    {
        style ??= GraphStyleConfig.Default;
        
        var drawList = ImPlot.GetPlotDrawList();
        var plotLimits = ImPlot.GetPlotLimits();
        
        // Get pixel positions
        var p1 = ImPlot.PlotToPixels(plotLimits.X.Min, yValue);
        var p2 = ImPlot.PlotToPixels(plotLimits.X.Max, yValue);
        
        var colorU32 = ImGui.GetColorU32(color);
        
        if (dashed)
        {
            // Draw dashed line
            var dashLength = style.PriceLineDashLength;
            var gapLength = style.PriceLineGapLength;
            var x = p1.X;
            while (x < p2.X)
            {
                var endX = Math.Min(x + dashLength, p2.X);
                drawList.AddLine(new Vector2(x, p1.Y), new Vector2(endX, p1.Y), colorU32, thickness);
                x += dashLength + gapLength;
            }
        }
        else
        {
            drawList.AddLine(p1, p2, colorU32, thickness);
        }
        
        // Draw price label on the right (clipped to plot area so it goes under the Y-axis)
        if (!string.IsNullOrEmpty(label))
        {
            var plotPos = ImPlot.GetPlotPos();
            var plotSize = ImPlot.GetPlotSize();
            var labelSize = ImGui.CalcTextSize(label);
            var labelPos = new Vector2(p2.X - labelSize.X - 4, p2.Y - labelSize.Y / 2);
            
            // Background for label
            var bgMin = new Vector2(labelPos.X - 4, labelPos.Y - 2);
            var bgMax = new Vector2(p2.X, labelPos.Y + labelSize.Y + 2);
            
            // Clip to plot area so label goes under the axis
            drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.85f)), 2f);
            drawList.AddText(labelPos, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)), label);
            drawList.PopClipRect();
        }
    }
    
    /// <summary>
    /// Draws a current price line using the default style.
    /// </summary>
    /// <param name="yValue">The Y value (in plot coordinates) where the line should be drawn.</param>
    /// <param name="style">Optional style configuration.</param>
    /// <param name="numberFormat">Optional number format configuration.</param>
    public static void DrawCurrentPriceLine(double yValue, GraphStyleConfig? style = null, NumberFormatConfig? numberFormat = null)
    {
        style ??= GraphStyleConfig.Default;
        var colors = style.Colors;
        var label = NumberFormatter.Format(yValue, numberFormat ?? NumberFormatConfig.Compact);
        DrawPriceLine(yValue, label, colors.CurrentPriceLine, style.PriceLineThickness, dashed: true, style);
    }
    
    #endregion
}
