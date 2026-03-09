using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Controls drawer utilities for ImPlot graphs.
/// Provides an interactive drawer panel with auto-scroll controls.
/// </summary>
public static class GraphControls
{
    #region Helper Methods
    
    /// <summary>
    /// Checks if mouse interactions should be processed.
    /// Returns false if another window is on top blocking interactions.
    /// </summary>
    private static bool CanProcessInteraction()
    {
        return ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
    }
    
    #endregion
    
    #region Time Unit Names
    
    /// <summary>
    /// Short names for time unit buttons.
    /// </summary>
    private static readonly string[] TimeUnitNames = { "sec", "min", "hr", "day", "wk" };
    
    #endregion
    
    #region Result Struct
    
    /// <summary>
    /// Result from drawing the controls drawer, containing bounds and state.
    /// </summary>
    public readonly struct ControlsDrawerResult
    {
        /// <summary>Minimum corner of the drawer bounds (including toggle button).</summary>
        public readonly Vector2 BoundsMin;
        
        /// <summary>Maximum corner of the drawer bounds (including toggle button).</summary>
        public readonly Vector2 BoundsMax;
        
        /// <summary>Whether the bounds are valid.</summary>
        public readonly bool IsValid;
        
        /// <summary>Updated open/closed state.</summary>
        public readonly bool IsOpen;
        
        /// <summary>Updated auto-scroll enabled state.</summary>
        public readonly bool AutoScrollEnabled;
        
        /// <summary>Updated auto-scroll time value.</summary>
        public readonly int AutoScrollTimeValue;
        
        /// <summary>Updated auto-scroll time unit.</summary>
        public readonly TimeUnit AutoScrollTimeUnit;
        
        /// <summary>Updated auto-scroll now position (0-100%).</summary>
        public readonly float AutoScrollNowPosition;
        
        /// <summary>Whether any settings changed this frame.</summary>
        public readonly bool SettingsChanged;
        
        public ControlsDrawerResult(
            Vector2 boundsMin,
            Vector2 boundsMax,
            bool isOpen,
            bool autoScrollEnabled,
            int autoScrollTimeValue,
            TimeUnit autoScrollTimeUnit,
            float autoScrollNowPosition,
            bool settingsChanged)
        {
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            IsValid = true;
            IsOpen = isOpen;
            AutoScrollEnabled = autoScrollEnabled;
            AutoScrollTimeValue = autoScrollTimeValue;
            AutoScrollTimeUnit = autoScrollTimeUnit;
            AutoScrollNowPosition = autoScrollNowPosition;
            SettingsChanged = settingsChanged;
        }
        
        public static readonly ControlsDrawerResult Invalid = new(Vector2.Zero, Vector2.Zero, false, false, 1, TimeUnit.Hours, 75f, false);
    }
    
    #endregion
    
    #region Main Draw Method
    
