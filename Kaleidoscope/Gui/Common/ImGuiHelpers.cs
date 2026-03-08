using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Common;

/// <summary>
/// Helper methods for common ImGui operations.
/// </summary>
public static class ImGuiHelpers
{
    /// <summary>
    /// Standard icon size for inline item/currency icons, based on current font height.
    /// This is a computed property that returns the text line height, commonly used for 
    /// small inline icons that should match text size.
    /// </summary>
    public static float IconSize => ImGui.GetTextLineHeight();
    
    /// <summary>
    /// Default horizontal padding added to each side of button text.
    /// </summary>
    private const float DefaultButtonPadding = 12f;
    
    /// <summary>
    /// Calculates the width of a button based on its text content plus padding.
    /// </summary>
    private static float CalcButtonWidth(string text, float padding = DefaultButtonPadding)
    {
        // Strip ## ID suffix if present
        var displayText = text;
        var hashIndex = text.IndexOf("##", StringComparison.Ordinal);
        if (hashIndex >= 0)
        {
            displayText = text[..hashIndex];
        }
        
        return ImGui.CalcTextSize(displayText).X + (padding * 2);
    }
    
    /// <summary>
    /// Creates a button with automatically calculated width based on text content.
    /// </summary>
    /// <param name="label">The button label (can include ## for ID suffix).</param>
    /// <param name="padding">Horizontal padding added to each side. Defaults to DefaultButtonPadding.</param>
    /// <returns>True if the button was clicked.</returns>
    public static bool ButtonAutoWidth(string label, float padding = DefaultButtonPadding)
    {
        var width = CalcButtonWidth(label, padding);
        return ImGui.Button(label, new Vector2(width, 0));
    }
    
    /// <summary>
    /// Creates a button with automatically calculated width and specified height.
    /// </summary>
    /// <param name="label">The button label (can include ## for ID suffix).</param>
    /// <param name="height">The button height. Use 0 for default height.</param>
    /// <param name="padding">Horizontal padding added to each side. Defaults to DefaultButtonPadding.</param>
    /// <returns>True if the button was clicked.</returns>
    public static bool ButtonAutoWidth(string label, float height, float padding = DefaultButtonPadding)
    {
        var width = CalcButtonWidth(label, padding);
        return ImGui.Button(label, new Vector2(width, height));
    }
    
    /// <summary>
    /// Default color used when no color is set or when cleared — alias for <see cref="UiColors.Muted"/>.
    /// </summary>
    public static readonly Vector4 DefaultColor = UiColors.Muted;
    
    /// <summary>
    /// Color picker with right-click to clear functionality.
    /// </summary>
    /// <param name="label">The label/ID for the color picker.</param>
    /// <param name="color">The nullable color value. Null means use default.</param>
    /// <param name="defaultColor">The default color to show when null. Also used as the reset value.</param>
    /// <param name="tooltip">Optional tooltip to show on hover.</param>
    /// <param name="flags">ImGui color edit flags.</param>
    /// <returns>Tuple of (changed, newColor). newColor is null if right-clicked to clear.</returns>
    public static (bool changed, Vector4? newColor) ColorPickerWithClear(
        string label,
        Vector4? color,
        Vector4 defaultColor,
        string? tooltip = null,
        Dalamud.Bindings.ImGui.ImGuiColorEditFlags flags = Dalamud.Bindings.ImGui.ImGuiColorEditFlags.NoInputs | Dalamud.Bindings.ImGui.ImGuiColorEditFlags.AlphaPreviewHalf)
    {
        var displayColor = color ?? defaultColor;
        var changed = false;
        Vector4? result = color;
        
        if (ImGui.ColorEdit4(label, ref displayColor, flags))
        {
            result = displayColor;
            changed = true;
        }
        
        // Right-click to clear (reset to null/default)
        if (ImGui.IsItemClicked(Dalamud.Bindings.ImGui.ImGuiMouseButton.Right))
        {
            result = null;
            changed = true;
        }
        
        if (ImGui.IsItemHovered())
        {
            var hoverText = tooltip ?? "Color";
            if (color.HasValue)
            {
                hoverText += "\nRight-click to reset to default";
            }
            ImGui.SetTooltip(hoverText);
        }
        
        return (changed, result);
    }
    
