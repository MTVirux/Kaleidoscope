using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Kaleidoscope.Gui.Widgets.Common;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Static utilities for drawing value labels on ImPlot graphs.
/// </summary>
public static class GraphValueLabels
{
    /// <summary>
    /// Contains information about a rendered value label for hover detection.
    /// </summary>
    public readonly struct ValueLabelBounds
    {
        /// <summary>Series name for this label.</summary>
        public readonly string SeriesName;
        
        /// <summary>The displayed value.</summary>
        public readonly float Value;
        
        /// <summary>Series color.</summary>
        public readonly Vector3 Color;
        
        /// <summary>Minimum corner of the label bounding box.</summary>
        public readonly Vector2 BoundsMin;
        
        /// <summary>Maximum corner of the label bounding box.</summary>
        public readonly Vector2 BoundsMax;
        
        public ValueLabelBounds(string seriesName, float value, Vector3 color, Vector2 boundsMin, Vector2 boundsMax)
        {
            SeriesName = seriesName;
            Value = value;
            Color = color;
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
        }
        
        /// <summary>Checks if a point is within this label's bounds.</summary>
        public bool Contains(Vector2 point) =>
            point.X >= BoundsMin.X && point.X <= BoundsMax.X &&
            point.Y >= BoundsMin.Y && point.Y <= BoundsMax.Y;
    }
    
    /// <summary>
    /// Represents a positioned label for collision detection and rendering.
    /// </summary>
    private readonly struct PositionedLabel
    {
        public readonly string Text;
        public readonly string SeriesName;
        public readonly float Value;
        public readonly Vector3 Color;
        public readonly Vector2 DataPoint;
        public readonly Vector2 LabelSize;
        public readonly float LabelY;
        public readonly float OriginalY;
        
        public PositionedLabel(string text, float value, Vector3 color, Vector2 dataPoint, Vector2 labelSize, string seriesName)
        {
            Text = text;
            SeriesName = seriesName;
            Value = value;
            Color = color;
            DataPoint = dataPoint;
            LabelSize = labelSize;
            OriginalY = dataPoint.Y;
            LabelY = dataPoint.Y;
        }
        
        private PositionedLabel(string text, string seriesName, float value, Vector3 color, Vector2 dataPoint, Vector2 labelSize, float labelY, float originalY)
        {
            Text = text;
            SeriesName = seriesName;
            Value = value;
            Color = color;
            DataPoint = dataPoint;
            LabelSize = labelSize;
            LabelY = labelY;
            OriginalY = originalY;
        }
        
        public PositionedLabel WithLabelY(float newY) => new(Text, SeriesName, Value, Color, DataPoint, LabelSize, newY, OriginalY);
    }
    