    /// <summary>
    /// Draws the controls drawer with toggle button and auto-scroll controls.
    /// The drawer slides out from the top-right corner of the plot.
    /// </summary>
    /// <param name="isOpen">Whether the drawer is currently open.</param>
    /// <param name="autoScrollEnabled">Current auto-scroll enabled state.</param>
    /// <param name="autoScrollTimeValue">Current auto-scroll time value.</param>
    /// <param name="AutoScrollTimeUnit">Current auto-scroll time unit.</param>
    /// <param name="autoScrollNowPosition">Current auto-scroll now position (0-100%).</param>
    /// <param name="style">Optional style configuration.</param>
    /// <returns>Result containing updated state and bounds.</returns>
    public static ControlsDrawerResult DrawControlsDrawer(
        bool isOpen,
        bool autoScrollEnabled,
        int autoScrollTimeValue,
        TimeUnit AutoScrollTimeUnit,
        float autoScrollNowPosition,
        GraphStyleConfig? style = null)
    {
        style ??= GraphStyleConfig.Default;
        var colors = style.Colors;
        
        var drawList = ImPlot.GetPlotDrawList();
        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        
        // Constants for drawer layout
        var toggleButtonWidth = style.ToggleButtonWidth;
        var toggleButtonHeight = style.ToggleButtonHeight;
        var drawerWidth = style.DrawerWidth;
        var drawerPadding = style.DrawerPadding;
        var rowHeight = style.DrawerRowHeight;
        var checkboxSize = style.DrawerCheckboxSize;
        
        // Track if any settings changed
        var settingsChanged = false;
        var newAutoScrollEnabled = autoScrollEnabled;
        var newAutoScrollTimeValue = autoScrollTimeValue;
        var newAutoScrollTimeUnit = AutoScrollTimeUnit;
        var newAutoScrollNowPosition = autoScrollNowPosition;
        var newIsOpen = isOpen;
        
        // Calculate drawer height based on content
        var drawerContentHeight = rowHeight; // Auto-scroll checkbox
        if (autoScrollEnabled)
        {
            drawerContentHeight += rowHeight; // Value input row (- value +)
            drawerContentHeight += rowHeight; // Unit selector row
            drawerContentHeight += rowHeight; // Position label row
            drawerContentHeight += rowHeight; // Slider row + percentage text
        }
        var drawerHeight = drawerPadding * 2 + drawerContentHeight;
        
        // Margin from plot edges
        var margin = style.DrawerMargin;
        
        // Position toggle button at top-right corner of plot, clamped within plot area
        var toggleButtonPos = new Vector2(
            Math.Clamp(plotPos.X + plotSize.X - toggleButtonWidth - margin, plotPos.X + margin, plotPos.X + plotSize.X - toggleButtonWidth - margin),
            plotPos.Y + margin
        );
        
        // Clamp drawer width to fit within plot area
        var maxDrawerWidth = plotSize.X - margin * 2;
        var clampedDrawerWidth = Math.Min(drawerWidth, maxDrawerWidth);
        
        // Position drawer below the toggle button, clamped within plot area
        var drawerPos = new Vector2(
            Math.Clamp(toggleButtonPos.X - clampedDrawerWidth - 4 + toggleButtonWidth, plotPos.X + margin, plotPos.X + plotSize.X - clampedDrawerWidth - margin),
            toggleButtonPos.Y + toggleButtonHeight + 4
        );
        
        // Track mouse interactions
        var mousePos = ImGui.GetMousePos();
        
        // Draw toggle button
        DrawToggleButton(drawList, toggleButtonPos, toggleButtonWidth, toggleButtonHeight, isOpen, mousePos, style, ref newIsOpen);
        
        // Calculate bounds
        Vector2 boundsMin, boundsMax;
        if (newIsOpen)
        {
            boundsMin = new Vector2(drawerPos.X, Math.Min(drawerPos.Y, toggleButtonPos.Y));
            boundsMax = new Vector2(toggleButtonPos.X + toggleButtonWidth, Math.Max(drawerPos.Y + drawerHeight, toggleButtonPos.Y + toggleButtonHeight));
        }
        else
        {
            boundsMin = toggleButtonPos;
            boundsMax = new Vector2(toggleButtonPos.X + toggleButtonWidth, toggleButtonPos.Y + toggleButtonHeight);
        }
        
        if (!newIsOpen)
        {
            return new ControlsDrawerResult(boundsMin, boundsMax, newIsOpen, newAutoScrollEnabled, newAutoScrollTimeValue, newAutoScrollTimeUnit, newAutoScrollNowPosition, newIsOpen != isOpen);
        }
        
        // Draw drawer background
        var drawerBgColor = ImGui.GetColorU32(new Vector4(colors.FrameBackground.X, colors.FrameBackground.Y, colors.FrameBackground.Z, 0.92f));
        var drawerBorderColor = ImGui.GetColorU32(colors.AxisLine);
        
        drawList.AddRectFilled(drawerPos, new Vector2(drawerPos.X + clampedDrawerWidth, drawerPos.Y + drawerHeight), drawerBgColor, 4f);
        drawList.AddRect(drawerPos, new Vector2(drawerPos.X + clampedDrawerWidth, drawerPos.Y + drawerHeight), drawerBorderColor, 4f);
        
        var contentX = drawerPos.X + drawerPadding;
        var contentY = drawerPos.Y + drawerPadding;
        
        // Draw Auto-Scroll checkbox
        if (DrawAutoScrollCheckbox(drawList, contentX, contentY, rowHeight, checkboxSize, clampedDrawerWidth, drawerPadding, autoScrollEnabled, mousePos, style))
        {
            newAutoScrollEnabled = !autoScrollEnabled;
            settingsChanged = true;
        }
        contentY += rowHeight;
        
        if (autoScrollEnabled)
        {
            // Draw value controls (- value +)
            var valueChanged = DrawValueControls(drawList, contentX, contentY, rowHeight, clampedDrawerWidth, drawerPadding, autoScrollTimeValue, mousePos, style, out var newValue);
            if (valueChanged)
            {
                newAutoScrollTimeValue = newValue;
                settingsChanged = true;
            }
            contentY += rowHeight;
            
            // Draw unit selector
            var unitChanged = DrawUnitSelector(drawList, contentX, contentY, rowHeight, clampedDrawerWidth, drawerPadding, AutoScrollTimeUnit, mousePos, style, out var newUnit);
            if (unitChanged)
            {
                newAutoScrollTimeUnit = newUnit;
                settingsChanged = true;
            }
            contentY += rowHeight;
            
            // Draw position slider
            var positionChanged = DrawPositionSlider(drawList, contentX, contentY, rowHeight, clampedDrawerWidth, drawerPadding, autoScrollNowPosition, mousePos, style, out var newPosition);
            if (positionChanged)
            {
                newAutoScrollNowPosition = newPosition;
                settingsChanged = true;
            }
        }
        
        // Track open/close change
        if (newIsOpen != isOpen)
            settingsChanged = true;
        
        return new ControlsDrawerResult(boundsMin, boundsMax, newIsOpen, newAutoScrollEnabled, newAutoScrollTimeValue, newAutoScrollTimeUnit, newAutoScrollNowPosition, settingsChanged);
    }
    