    /// <summary>
    /// Simple color picker with right-click to reset to default.
    /// </summary>
    /// <param name="label">The label/ID for the color picker.</param>
    /// <param name="color">The current color value.</param>
    /// <param name="defaultColor">The default color to reset to on right-click.</param>
    /// <param name="tooltip">Optional tooltip to show on hover.</param>
    /// <param name="flags">ImGui color edit flags.</param>
    /// <returns>Tuple of (changed, newColor).</returns>
    public static (bool changed, Vector4 newColor) ColorPickerWithReset(
        string label,
        Vector4 color,
        Vector4 defaultColor,
        string? tooltip = null,
        Dalamud.Bindings.ImGui.ImGuiColorEditFlags flags = Dalamud.Bindings.ImGui.ImGuiColorEditFlags.NoInputs | Dalamud.Bindings.ImGui.ImGuiColorEditFlags.AlphaPreviewHalf)
    {
        var changed = false;
        var result = color;
        
        if (ImGui.ColorEdit4(label, ref result, flags))
        {
            changed = true;
        }
        
        // Right-click to reset to default
        if (ImGui.IsItemClicked(Dalamud.Bindings.ImGui.ImGuiMouseButton.Right))
        {
            result = defaultColor;
            changed = true;
        }
        
        if (ImGui.IsItemHovered())
        {
            var hoverText = tooltip ?? "Color";
            if (result != defaultColor)
            {
                hoverText += "\nRight-click to reset to default";
            }
            ImGui.SetTooltip(hoverText);
        }
        
        return (changed, result);
    }
    
    /// <summary>
    /// Placeholder color shown when no custom color is set and the user hasn't clicked to edit.
    /// </summary>
    private static readonly Vector4 PlaceholderButtonColor = new(0.3f, 0.3f, 0.3f, 0.5f);
    
    /// <summary>
    /// Default initial color assigned when the user clicks the "Auto" placeholder to start editing.
    /// </summary>
    private static readonly Vector4 DefaultNewEditColor = new(1f, 1f, 1f, 1f);
    
