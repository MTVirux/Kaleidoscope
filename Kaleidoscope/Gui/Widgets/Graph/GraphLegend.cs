using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Kaleidoscope.Gui.Widgets.Common;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Legend rendering utilities for ImPlot graphs.
/// Provides methods for drawing scrollable legends both inside and outside the plot area.
/// </summary>
public static class GraphLegend
{
    // Reusable sort buffer to avoid per-frame allocations
    [ThreadStatic] private static List<GraphSeriesData>? _sortBuffer;
    
    private static int CompareByLastValueDescending(GraphSeriesData a, GraphSeriesData b)
    {
        var va = a.PointCount > 0 ? a.YValues[a.PointCount - 1] : 0.0;
        var vb = b.PointCount > 0 ? b.YValues[b.PointCount - 1] : 0.0;
        return vb.CompareTo(va); // descending
    }
    
    private static List<GraphSeriesData> GetSortedSeries(IReadOnlyList<GraphSeriesData> series)
    {
        _sortBuffer ??= new List<GraphSeriesData>();
        _sortBuffer.Clear();
        for (var i = 0; i < series.Count; i++)
            _sortBuffer.Add(series[i]);
        _sortBuffer.Sort(CompareByLastValueDescending);
        return _sortBuffer;
    }
    
    #region Helper Methods

    /// <summary>
    /// Checks if a series is hidden via any of its groups.
    /// </summary>
    private static bool IsSeriesHiddenViaGroup(GraphSeriesData series, HashSet<string> hiddenGroups)
    {
        if (series.GroupNames is not { Count: > 0 })
            return false;

        foreach (var groupName in series.GroupNames)
        {
            if (hiddenGroups.Contains(groupName))
                return true;
        }

        return false;
    }

    #endregion

    #region Inside Legend (Plot Draw List)
    
    /// <summary>
    /// Result from drawing an inside legend, containing bounds for input blocking.
    /// </summary>
    public readonly struct InsideLegendResult
    {
        /// <summary>Overlay region covering the legend bounds, used for input blocking.</summary>
        public readonly OverlayRegion Region;

        /// <summary>Updated scroll offset for the legend.</summary>
        public readonly float ScrollOffset;

        /// <summary>Whether the collapse toggle was clicked.</summary>
        public readonly bool CollapseToggled;

        public InsideLegendResult(Vector2 boundsMin, Vector2 boundsMax, float scrollOffset, bool collapseToggled = false)
        {
            Region = new OverlayRegion(boundsMin, boundsMax);
            ScrollOffset = scrollOffset;
            CollapseToggled = collapseToggled;
        }

        public static readonly InsideLegendResult Invalid = default;
    }
    