    #endregion
    
    #region Component Drawing Methods
    
    /// <summary>
    /// Draws the toggle button for the controls drawer.
    /// </summary>
    private static void DrawToggleButton(
        ImDrawListPtr drawList,
        Vector2 toggleButtonPos,
        float toggleButtonWidth,
        float toggleButtonHeight,
        bool isOpen,
        Vector2 mousePos,
        GraphStyleConfig style,
        ref bool newIsOpen)
    {
        var colors = style.Colors;
        var buttonBgColor = ImGui.GetColorU32(new Vector4(colors.FrameBackground.X, colors.FrameBackground.Y, colors.FrameBackground.Z, 0.9f));
        var buttonBorderColor = ImGui.GetColorU32(colors.AxisLine);
        var buttonHovered = mousePos.X >= toggleButtonPos.X && mousePos.X <= toggleButtonPos.X + toggleButtonWidth &&
                           mousePos.Y >= toggleButtonPos.Y && mousePos.Y <= toggleButtonPos.Y + toggleButtonHeight;
        
        if (buttonHovered)
        {
            buttonBgColor = ImGui.GetColorU32(new Vector4(colors.GridLine.X, colors.GridLine.Y, colors.GridLine.Z, 0.9f));
        }
        
        drawList.AddRectFilled(toggleButtonPos, 
            new Vector2(toggleButtonPos.X + toggleButtonWidth, toggleButtonPos.Y + toggleButtonHeight), 
            buttonBgColor, 3f);
        drawList.AddRect(toggleButtonPos, 
            new Vector2(toggleButtonPos.X + toggleButtonWidth, toggleButtonPos.Y + toggleButtonHeight), 
            buttonBorderColor, 3f);
        
        // Draw gear/settings icon
        var iconColor = ImGui.GetColorU32(isOpen ? colors.Neutral : colors.TextPrimary);
        var iconCenter = new Vector2(toggleButtonPos.X + toggleButtonWidth / 2, toggleButtonPos.Y + toggleButtonHeight / 2);
        var iconRadius = style.DrawerIconRadius;
        
        drawList.AddCircle(iconCenter, iconRadius, iconColor, 8, 1.5f);
        drawList.AddCircleFilled(iconCenter, 2f, iconColor);
        
        // Handle toggle button click (only if no other window is blocking)
        if (buttonHovered && CanProcessInteraction() && ImGui.IsMouseClicked(0))
        {
            newIsOpen = !isOpen;
        }
        
        // Show tooltip (only if window is not blocked)
        if (buttonHovered && CanProcessInteraction())
        {
            ImGui.SetTooltip(isOpen ? "Close controls" : "Open controls");
        }
    }
    