    /// <summary>
    /// Draws an inline color editor with "Auto" placeholder and optional clear button.
    /// When no color is set and the user is not actively editing, a dimmed placeholder button
    /// labeled "Auto" is shown. Clicking it enters edit mode. Once a color is set or the user
    /// is editing, a full <c>ColorEdit4</c> picker is displayed. The clear button resets the
    /// color and exits edit mode.
    /// </summary>
    /// <typeparam name="TId">Type of the entity identifier (must be a value type).</typeparam>
    /// <param name="entityId">The identifier of the entity being edited.</param>
    /// <param name="editingId">
    /// Ref to the currently-editing entity state field. Set to <paramref name="entityId"/>
    /// when the user enters edit mode, and reset to <c>default</c> when editing completes.
    /// </param>
    /// <param name="colorEditBuffer">
    /// Ref to the shared <see cref="Vector4"/> buffer used while editing. Updated each frame
    /// as the user drags the color picker.
    /// </param>
    /// <param name="currentColorUint">The current uint color value, or <c>null</c> if unset ("Auto").</param>
    /// <param name="defaultDisplayColor">
    /// The <see cref="Vector4"/> color to display in the picker when no custom color is set
    /// (before the user starts editing).
    /// </param>
    /// <param name="onColorChanged">Callback invoked with the new uint color when the user finishes editing.</param>
    /// <param name="onColorCleared">Callback invoked when the user clicks the clear button.</param>
    /// <param name="drawClearButton">
    /// Whether to draw the "X" clear button inline. Set to <c>false</c> if the clear button
    /// is rendered in a separate column.
    /// </param>
    /// <returns><c>true</c> if the color was changed or cleared during this frame.</returns>
    public static bool InlineColorEditor<TId>(
        TId entityId,
        ref TId? editingId,
        ref Vector4 colorEditBuffer,
        uint? currentColorUint,
        Vector4 defaultDisplayColor,
        Action<uint> onColorChanged,
        Action onColorCleared,
        bool drawClearButton = true) where TId : struct
    {
        var isEditing = editingId.HasValue && EqualityComparer<TId>.Default.Equals(editingId.Value, entityId);
        var hasColor = currentColorUint.HasValue;
        var changed = false;

        // Resolve the display color
        Vector4 colorValue;
        if (isEditing)
            colorValue = colorEditBuffer;
        else if (hasColor)
            colorValue = ColorUtils.UintToVector4(currentColorUint!.Value);
        else
            colorValue = defaultDisplayColor;

        if (!hasColor && !isEditing)
        {
            // No color set and not editing — show placeholder "Auto" button
            if (ImGui.ColorButton("##colorPreview", PlaceholderButtonColor,
                ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20)))
            {
                editingId = entityId;
                colorEditBuffer = DefaultNewEditColor;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Click to set a custom color");
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
            ImGui.TextColored(DefaultColor, "Auto");
        }
        else
        {
            // Active color picker
            if (ImGui.ColorEdit4("##color", ref colorValue,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
            {
                colorEditBuffer = colorValue;
            }

            // Track when we start editing an already-set color
            if (ImGui.IsItemActivated() && hasColor)
            {
                editingId = entityId;
                colorEditBuffer = colorValue;
            }

            // Save when the user finishes editing
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                onColorChanged(ColorUtils.Vector4ToUint(colorEditBuffer));
                editingId = null;
                changed = true;
            }

            // Inline clear button
            if (drawClearButton && (hasColor || isEditing))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("X"))
                {
                    onColorCleared();
                    editingId = null;
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Clear custom color");
                    ImGui.EndTooltip();
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Draws an inline color editor that always shows the <c>ColorEdit4</c> picker (no "Auto" placeholder).
    /// Use this variant for entities that always have a color value.
    /// </summary>
    /// <typeparam name="TId">Type of the entity identifier (must be a value type).</typeparam>
    /// <param name="entityId">The identifier of the entity being edited.</param>
    /// <param name="editingId">
    /// Ref to the currently-editing entity state field. Nullable; set to <paramref name="entityId"/>
    /// when the user activates the picker.
    /// </param>
    /// <param name="colorEditBuffer">
    /// Ref to the shared <see cref="Vector4"/> buffer used while editing.
    /// </param>
    /// <param name="currentColorUint">The current uint color value.</param>
    /// <param name="onColorChanged">Callback invoked with the new uint color when the user finishes editing.</param>
    /// <returns><c>true</c> if the color was changed during this frame.</returns>
    public static bool InlineColorEditorAlwaysVisible<TId>(
        TId entityId,
        ref TId? editingId,
        ref Vector4 colorEditBuffer,
        uint currentColorUint,
        Action<uint> onColorChanged) where TId : struct
    {
        var isEditing = editingId.HasValue && EqualityComparer<TId>.Default.Equals(editingId.Value, entityId);
        var changed = false;

        var colorValue = isEditing
            ? colorEditBuffer
            : ColorUtils.UintToVector4(currentColorUint);

        if (ImGui.ColorEdit4("##color", ref colorValue,
            ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
        {
            colorEditBuffer = colorValue;
        }

        if (ImGui.IsItemActivated())
        {
            editingId = entityId;
            colorEditBuffer = colorValue;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            onColorChanged(ColorUtils.Vector4ToUint(colorEditBuffer));
            editingId = null;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Draws a clear/remove button for an inline color editor. Use alongside
    /// <see cref="InlineColorEditor{TId}"/> when <c>drawClearButton</c> is <c>false</c>,
    /// or with <see cref="InlineColorEditorAlwaysVisible{TId}"/> which never draws one.
    /// </summary>
    /// <typeparam name="TId">Type of the entity identifier.</typeparam>
    /// <param name="hasColor">Whether the entity currently has a color set.</param>
    /// <param name="editingId">Ref to the currently-editing entity state field.</param>
    /// <param name="onColorCleared">Callback invoked when the button is clicked.</param>
    /// <param name="tooltip">Tooltip text for the button. Defaults to "Clear custom color".</param>
    /// <returns><c>true</c> if the color was cleared during this frame.</returns>
    public static bool InlineColorClearButton<TId>(
        bool hasColor,
        ref TId? editingId,
        Action onColorCleared,
        string tooltip = "Clear custom color") where TId : struct
    {
        if (!hasColor && !editingId.HasValue)
            return false;

        if (ImGui.SmallButton("X"))
        {
            onColorCleared();
            editingId = null;
            return true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }
        return false;
    }
    
    private static readonly Vector4 DangerButtonColor = new(0.6f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 DangerButtonHoveredColor = new(0.8f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 DangerButtonActiveColor = new(0.9f, 0.2f, 0.2f, 1f);
    
    private static readonly Vector4 SuccessButtonColor = new(0.2f, 0.5f, 0.3f, 1f);
    private static readonly Vector4 SuccessButtonHoveredColor = new(0.3f, 0.6f, 0.4f, 1f);
    private static readonly Vector4 SuccessButtonActiveColor = new(0.2f, 0.7f, 0.4f, 1f);
    
    private static readonly Vector4 PrimaryButtonColor = new(0.3f, 0.3f, 0.5f, 1f);
    private static readonly Vector4 PrimaryButtonHoveredColor = new(0.4f, 0.4f, 0.6f, 1f);
    private static readonly Vector4 PrimaryButtonActiveColor = new(0.35f, 0.35f, 0.7f, 1f);
    
    /// <summary>
    /// Core helper: pushes 3 button style colors, invokes <paramref name="buttonFunc"/>, then pops.
    /// </summary>
    private static bool StyledButton(Vector4 normal, Vector4 hovered, Vector4 active, Func<bool> buttonFunc)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, normal);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, active);
        var clicked = buttonFunc();
        ImGui.PopStyleColor(3);
        return clicked;
    }

    /// <summary>Creates a danger-styled button (red).</summary>
    public static bool DangerButton(string label)
        => StyledButton(DangerButtonColor, DangerButtonHoveredColor, DangerButtonActiveColor, () => ImGui.Button(label));

    /// <summary>Creates a danger-styled small button (red).</summary>
    public static bool DangerSmallButton(string label)
        => StyledButton(DangerButtonColor, DangerButtonHoveredColor, DangerButtonActiveColor, () => ImGui.SmallButton(label));

    /// <summary>Creates a danger-styled button (red) with specified size.</summary>
    public static bool DangerButton(string label, Vector2 size)
        => StyledButton(DangerButtonColor, DangerButtonHoveredColor, DangerButtonActiveColor, () => ImGui.Button(label, size));

    /// <summary>Creates a success-styled button (green).</summary>
    public static bool SuccessButton(string label)
        => StyledButton(SuccessButtonColor, SuccessButtonHoveredColor, SuccessButtonActiveColor, () => ImGui.Button(label));

    /// <summary>Creates a success-styled button (green) with specified size.</summary>
    public static bool SuccessButton(string label, Vector2 size)
        => StyledButton(SuccessButtonColor, SuccessButtonHoveredColor, SuccessButtonActiveColor, () => ImGui.Button(label, size));

    /// <summary>Creates a primary-styled button (blue-purple).</summary>
    public static bool PrimaryButton(string label)
        => StyledButton(PrimaryButtonColor, PrimaryButtonHoveredColor, PrimaryButtonActiveColor, () => ImGui.Button(label));

    /// <summary>Creates a primary-styled button (blue-purple) with specified size.</summary>
    public static bool PrimaryButton(string label, Vector2 size)
        => StyledButton(PrimaryButtonColor, PrimaryButtonHoveredColor, PrimaryButtonActiveColor, () => ImGui.Button(label, size));
    
    /// <summary>
    /// Standard color for stat/info values — alias for <see cref="UiColors.Value"/>.
    /// </summary>
    public static readonly Vector4 StatValueColor = UiColors.Value;
    
    /// <summary>
    /// Dimmed color for secondary stat values.
    /// </summary>
    public static readonly Vector4 StatDimColor = new(0.6f, 0.6f, 0.6f, 1f);
    
    /// <summary>
    /// Draws a label-value row for statistics display.
    /// </summary>
    /// <param name="label">The label text.</param>
    /// <param name="value">The value to display.</param>
    /// <param name="valueColor">Optional custom color for the value.</param>
    /// <param name="labelWidth">Width at which to align the value. Default is 180.</param>
    public static void DrawStatRow(string label, string value, Vector4? valueColor = null, float labelWidth = 180f)
    {
        ImGui.TextUnformatted(label + ":");
        ImGui.SameLine(labelWidth);
        ImGui.TextColored(valueColor ?? StatValueColor, value);
    }
    
    /// <summary>
    /// Shows a "(?) " help marker with a hover tooltip (word-wrapped).
    /// Canonical implementation — replaces per-file copies in ConfigCategories and Widgets.
    /// </summary>
    /// <param name="desc">Tooltip description text.</param>
    /// <param name="sameLine">If true, calls <c>ImGui.SameLine()</c> before the marker.</param>
    /// <param name="wrapMultiplier">Font-size multiplier for text wrap width (default 20).</param>
    public static void HelpMarker(string desc, bool sameLine = false, float wrapMultiplier = 20f)
    {
        if (sameLine) ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * wrapMultiplier);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    /// Shows a hover tooltip with text wrapping for the previously drawn item.
    /// </summary>
    /// <param name="desc">Tooltip text.</param>
    /// <param name="wrapMultiplier">Font-size multiplier for text wrap width (default 20).</param>
    public static void HoverTooltip(string desc, float wrapMultiplier = 20f)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * wrapMultiplier);
        ImGui.TextUnformatted(desc);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>
    /// Shows a tooltip with a description and default value info.
    /// </summary>
    /// <param name="description">The description text.</param>
    /// <param name="defaultValue">The default value description.</param>
    public static void SettingTooltip(string description, string? defaultValue = null)
    {
        if (!ImGui.IsItemHovered()) return;
        
        ImGui.BeginTooltip();
        if (!string.IsNullOrEmpty(description))
            ImGui.TextUnformatted(description);
        if (!string.IsNullOrEmpty(defaultValue))
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Default: {defaultValue}");
        }
        ImGui.EndTooltip();
    }
}