    /// <summary>
    /// Draws an interactive legend inside the plot area using ImPlot's draw list.
    /// Supports both series and group toggling, and collapsible mode.
    /// </summary>
    /// <param name="data">Prepared graph data containing series and groups.</param>
    /// <param name="hiddenSeries">Set of hidden series names.</param>
    /// <param name="hiddenGroups">Set of hidden group names.</param>
    /// <param name="position">Legend position within the plot.</param>
    /// <param name="legendHeightPercent">Maximum legend height as percentage of plot height (10-80).</param>
    /// <param name="scrollOffset">Current scroll offset (pass result back on next frame).</param>
    /// <param name="isCollapsed">Whether the legend is collapsed.</param>
    /// <param name="onToggleSeries">Callback when a series visibility is toggled.</param>
    /// <param name="onToggleGroup">Callback when a group visibility is toggled.</param>
    /// <param name="onToggleCollapse">Callback when the collapse state is toggled.</param>
    /// <param name="style">Optional style configuration.</param>
    /// <returns>Result containing legend bounds and updated scroll offset.</returns>
    public static InsideLegendResult DrawInsideLegend(
        PreparedGraphData data,
        HashSet<string> hiddenSeries,
        HashSet<string>? hiddenGroups = null,
        LegendPosition position = LegendPosition.InsideTopRight,
        float legendHeightPercent = 50f,
        float scrollOffset = 0f,
        bool isCollapsed = false,
        Action<string>? onToggleSeries = null,
        Action<string>? onToggleGroup = null,
        Action? onToggleCollapse = null,
        GraphStyleConfig? style = null)
    {
        style ??= GraphStyleConfig.Default;
        hiddenGroups ??= new HashSet<string>();
        var colors = style.Colors;
        
        var drawList = ImPlot.GetPlotDrawList();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        
        // Calculate legend dimensions
        var padding = style.LegendPadding;
        var indicatorSize = style.LegendIndicatorSize;
        var rowHeight = style.LegendRowHeight;
        var scrollbarWidth = style.LegendScrollbarWidth;
        var indicatorTextGap = style.LegendIndicatorTextGap;
        var separatorHeight = style.LegendSeparatorHeight;
        
        // Count groups and series for layout
        var groupCount = data.Groups?.Count ?? 0;
        var hasGroups = groupCount > 0;
        
        // Measure max text width across both groups and series
        var maxTextWidth = 0f;
        var validSeriesCount = 0;
        
        if (data.Groups != null)
        {
            foreach (var group in data.Groups)
            {
                var textSize = ImGui.CalcTextSize($"[{group.Name}]");
                maxTextWidth = Math.Max(maxTextWidth, textSize.X);
            }
        }
        
        foreach (var series in data.Series)
        {
            var textSize = ImGui.CalcTextSize(series.Name);
            maxTextWidth = Math.Max(maxTextWidth, textSize.X);
            validSeriesCount++;
        }
        
        if (validSeriesCount == 0 && groupCount == 0) 
            return InsideLegendResult.Invalid;
        
        // Track if collapse was toggled
        var collapseToggled = false;
        
        // Handle collapsed state - show small toggle button
        if (isCollapsed)
        {
            var collapsedSize = 24f;
            var collapsedMargin = style.LegendMargin;
            
            // Position the collapsed button based on legend position preference
            Vector2 collapsedPos = position switch
            {
                LegendPosition.InsideTopRight => new Vector2(plotPos.X + plotSize.X - collapsedSize - collapsedMargin, plotPos.Y + collapsedMargin),
                LegendPosition.InsideBottomLeft => new Vector2(plotPos.X + collapsedMargin, plotPos.Y + plotSize.Y - collapsedSize - collapsedMargin),
                LegendPosition.InsideBottomRight => new Vector2(plotPos.X + plotSize.X - collapsedSize - collapsedMargin, plotPos.Y + plotSize.Y - collapsedSize - collapsedMargin),
                _ => new Vector2(plotPos.X + collapsedMargin, plotPos.Y + collapsedMargin)
            };
            
            var collapsedBgColor = ImGui.GetColorU32(new Vector4(colors.FrameBackground.X, colors.FrameBackground.Y, colors.FrameBackground.Z, 0.85f));
            var collapsedBorderColor = ImGui.GetColorU32(colors.AxisLine);
            
            drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
            drawList.AddRectFilled(collapsedPos, new Vector2(collapsedPos.X + collapsedSize, collapsedPos.Y + collapsedSize), collapsedBgColor, style.LegendRounding);
            drawList.AddRect(collapsedPos, new Vector2(collapsedPos.X + collapsedSize, collapsedPos.Y + collapsedSize), collapsedBorderColor, style.LegendRounding);
            
            // Draw expand icon (▼) - down arrow when collapsed, centered with reduced font
            var iconText = "▼";
            ImGui.SetWindowFontScale(0.75f);
            var iconTextSize = ImGui.CalcTextSize(iconText);
            var iconPos = new Vector2(
                collapsedPos.X + (collapsedSize - iconTextSize.X) / 2f,
                collapsedPos.Y + (collapsedSize - iconTextSize.Y) / 2f);
            drawList.AddText(iconPos, ImGui.GetColorU32(colors.TextSecondary), iconText);
            ImGui.SetWindowFontScale(1.0f);
            
            // Handle click to expand
            var collapsedMousePos = ImGui.GetMousePos();
            var mouseInButton = GraphOverlay.RectContains(collapsedPos,
                new Vector2(collapsedPos.X + collapsedSize, collapsedPos.Y + collapsedSize), collapsedMousePos);

            if (mouseInButton && GraphOverlay.CanProcessInteraction())
            {
                // Show tooltip
                ImGui.SetTooltip("Expand legend");
                
                if (ImGui.IsMouseClicked(0))
                {
                    onToggleCollapse?.Invoke();
                    collapseToggled = true;
                }
            }
            
            drawList.PopClipRect();
            
            return new InsideLegendResult(collapsedPos, new Vector2(collapsedPos.X + collapsedSize, collapsedPos.Y + collapsedSize), scrollOffset, collapseToggled);
        }
        
        // Calculate content height (groups + separator + series + header for collapse toggle)
        // Header row uses a smaller height for the collapse toggle
        var headerRowHeight = 18f;
        var headerHeight = onToggleCollapse != null ? headerRowHeight : 0f;
        var contentHeight = (groupCount + validSeriesCount) * rowHeight + headerHeight;
        if (hasGroups)
            contentHeight += separatorHeight;
            
        var maxLegendHeight = plotSize.Y * (legendHeightPercent / 100f);
        maxLegendHeight = Math.Max(maxLegendHeight, rowHeight + padding * 2);
        var needsScrolling = contentHeight > maxLegendHeight - padding * 2;
        
        var legendWidth = padding * 2 + indicatorSize + indicatorTextGap + maxTextWidth + (needsScrolling ? scrollbarWidth + 4f : 0f);
        var legendHeight = Math.Min(padding * 2 + contentHeight, maxLegendHeight);
        
        // Clamp legend dimensions to fit within plot area with margin
        var legendMargin = style.LegendMargin;
        var maxLegendWidth = plotSize.X - legendMargin * 2;
        legendWidth = Math.Min(legendWidth, Math.Max(50f, maxLegendWidth));
        
        // Determine legend position
        Vector2 legendPos = position switch
        {
            LegendPosition.InsideTopRight => new Vector2(plotPos.X + plotSize.X - legendWidth - legendMargin, plotPos.Y + legendMargin),
            LegendPosition.InsideBottomLeft => new Vector2(plotPos.X + legendMargin, plotPos.Y + plotSize.Y - legendHeight - legendMargin),
            LegendPosition.InsideBottomRight => new Vector2(plotPos.X + plotSize.X - legendWidth - legendMargin, plotPos.Y + plotSize.Y - legendHeight - legendMargin),
            _ => new Vector2(plotPos.X + legendMargin, plotPos.Y + legendMargin)
        };
        
        legendPos.X = Math.Clamp(legendPos.X, plotPos.X + legendMargin, plotPos.X + plotSize.X - legendWidth - legendMargin);
        legendPos.Y = Math.Clamp(legendPos.Y, plotPos.Y + legendMargin, plotPos.Y + plotSize.Y - legendHeight - legendMargin);
        
        drawList.PushClipRect(plotPos, new Vector2(plotPos.X + plotSize.X, plotPos.Y + plotSize.Y), true);
        
        // Draw legend background
        var bgColor = ImGui.GetColorU32(new Vector4(colors.FrameBackground.X, colors.FrameBackground.Y, colors.FrameBackground.Z, 0.85f));
        var borderColor = ImGui.GetColorU32(colors.AxisLine);
        drawList.AddRectFilled(legendPos, new Vector2(legendPos.X + legendWidth, legendPos.Y + legendHeight), bgColor, style.LegendRounding);
        drawList.AddRect(legendPos, new Vector2(legendPos.X + legendWidth, legendPos.Y + legendHeight), borderColor, style.LegendRounding);
        
        // Track mouse interactions
        var mousePos = ImGui.GetMousePos();
        var mouseInLegend = GraphOverlay.RectContains(legendPos,
            new Vector2(legendPos.X + legendWidth, legendPos.Y + legendHeight), mousePos);

        // Handle mouse wheel scrolling (only if no other window is blocking)
        if (mouseInLegend && needsScrolling && GraphOverlay.CanProcessInteraction())
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0)
            {
                scrollOffset -= wheel * rowHeight * 2f;
            }
        }
        
        var maxScrollOffset = Math.Max(0f, contentHeight - (legendHeight - padding * 2));
        scrollOffset = Math.Clamp(scrollOffset, 0f, maxScrollOffset);
        
        // Calculate visible area
        var contentAreaTop = legendPos.Y + padding;
        var contentAreaBottom = legendPos.Y + legendHeight - padding;
        var contentAreaRight = legendPos.X + legendWidth - padding - (needsScrolling ? scrollbarWidth + 4f : 0f);
        
        var yOffset = contentAreaTop - scrollOffset;
        
        // Track which item is being hovered for tooltip
        string? hoveredItemName = null;
        bool hoveredIsGroup = false;
        
        // Draw collapse toggle header if callback is provided
        if (onToggleCollapse != null)
        {
            var headerRowTop = yOffset;
            var headerRowBottom = yOffset + headerRowHeight;
            
            if (headerRowBottom >= contentAreaTop && headerRowTop <= contentAreaBottom)
            {
                var mouseInHeader = mouseInLegend &&
                                   mousePos.X >= legendPos.X + padding &&
                                   mousePos.X <= contentAreaRight &&
                                   mousePos.Y >= Math.Max(headerRowTop, contentAreaTop) &&
                                   mousePos.Y < Math.Min(headerRowBottom, contentAreaBottom);
                
                if (mouseInHeader && GraphOverlay.CanProcessInteraction())
                {
                    // Highlight on hover
                    var hoverColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.1f));
                    drawList.AddRectFilled(
                        new Vector2(legendPos.X + 2, Math.Max(headerRowTop, contentAreaTop)),
                        new Vector2(contentAreaRight, Math.Min(headerRowBottom, contentAreaBottom)),
                        hoverColor, 2f);
                    
                    ImGui.SetTooltip("Collapse legend");
                    
                    if (ImGui.IsMouseClicked(0))
                    {
                        onToggleCollapse.Invoke();
                        collapseToggled = true;
                    }
                }
                
                // Draw collapse icon (▲) - up arrow when expanded, aligned with series indicators
                var iconText = "▲";
                ImGui.SetWindowFontScale(0.75f);
                var iconTextSize = ImGui.CalcTextSize(iconText);
                // Align X with series indicators (legendPos.X + padding), center within indicatorSize width
                var iconX = legendPos.X + padding + (indicatorSize - iconTextSize.X) / 2f;
                var iconY = yOffset + (headerRowHeight - iconTextSize.Y) / 2f;
                var iconPos = new Vector2(iconX, iconY);
                drawList.AddText(iconPos, ImGui.GetColorU32(colors.TextSecondary), iconText);
                ImGui.SetWindowFontScale(1.0f);
                
                // Draw separator line under header
                var separatorY = headerRowBottom - 1f;
                if (separatorY >= contentAreaTop && separatorY <= contentAreaBottom)
                {
                    var separatorColor = ImGui.GetColorU32(colors.GridLine);
                    drawList.AddLine(
                        new Vector2(legendPos.X + padding, separatorY),
                        new Vector2(contentAreaRight, separatorY),
                        separatorColor);
                }
            }
            
            yOffset += headerRowHeight;
        }
        
        // Draw groups first
        if (data.Groups != null)
        {
            foreach (var group in data.Groups)
            {
                var rowTop = yOffset;
                var rowBottom = yOffset + rowHeight;
                
                if (rowBottom >= contentAreaTop && rowTop <= contentAreaBottom)
                {
                    var isHidden = hiddenGroups.Contains(group.Name);
                    var displayAlpha = isHidden ? style.LegendHiddenAlpha : 1f;
                    
                    var mouseInRow = mouseInLegend && 
                                    mousePos.X <= contentAreaRight &&
                                    mousePos.Y >= Math.Max(rowTop, contentAreaTop) && 
                                    mousePos.Y < Math.Min(rowBottom, contentAreaBottom) &&
                                    rowTop >= contentAreaTop && rowBottom <= contentAreaBottom;
                    
                    if (mouseInRow && GraphOverlay.CanProcessInteraction() && ImGui.IsMouseClicked(0))
                    {
                        onToggleGroup?.Invoke(group.Name);
                    }
                    
                    if (mouseInRow && GraphOverlay.CanProcessInteraction())
                    {
                        hoveredItemName = group.Name;
                        hoveredIsGroup = true;
                        var hoverColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.1f));
                        drawList.AddRectFilled(
                            new Vector2(legendPos.X + 2, Math.Max(rowTop, contentAreaTop)), 
                            new Vector2(contentAreaRight, Math.Min(rowBottom, contentAreaBottom)), 
                            hoverColor, 2f);
                    }
                    
                    var indicatorY = yOffset + (rowHeight - indicatorSize) / 2;
                    var indicatorPos = new Vector2(legendPos.X + padding, indicatorY);
                    var colorU32 = ImGui.GetColorU32(new Vector4(group.Color.X, group.Color.Y, group.Color.Z, displayAlpha));
                    
                    if (isHidden)
                    {
                        drawList.AddRect(indicatorPos, new Vector2(indicatorPos.X + indicatorSize, indicatorPos.Y + indicatorSize), colorU32, style.LegendGroupIndicatorRounding, ImDrawFlags.None, style.LegendIndicatorBorderThickness);
                    }
                    else
                    {
                        drawList.AddRectFilled(indicatorPos, new Vector2(indicatorPos.X + indicatorSize, indicatorPos.Y + indicatorSize), colorU32, style.LegendGroupIndicatorRounding);
                    }

                    var textColor = isHidden ? colors.TextSecondary : colors.TextPrimary;
                    var textY = yOffset + (rowHeight - ImGui.GetTextLineHeight()) / 2;
                    var textPos = new Vector2(indicatorPos.X + indicatorSize + indicatorTextGap, textY);
                    drawList.AddText(textPos, ImGui.GetColorU32(textColor), $"[{group.Name}]");
                }
                
                yOffset += rowHeight;
            }
            
            // Draw separator
            var separatorY = yOffset + separatorHeight / 2;
            if (separatorY >= contentAreaTop && separatorY <= contentAreaBottom)
            {
                var separatorColor = ImGui.GetColorU32(colors.GridLine);
                drawList.AddLine(
                    new Vector2(legendPos.X + padding, separatorY),
                    new Vector2(legendPos.X + legendWidth - padding - (needsScrolling ? scrollbarWidth + 4f : 0f), separatorY),
                    separatorColor);
            }
            yOffset += separatorHeight;
        }
        
        // Sort series by value descending
        var sortedSeries = GetSortedSeries(data.Series);
        
        // Draw each series entry
        foreach (var series in sortedSeries)
        {
            var rowTop = yOffset;
            var rowBottom = yOffset + rowHeight;
            
            if (rowBottom < contentAreaTop || rowTop > contentAreaBottom)
            {
                yOffset += rowHeight;
                continue;
            }
            
            var isDirectlyHidden = hiddenSeries.Contains(series.Name);
            var isHiddenViaGroup = IsSeriesHiddenViaGroup(series, hiddenGroups);
            var isHidden = isDirectlyHidden || isHiddenViaGroup;
            var displayAlpha = isHidden ? style.LegendHiddenAlpha : 1f;
            
            var mouseInRow = mouseInLegend && 
                            mousePos.X <= contentAreaRight &&
                            mousePos.Y >= Math.Max(rowTop, contentAreaTop) && 
                            mousePos.Y < Math.Min(rowBottom, contentAreaBottom) &&
                            rowTop >= contentAreaTop && rowBottom <= contentAreaBottom;
            
            if (mouseInRow && GraphOverlay.CanProcessInteraction() && ImGui.IsMouseClicked(0))
            {
                onToggleSeries?.Invoke(series.Name);
            }
            
            if (mouseInRow && GraphOverlay.CanProcessInteraction())
            {
                hoveredItemName = series.Name;
                hoveredIsGroup = false;
                var hoverColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.1f));
                drawList.AddRectFilled(
                    new Vector2(legendPos.X + 2, Math.Max(rowTop, contentAreaTop)), 
                    new Vector2(contentAreaRight, Math.Min(rowBottom, contentAreaBottom)), 
                    hoverColor, 2f);
            }
            
            if (rowTop >= contentAreaTop - rowHeight && rowBottom <= contentAreaBottom + rowHeight)
            {
                var indicatorY = yOffset + (rowHeight - indicatorSize) / 2;
                if (indicatorY >= contentAreaTop - indicatorSize && indicatorY + indicatorSize <= contentAreaBottom + indicatorSize)
                {
                    var indicatorPos = new Vector2(legendPos.X + padding, indicatorY);
                    var colorU32 = ImGui.GetColorU32(new Vector4(series.Color.X, series.Color.Y, series.Color.Z, displayAlpha));
                    
                    if (isHidden)
                    {
                        drawList.AddRect(indicatorPos, new Vector2(indicatorPos.X + indicatorSize, indicatorPos.Y + indicatorSize), colorU32, style.LegendIndicatorRounding);
                    }
                    else
                    {
                        drawList.AddRectFilled(indicatorPos, new Vector2(indicatorPos.X + indicatorSize, indicatorPos.Y + indicatorSize), colorU32, style.LegendIndicatorRounding);
                    }

                    var textColor = isHidden ? colors.TextSecondary : colors.TextPrimary;
                    var textY = yOffset + (rowHeight - ImGui.GetTextLineHeight()) / 2;
                    if (textY >= contentAreaTop - rowHeight && textY <= contentAreaBottom)
                    {
                        var textPos = new Vector2(indicatorPos.X + indicatorSize + indicatorTextGap, textY);
                        drawList.AddText(textPos, ImGui.GetColorU32(textColor), series.Name);
                    }
                }
            }
            
            yOffset += rowHeight;
        }
        
        // Draw scrollbar if needed
        if (needsScrolling)
        {
            scrollOffset = DrawScrollbar(
                drawList,
                legendPos.X + legendWidth - padding - scrollbarWidth,
                contentAreaTop,
                contentAreaBottom,
                scrollbarWidth,
                contentHeight,
                legendHeight - padding * 2,
                scrollOffset,
                maxScrollOffset,
                style);
        }
        
        // Show tooltip
        var scrollbarX = legendPos.X + legendWidth - padding - scrollbarWidth;
        var mouseOverScrollbar = needsScrolling && mousePos.X >= scrollbarX && mousePos.X <= legendPos.X + legendWidth;
        if (mouseInLegend && !mouseOverScrollbar && hoveredItemName != null)
        {
            if (hoveredIsGroup)
            {
                var group = data.Groups?.FirstOrDefault(g => g.Name == hoveredItemName);
                if (group != null)
                {
                    var isHidden = hiddenGroups.Contains(group.Name);
                    var statusText = isHidden ? " (hidden)" : "";
                    var scrollHint = needsScrolling ? "\nScroll to see more" : "";
                    ImGui.SetTooltip($"Group: {group.Name}{statusText}\n{group.SeriesNames.Count} series\nClick to toggle visibility{scrollHint}");
                }
            }
            else
            {
                var series = sortedSeries.FirstOrDefault(s => s.Name == hoveredItemName);
                if (series != null)
                {
                    var isDirectlyHidden = hiddenSeries.Contains(series.Name);
                    var isHiddenViaGroup = IsSeriesHiddenViaGroup(series, hiddenGroups);
                    var lastValue = series.PointCount > 0 ? (float)series.YValues[series.PointCount - 1] : 0f;
                    var statusText = isDirectlyHidden ? " (hidden)" : isHiddenViaGroup ? " (hidden via group)" : "";
                    var groupInfo = series.GroupNames is { Count: > 0 } 
                        ? $"\nGroups: {string.Join(", ", series.GroupNames)}" 
                        : "";
                    var scrollHint = needsScrolling ? "\nScroll to see more" : "";
                    ImGui.SetTooltip($"{series.Name}: {NumberFormatter.FormatCompact(lastValue)}{statusText}{groupInfo}\nClick to toggle visibility{scrollHint}");
                }
            }
        }
        
        drawList.PopClipRect();
        
        return new InsideLegendResult(legendPos, new Vector2(legendPos.X + legendWidth, legendPos.Y + legendHeight), scrollOffset, collapseToggled);
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Draws a scrollbar for the inside legend and handles mouse interaction.
    /// </summary>
    /// <returns>The updated scroll offset if the user is interacting with the scrollbar.</returns>
    private static float DrawScrollbar(
        ImDrawListPtr drawList,
        float x,
        float trackTop,
        float trackBottom,
        float width,
        float contentHeight,
        float visibleHeight,
        float scrollOffset,
        float maxScrollOffset,
        GraphStyleConfig style)
    {
        var trackHeight = trackBottom - trackTop;
        
        // Track background
        var trackColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
        drawList.AddRectFilled(
            new Vector2(x, trackTop),
            new Vector2(x + width, trackBottom),
            trackColor, 3f);
        
        // Thumb calculations
        var visibleRatio = visibleHeight / contentHeight;
        var thumbHeight = Math.Max(20f, trackHeight * visibleRatio);
        var scrollRatio = maxScrollOffset > 0 ? scrollOffset / maxScrollOffset : 0f;
        var thumbTop = trackTop + scrollRatio * (trackHeight - thumbHeight);
        
        // Check if mouse is over the scrollbar track
        var mousePos = ImGui.GetMousePos();
        var mouseOverTrack = GraphOverlay.RectContains(new Vector2(x, trackTop), new Vector2(x + width, trackBottom), mousePos);
        var mouseOverThumb = GraphOverlay.RectContains(new Vector2(x, thumbTop), new Vector2(x + width, thumbTop + thumbHeight), mousePos);
        
        // Handle scrollbar click/drag
        if (mouseOverTrack && ImGui.IsMouseDown(0))
        {
            // Calculate new scroll position based on mouse Y
            // Map mouse Y to scroll offset (click on track jumps to that position)
            var clickableTrackHeight = trackHeight - thumbHeight;
            if (clickableTrackHeight > 0)
            {
                // Center the thumb on the mouse position
                var targetThumbTop = mousePos.Y - thumbHeight / 2f;
                targetThumbTop = Math.Clamp(targetThumbTop, trackTop, trackTop + clickableTrackHeight);
                var newScrollRatio = (targetThumbTop - trackTop) / clickableTrackHeight;
                scrollOffset = newScrollRatio * maxScrollOffset;
            }
        }
        
        // Draw thumb with hover/active highlighting
        var colors = style.Colors;
        var thumbColor = mouseOverThumb || (mouseOverTrack && ImGui.IsMouseDown(0))
            ? ImGui.GetColorU32(colors.TextSecondary)  // Brighter when hovered/active
            : ImGui.GetColorU32(colors.GridLine);
        
        // Recalculate thumb position with potentially updated scroll offset
        scrollRatio = maxScrollOffset > 0 ? scrollOffset / maxScrollOffset : 0f;
        thumbTop = trackTop + scrollRatio * (trackHeight - thumbHeight);
        
        drawList.AddRectFilled(
            new Vector2(x, thumbTop),
            new Vector2(x + width, thumbTop + thumbHeight),
            thumbColor, 3f);

        return scrollOffset;
    }

    #endregion
}