    /// <summary>
    /// Draws the auto-scroll checkbox.
    /// </summary>
    private static bool DrawAutoScrollCheckbox(
        ImDrawListPtr drawList,
        float contentX,
        float contentY,
        float rowHeight,
        float checkboxSize,
        float drawerWidth,
        float drawerPadding,
        bool autoScrollEnabled,
        Vector2 mousePos,
        GraphStyleConfig style)
    {
        var colors = style.Colors;
        var checkboxPos = new Vector2(contentX, contentY + (rowHeight - checkboxSize) / 2);
        var checkboxRowEnd = new Vector2(contentX + drawerWidth - drawerPadding * 2, contentY + rowHeight);
        var checkboxRowHovered = mousePos.X >= contentX && mousePos.X <= checkboxRowEnd.X &&
                                mousePos.Y >= contentY && mousePos.Y <= checkboxRowEnd.Y;
        
        var checkboxBorderColor = checkboxRowHovered 
            ? ImGui.GetColorU32(colors.TextPrimary) 
            : ImGui.GetColorU32(colors.TextSecondary);
        drawList.AddRect(checkboxPos, 
            new Vector2(checkboxPos.X + checkboxSize, checkboxPos.Y + checkboxSize), 
            checkboxBorderColor, style.DrawerRounding);
        
        if (autoScrollEnabled)
        {
            var checkColor = ImGui.GetColorU32(colors.Bullish);
            var checkPadding = style.DrawerCheckPadding;
            drawList.AddLine(
                new Vector2(checkboxPos.X + checkPadding, checkboxPos.Y + checkboxSize / 2),
                new Vector2(checkboxPos.X + checkboxSize / 2, checkboxPos.Y + checkboxSize - checkPadding),
                checkColor, 2f);
            drawList.AddLine(
                new Vector2(checkboxPos.X + checkboxSize / 2, checkboxPos.Y + checkboxSize - checkPadding),
                new Vector2(checkboxPos.X + checkboxSize - checkPadding, checkboxPos.Y + checkPadding),
                checkColor, 2f);
        }
        
        var labelPos = new Vector2(checkboxPos.X + checkboxSize + 6, contentY + (rowHeight - ImGui.GetTextLineHeight()) / 2);
        var labelColor = ImGui.GetColorU32(autoScrollEnabled ? colors.TextPrimary : colors.TextSecondary);
        drawList.AddText(labelPos, labelColor, "Auto-scroll");
        
        return checkboxRowHovered && CanProcessInteraction() && ImGui.IsMouseClicked(0);
    }
    