    /// <summary>
    /// Draws current value labels at the latest point of each series.
    /// Labels are colored to match their series and auto-adjust to prevent overlap.
    /// Uses horizontal staggering when labels would overlap vertically.
    /// </summary>
    /// <param name="seriesData">List of series data containing name, last X/Y position, and color.</param>
    /// <param name="style">Optional style configuration.</param>
    /// <param name="offsetX">Additional horizontal offset for all labels. Default: 0</param>
    /// <param name="offsetY">Additional vertical offset for all labels. Default: 0</param>
    /// <param name="numberFormat">Optional number format configuration. If null, uses Compact format.</param>
    /// <returns>List of label bounds for hover detection.</returns>
    public static List<ValueLabelBounds> DrawCurrentValueLabels(IReadOnlyList<(string Name, double LastX, double LastY, Vector3 Color)> seriesData, GraphStyleConfig? style = null, float offsetX = 0f, float offsetY = 0f, NumberFormatConfig? numberFormat = null)
    {
        style ??= GraphStyleConfig.Default;
        numberFormat ??= NumberFormatConfig.Compact;
        
        if (seriesData.Count == 0)
            return new List<ValueLabelBounds>();
        
        var drawList = ImPlot.GetPlotDrawList();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        var plotLimits = ImPlot.GetPlotLimits();
        
        // Limit the number of visible labels to prevent excessive clutter
        var visibleCount = Math.Min(seriesData.Count, style.ValueLabelMaxVisible);
        
        // Sort by value for display priority (highest values get priority)
        // Build a reusable list and sort in-place to avoid LINQ allocations
        var sortedByValue = new List<(string Name, double LastX, double LastY, Vector3 Color)>(visibleCount);
        {
            // Copy all to a temp list, sort, then take top N
            var all = new List<(string Name, double LastX, double LastY, Vector3 Color)>(seriesData.Count);
            for (var i = 0; i < seriesData.Count; i++)
                all.Add(seriesData[i]);
            all.Sort((a, b) => b.LastY.CompareTo(a.LastY)); // descending
            var count = Math.Min(visibleCount, all.Count);
            for (var i = 0; i < count; i++)
                sortedByValue.Add(all[i]);
        }
        
        // Build positioned labels with pixel coordinates
        var labels = new List<PositionedLabel>();
        var padding = style.ValueLabelPadding;
        
        // Find the rightmost data point X position
        var maxDataX = float.MinValue;
        foreach (var (_, lastX, _, _) in sortedByValue)
        {
            var pixelX = ImPlot.PlotToPixels(lastX, 0).X;
            maxDataX = Math.Max(maxDataX, pixelX);
        }
        
        var rightEdge = plotPos.X + plotSize.X - 4f;
        var availableWidth = rightEdge - maxDataX - style.ValueLabelHorizontalOffset;
        
        foreach (var (name, lastX, lastY, color) in sortedByValue)
        {
            // Skip if the point is outside the visible plot area
            if (lastY < plotLimits.Y.Min || lastY > plotLimits.Y.Max)
                continue;
            
            var pixelPos = ImPlot.PlotToPixels(lastX, lastY);
            var valueText = NumberFormatter.Format((float)lastY, numberFormat);
            var labelSize = ImGui.CalcTextSize(valueText);
            
            labels.Add(new PositionedLabel(valueText, (float)lastY, color, pixelPos, labelSize, name));
        }
        
        if (labels.Count == 0)
            return new List<ValueLabelBounds>();
        
        // Sort by value descending (highest values first, will be placed at top)
        labels.Sort((a, b) => b.Value.CompareTo(a.Value));
        
        // Calculate label dimensions
        var labelHeight = labels[0].LabelSize.Y + padding * 2;
        var maxLabelWidth = 0f;
        foreach (var l in labels)
        {
            var w = l.LabelSize.X + padding * 2;
            if (w > maxLabelWidth) maxLabelWidth = w;
        }
        
        // Assign X positions using horizontal staggering for overlapping labels
        // Labels are placed top-to-bottom in value order (highest at top)
        // Apply offsetX to the starting position
        var labelPositions = AssignLabelPositions(labels, padding, style.ValueLabelMinSpacing, 
            maxDataX + style.ValueLabelHorizontalOffset + offsetX, rightEdge, maxLabelWidth,
            plotPos.Y + offsetY, plotPos.Y + plotSize.Y,
            style.ValueLabelStepsPerRow);
        
        // Sort by value ascending so higher values are drawn last (on top)
        labelPositions.Sort((a, b) => a.Label.Value.CompareTo(b.Label.Value));
        var sortedForDrawing = labelPositions;
        
        // Collect bounds for hover detection
        var labelBounds = new List<ValueLabelBounds>();
        
        // Clip drawing to the plot area
        drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
        
        // First pass: draw all connecting lines (underneath labels)
        foreach (var (label, labelX, labelY) in sortedForDrawing)
        {
            var thisLabelHeight = label.LabelSize.Y + padding * 2;
            var labelWidth = label.LabelSize.X + padding * 2;
            
            var labelPos = new Vector2(labelX, labelY - thisLabelHeight / 2);
            var bgMin = labelPos;
            
            // Draw connecting line from data point to label
            var lineColor = ImGui.GetColorU32(new Vector4(label.Color.X, label.Color.Y, label.Color.Z, style.ValueLabelLineAlpha));
            var lineStart = label.DataPoint;
            var lineEnd = new Vector2(bgMin.X, labelY);
            
            // Only draw line if there's meaningful distance
            if (lineEnd.X - lineStart.X > 4f || Math.Abs(labelY - label.OriginalY) > 2f)
            {
                drawList.AddLine(lineStart, lineEnd, lineColor, style.ValueLabelLineThickness);
            }
        }
        
        // Second pass: draw all label boxes (on top of lines)
        foreach (var (label, labelX, labelY) in sortedForDrawing)
        {
            var thisLabelHeight = label.LabelSize.Y + padding * 2;
            var labelWidth = label.LabelSize.X + padding * 2;
            
            var labelPos = new Vector2(labelX, labelY - thisLabelHeight / 2);
            var bgMin = labelPos;
            var bgMax = new Vector2(labelPos.X + labelWidth, labelPos.Y + thisLabelHeight);
            
            // Background with series-tinted color
            var bgColor = new Vector4(
                label.Color.X * 0.15f + 0.05f,
                label.Color.Y * 0.15f + 0.05f,
                label.Color.Z * 0.15f + 0.05f,
                style.ValueLabelBackgroundAlpha
            );
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(bgColor), style.ValueLabelRounding);
            
            // Border in series color
            var borderColor = new Vector4(label.Color.X, label.Color.Y, label.Color.Z, style.ValueLabelBorderAlpha);
            drawList.AddRect(bgMin, bgMax, ImGui.GetColorU32(borderColor), style.ValueLabelRounding, 0, style.ValueLabelBorderThickness);
            
            // Text in series color
            var textColor = new Vector4(label.Color.X, label.Color.Y, label.Color.Z, 1f);
            var textPos = new Vector2(labelPos.X + padding, labelPos.Y + padding);
            drawList.AddText(textPos, ImGui.GetColorU32(textColor), label.Text);
            
            // Collect bounds for hover detection
            labelBounds.Add(new ValueLabelBounds(label.SeriesName, label.Value, label.Color, bgMin, bgMax));
        }
        