    /// <summary>
    /// Draws the value controls (- value +) with editable input field.
    /// </summary>
    private static bool DrawValueControls(
        ImDrawListPtr drawList,
        float contentX,
        float contentY,
        float rowHeight,
        float drawerWidth,
        float drawerPadding,
        int currentValue,
        Vector2 mousePos,
        GraphStyleConfig style,
        out int newValue)
    {
        var colors = style.Colors;
        var valueBoxWidth = style.DrawerValueBoxWidth;
        var smallButtonWidth = style.DrawerSmallButtonWidth;
        var spacing = style.DrawerElementSpacing;
        var rounding = style.DrawerRounding;
        
        newValue = currentValue;
        var changed = false;
        
        // Draw "-" button
        var minusBtnPos = new Vector2(contentX, contentY + 2);
        var minusBtnHovered = mousePos.X >= minusBtnPos.X && mousePos.X <= minusBtnPos.X + smallButtonWidth &&
                             mousePos.Y >= minusBtnPos.Y && mousePos.Y <= minusBtnPos.Y + rowHeight - 4;
        var minusBtnBg = minusBtnHovered 
            ? ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.35f, 0.8f))
            : ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.7f));
        drawList.AddRectFilled(minusBtnPos, new Vector2(minusBtnPos.X + smallButtonWidth, minusBtnPos.Y + rowHeight - 4), minusBtnBg, rounding);
        drawList.AddRect(minusBtnPos, new Vector2(minusBtnPos.X + smallButtonWidth, minusBtnPos.Y + rowHeight - 4), ImGui.GetColorU32(colors.GridLine), rounding);
        var minusText = "-";
        var minusTextSize = ImGui.CalcTextSize(minusText);
        drawList.AddText(new Vector2(minusBtnPos.X + (smallButtonWidth - minusTextSize.X) / 2, minusBtnPos.Y + (rowHeight - 4 - minusTextSize.Y) / 2), 
            ImGui.GetColorU32(colors.TextPrimary), minusText);
        
        if (minusBtnHovered && CanProcessInteraction() && ImGui.IsMouseClicked(0) && currentValue > 1)
        {
            newValue = currentValue - 1;
            changed = true;
        }
        
        // Value input box - use ImGui InputInt for direct editing
        var valueBoxPos = new Vector2(minusBtnPos.X + smallButtonWidth + spacing, contentY + 2);
        var inputHeight = rowHeight - 4;
        
        // Position the cursor for ImGui widget
        ImGui.SetCursorScreenPos(valueBoxPos);
        
        // Style the input to match drawer theme
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, (inputHeight - ImGui.GetTextLineHeight()) / 2));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.15f, 0.15f, 0.15f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.Border, colors.GridLine);
        ImGui.PushStyleColor(ImGuiCol.Text, colors.Neutral);
        
        ImGui.SetNextItemWidth(valueBoxWidth);
        var inputValue = currentValue;
        if (ImGui.InputInt("##GraphValueInput", ref inputValue, 0, 0))
        {
            // Clamp the value to valid range
            newValue = Math.Clamp(inputValue, 1, 999);
            changed = newValue != currentValue;
        }
        
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(2);
        
        // "+" button
        var plusBtnPos = new Vector2(valueBoxPos.X + valueBoxWidth + spacing, contentY + 2);
        var plusBtnHovered = mousePos.X >= plusBtnPos.X && mousePos.X <= plusBtnPos.X + smallButtonWidth &&
                            mousePos.Y >= plusBtnPos.Y && mousePos.Y <= plusBtnPos.Y + rowHeight - 4;
        var plusBtnBg = plusBtnHovered 
            ? ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.35f, 0.8f))
            : ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.7f));
        drawList.AddRectFilled(plusBtnPos, new Vector2(plusBtnPos.X + smallButtonWidth, plusBtnPos.Y + rowHeight - 4), plusBtnBg, rounding);
        drawList.AddRect(plusBtnPos, new Vector2(plusBtnPos.X + smallButtonWidth, plusBtnPos.Y + rowHeight - 4), ImGui.GetColorU32(colors.GridLine), rounding);
        var plusText = "+";
        var plusTextSize = ImGui.CalcTextSize(plusText);
        drawList.AddText(new Vector2(plusBtnPos.X + (smallButtonWidth - plusTextSize.X) / 2, plusBtnPos.Y + (rowHeight - 4 - plusTextSize.Y) / 2), 
            ImGui.GetColorU32(colors.TextPrimary), plusText);
        
        if (plusBtnHovered && CanProcessInteraction() && ImGui.IsMouseClicked(0) && currentValue < 999)
        {
            newValue = currentValue + 1;
            changed = true;
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws the time unit selector buttons.
    /// </summary>
    private static bool DrawUnitSelector(
        ImDrawListPtr drawList,
        float contentX,
        float contentY,
        float rowHeight,
        float drawerWidth,
        float drawerPadding,
        TimeUnit currentUnit,
        Vector2 mousePos,
        GraphStyleConfig style,
        out TimeUnit newUnit)
    {
        var colors = style.Colors;
        var spacing = style.DrawerElementSpacing;
        var rounding = style.DrawerRounding;
        
        newUnit = currentUnit;
        var changed = false;
        
        var unitButtonWidth = (drawerWidth - drawerPadding * 2 - spacing * (TimeUnitNames.Length - 1)) / TimeUnitNames.Length;
        var unitButtonHeight = rowHeight - 4;
        
        for (var i = 0; i < TimeUnitNames.Length; i++)
        {
            var unitBtnPos = new Vector2(contentX + i * (unitButtonWidth + spacing), contentY + 2);
            var isSelected = (int)currentUnit == i;
            var unitBtnHovered = mousePos.X >= unitBtnPos.X && mousePos.X <= unitBtnPos.X + unitButtonWidth &&
                                mousePos.Y >= unitBtnPos.Y && mousePos.Y <= unitBtnPos.Y + unitButtonHeight;
            
            var unitBtnBg = isSelected 
                ? ImGui.GetColorU32(new Vector4(colors.Neutral.X, colors.Neutral.Y, colors.Neutral.Z, 0.35f))
                : unitBtnHovered 
                    ? ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.6f))
                    : ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 0.5f));
            var unitBtnBorder = isSelected 
                ? ImGui.GetColorU32(colors.Neutral) 
                : ImGui.GetColorU32(colors.GridLine);
            
            drawList.AddRectFilled(unitBtnPos, new Vector2(unitBtnPos.X + unitButtonWidth, unitBtnPos.Y + unitButtonHeight), unitBtnBg, rounding);
            drawList.AddRect(unitBtnPos, new Vector2(unitBtnPos.X + unitButtonWidth, unitBtnPos.Y + unitButtonHeight), unitBtnBorder, rounding);
            
            var unitText = TimeUnitNames[i];
            var unitTextSize = ImGui.CalcTextSize(unitText);
            var unitTextColor = isSelected ? ImGui.GetColorU32(colors.Neutral) : ImGui.GetColorU32(colors.TextPrimary);
            drawList.AddText(new Vector2(unitBtnPos.X + (unitButtonWidth - unitTextSize.X) / 2, unitBtnPos.Y + (unitButtonHeight - unitTextSize.Y) / 2), 
                unitTextColor, unitText);
            
            if (unitBtnHovered && CanProcessInteraction() && ImGui.IsMouseClicked(0))
            {
                newUnit = (TimeUnit)i;
                changed = true;
            }
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws the position slider for auto-scroll "now" position.
    /// </summary>
    private static bool DrawPositionSlider(
        ImDrawListPtr drawList,
        float contentX,
        float contentY,
        float rowHeight,
        float drawerWidth,
        float drawerPadding,
        float currentPosition,
        Vector2 mousePos,
        GraphStyleConfig style,
        out float newPosition)
    {
        var colors = style.Colors;
        var sliderHeight = style.DrawerSliderHeight;
        var handleWidth = style.DrawerSliderHandleWidth;
        var handleHeight = style.DrawerSliderHandleHeight;
        var sliderRounding = style.DrawerSliderRounding;
        var rounding = style.DrawerRounding;
        
        newPosition = currentPosition;
        var changed = false;
        
        // Position label
        var sliderLabelPos = new Vector2(contentX, contentY + (rowHeight - ImGui.GetTextLineHeight()) / 2);
        drawList.AddText(sliderLabelPos, ImGui.GetColorU32(colors.TextSecondary), "Position:");
        
        contentY += rowHeight;
        
        // Slider track
        var sliderTrackPos = new Vector2(contentX, contentY + (rowHeight - sliderHeight) / 2 - 4);
        var sliderTrackWidth = drawerWidth - drawerPadding * 2;
        
        drawList.AddRectFilled(sliderTrackPos, new Vector2(sliderTrackPos.X + sliderTrackWidth, sliderTrackPos.Y + sliderHeight), 
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 0.8f)), sliderRounding);
        drawList.AddRect(sliderTrackPos, new Vector2(sliderTrackPos.X + sliderTrackWidth, sliderTrackPos.Y + sliderHeight), 
            ImGui.GetColorU32(colors.GridLine), sliderRounding);
        
        // Fill
        var fillWidth = sliderTrackWidth * (currentPosition / 100f);
        if (fillWidth > 0)
        {
            drawList.AddRectFilled(sliderTrackPos, new Vector2(sliderTrackPos.X + fillWidth, sliderTrackPos.Y + sliderHeight), 
                ImGui.GetColorU32(new Vector4(colors.Neutral.X, colors.Neutral.Y, colors.Neutral.Z, 0.4f)), sliderRounding);
        }
        
        // Handle
        var handleX = sliderTrackPos.X + fillWidth - handleWidth / 2;
        handleX = Math.Clamp(handleX, sliderTrackPos.X, sliderTrackPos.X + sliderTrackWidth - handleWidth);
        var handleY = sliderTrackPos.Y + sliderHeight / 2 - handleHeight / 2;
        
        var sliderHovered = mousePos.X >= sliderTrackPos.X - 5 && mousePos.X <= sliderTrackPos.X + sliderTrackWidth + 5 &&
                           mousePos.Y >= handleY && mousePos.Y <= handleY + handleHeight;
        
        var handleColor = sliderHovered 
            ? ImGui.GetColorU32(colors.Neutral)
            : ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.6f, 1f));
        drawList.AddRectFilled(new Vector2(handleX, handleY), new Vector2(handleX + handleWidth, handleY + handleHeight), handleColor, rounding);
        drawList.AddRect(new Vector2(handleX, handleY), new Vector2(handleX + handleWidth, handleY + handleHeight), 
            ImGui.GetColorU32(colors.AxisLine), rounding);
        
        // Handle drag
        if (sliderHovered && ImGui.IsMouseDown(0))
        {
            var relativeX = mousePos.X - sliderTrackPos.X;
            var newPos = Math.Clamp(relativeX / sliderTrackWidth * 100f, 0f, 100f);
            if (Math.Abs(newPos - currentPosition) > 0.5f)
            {
                newPosition = newPos;
                changed = true;
            }
        }
        
        // Percentage text
        var percentText = $"{currentPosition:F0}%";
        var percentTextSize = ImGui.CalcTextSize(percentText);
        drawList.AddText(new Vector2(sliderTrackPos.X + sliderTrackWidth - percentTextSize.X, sliderTrackPos.Y + sliderHeight + 2), 
            ImGui.GetColorU32(colors.TextSecondary), percentText);
        
        return changed;
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Checks if the mouse is within the controls drawer bounds.
    /// Also checks that no other window is on top blocking the interaction.
    /// </summary>
    /// <param name="result">The drawer result from the previous draw call.</param>
    /// <returns>True if mouse is over the drawer and the window is hovered.</returns>
    public static bool IsMouseOverDrawer(ControlsDrawerResult result)
    {
        if (!result.IsValid) return false;
        
        // Check if the current window is hovered (no other window on top blocking it)
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            return false;
        
        var mousePos = ImGui.GetMousePos();
        return mousePos.X >= result.BoundsMin.X && mousePos.X <= result.BoundsMax.X &&
               mousePos.Y >= result.BoundsMin.Y && mousePos.Y <= result.BoundsMax.Y;
    }
    
    #endregion
}