        drawList.PopClipRect();
        
        return labelBounds;
    }
    
    /// <summary>
    /// Assigns X and Y positions to labels using a stair pattern.
    /// The highest value label is placed at row 0, column 0.
    /// Subsequent labels descend in a stair pattern: each step down moves a fraction of a row
    /// (controlled by stepsPerRow) and cycles through columns.
    /// </summary>
    private static List<(PositionedLabel Label, float X, float Y)> AssignLabelPositions(
        List<PositionedLabel> labels,
        float padding,
        float minSpacing,
        float startX,
        float rightEdge,
        float maxLabelWidth,
        float minY,
        float maxY,
        int stepsPerRow)
    {
        if (labels.Count == 0)
            return new List<(PositionedLabel, float, float)>();
        
        var result = new List<(PositionedLabel Label, float X, float Y)>();
        
        // Find the highest value label (first in the list since sorted by value desc)
        var highestLabel = labels[0];
        var highestLabelHeight = highestLabel.LabelSize.Y + padding * 2;
        
        // Place highest label at its original Y position (matching data point)
        var highestY = highestLabel.OriginalY;
        highestY = Math.Max(minY + highestLabelHeight / 2, Math.Min(highestY, maxY - highestLabelHeight / 2));
        
        // Calculate grid dimensions
        var labelHeight = highestLabelHeight;
        var columnWidth = maxLabelWidth + 5f;
        
        // Calculate available columns
        var numColumns = Math.Max(1, (int)((rightEdge - startX) / columnWidth));
        
        // If only one label, just place it and return
        if (labels.Count == 1)
        {
            result.Add((highestLabel, startX, highestY));
            return result;
        }
        
        // Calculate step height for stair pattern
        // stepsPerRow controls how many labels fit in one row height of vertical space
        // stepHeight is always based on stepsPerRow to maintain consistent stair slope
        var baseRowHeight = labelHeight + minSpacing;
        var stepHeight = baseRowHeight / stepsPerRow;
        
        // Place highest label at column 0, row 0
        result.Add((highestLabel, startX, highestY));
        
        // Calculate starting Y for stair pattern (one label height below highest, plus a small margin)
        var stairStartY = highestY + labelHeight + (labelHeight * 0.3f);
        
        // Track the last Y position for each column (to handle wrapping with proper spacing)
        var columnLastY = new float[numColumns];
        var columnRowCount = new int[numColumns]; // Track how many labels are in each column
        for (var c = 0; c < numColumns; c++)
        {
            columnLastY[c] = float.MinValue;
            columnRowCount[c] = 0;
        }
        
        // Place all remaining labels in stair pattern
        var currentCol = 0;
        var currentStairY = stairStartY;
        
        for (var i = 1; i < labels.Count; i++)
        {
            var label = labels[i];
            
            // Center the label within its cell if it's narrower than the max width
            var thisLabelWidth = label.LabelSize.X + padding * 2;
            var centeringOffset = (maxLabelWidth - thisLabelWidth) / 2;
            
            // Add horizontal offset on alternating rows (0.3f of label width)
            var rowInColumn = columnRowCount[currentCol];
            var alternatingOffset = (rowInColumn % 2 == 1) ? (maxLabelWidth * 0.08f) : 0f;
            var currentX = startX + currentCol * columnWidth + alternatingOffset + centeringOffset;
            
            // Check if this column already has a label - if so, ensure 1 label height spacing
            float labelY;
            if (columnLastY[currentCol] > float.MinValue)
            {
                // Place 1 label heights below the bottom of the previous label in this column
                var previousBottom = columnLastY[currentCol] + labelHeight / 2;
                labelY = previousBottom + (labelHeight * 1f);
            }
            else
            {
                // First label in this column - use stair pattern position
                labelY = currentStairY;
            }
            
            // Skip labels that would go below the plot area (maxY is the bottom in screen coordinates)
            if (labelY + labelHeight / 2 > maxY)
            {
                currentCol++;
                currentStairY += stepHeight;
                if (currentCol >= numColumns)
                {
                    currentCol = 0;
                }
                continue;
            }
            
            result.Add((label, currentX, labelY));
            columnLastY[currentCol] = labelY;
            columnRowCount[currentCol]++;
            
            currentCol++;
            currentStairY += stepHeight;
            
            if (currentCol >= numColumns)
            {
                currentCol = 0;
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Draws a value label at the end of a series line.
    /// </summary>
    /// <param name="plotX">X position in plot coordinates.</param>
    /// <param name="plotY">Y position in plot coordinates.</param>
    /// <param name="value">The value to display.</param>
    /// <param name="color">The series color (RGB).</param>
    /// <param name="offsetX">Horizontal offset from the point. Default: 0</param>
    /// <param name="offsetY">Vertical offset from the point. Default: 0</param>
    /// <param name="style">Optional style configuration.</param>
    public static void DrawValueLabel(double plotX, double plotY, float value, Vector3 color, float offsetX = 0f, float offsetY = 0f, GraphStyleConfig? style = null)
    {
        style ??= GraphStyleConfig.Default;
        
        var drawList = ImPlot.GetPlotDrawList();
        var pixelPos = ImPlot.PlotToPixels(plotX, plotY);
        
        // Apply offset
        var labelPos = new Vector2(pixelPos.X + offsetX, pixelPos.Y + offsetY);
        
        var label = NumberFormatter.FormatCompact(value);
        var labelSize = ImGui.CalcTextSize(label);
        
        var padding = style.ValueLabelPadding;
        var bgMin = new Vector2(labelPos.X - padding, labelPos.Y - labelSize.Y / 2 - padding);
        var bgMax = new Vector2(labelPos.X + labelSize.X + padding, labelPos.Y + labelSize.Y / 2 + padding);
        
        // Draw connecting line from point to label
        var lineColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.6f));
        drawList.AddLine(pixelPos, new Vector2(labelPos.X, labelPos.Y), lineColor, style.ValueLabelLineThickness);
        
        // Background
        var bgColor = new Vector4(color.X * 0.3f, color.Y * 0.3f, color.Z * 0.3f, 0.9f);
        drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(bgColor), style.ValueLabelRounding);
        
        // Border with series color
        var borderColor = new Vector4(color.X, color.Y, color.Z, 0.8f);
        drawList.AddRect(bgMin, bgMax, ImGui.GetColorU32(borderColor), style.ValueLabelRounding);
        
        // Text in series color
        var textColor = new Vector4(color.X, color.Y, color.Z, 1f);
        var textPos = new Vector2(labelPos.X, labelPos.Y - labelSize.Y / 2);
        drawList.AddText(textPos, ImGui.GetColorU32(textColor), label);
    }
}
