using System.Numerics;
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
    public const float DefaultButtonPadding = 12f;
    
    /// <summary>
    /// Calculates the width of a button based on its text content plus padding.
    /// </summary>
    /// <param name="text">The button text (can include ## for ID suffix).</param>
    /// <param name="padding">Horizontal padding added to each side. Defaults to DefaultButtonPadding.</param>
    /// <returns>The calculated button width.</returns>
    public static float CalcButtonWidth(string text, float padding = DefaultButtonPadding)
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
    /// Default color used when no color is set or when cleared.
    /// </summary>
    public static readonly Vector4 DefaultColor = new(0.5f, 0.5f, 0.5f, 1f);
    
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
    
    #region Styled Buttons
    
    /// <summary>Standard danger button color (red).</summary>
    public static readonly Vector4 DangerButtonColor = new(0.6f, 0.2f, 0.2f, 1f);
    /// <summary>Standard danger button hover color.</summary>
    public static readonly Vector4 DangerButtonHoveredColor = new(0.8f, 0.3f, 0.3f, 1f);
    /// <summary>Standard danger button active color.</summary>
    public static readonly Vector4 DangerButtonActiveColor = new(0.9f, 0.2f, 0.2f, 1f);
    
    /// <summary>Standard success button color (green).</summary>
    public static readonly Vector4 SuccessButtonColor = new(0.2f, 0.5f, 0.3f, 1f);
    /// <summary>Standard success button hover color.</summary>
    public static readonly Vector4 SuccessButtonHoveredColor = new(0.3f, 0.6f, 0.4f, 1f);
    /// <summary>Standard success button active color.</summary>
    public static readonly Vector4 SuccessButtonActiveColor = new(0.2f, 0.7f, 0.4f, 1f);
    
    /// <summary>Standard primary button color (blue-purple).</summary>
    public static readonly Vector4 PrimaryButtonColor = new(0.3f, 0.3f, 0.5f, 1f);
    /// <summary>Standard primary button hover color.</summary>
    public static readonly Vector4 PrimaryButtonHoveredColor = new(0.4f, 0.4f, 0.6f, 1f);
    /// <summary>Standard primary button active color.</summary>
    public static readonly Vector4 PrimaryButtonActiveColor = new(0.35f, 0.35f, 0.7f, 1f);
    
    /// <summary>
    /// Creates a danger-styled button (red).
    /// </summary>
    /// <param name="label">Button label.</param>
    /// <returns>True if clicked.</returns>
    public static bool DangerButton(string label)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, DangerButtonColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, DangerButtonHoveredColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, DangerButtonActiveColor);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        return clicked;
    }
    
    /// <summary>
    /// Creates a danger-styled small button (red).
    /// </summary>
    public static bool DangerSmallButton(string label)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, DangerButtonColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, DangerButtonHoveredColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, DangerButtonActiveColor);
        var clicked = ImGui.SmallButton(label);
        ImGui.PopStyleColor(3);
        return clicked;
    }
    
    /// <summary>
    /// Creates a danger-styled button (red) with specified size.
    /// </summary>
    public static bool DangerButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, DangerButtonColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, DangerButtonHoveredColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, DangerButtonActiveColor);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }
    
    /// <summary>
    /// Creates a success-styled button (green).
    /// </summary>
    public static bool SuccessButton(string label)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, SuccessButtonColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, SuccessButtonHoveredColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, SuccessButtonActiveColor);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        return clicked;
    }
    
    /// <summary>
    /// Creates a success-styled button (green) with specified size.
    /// </summary>
    public static bool SuccessButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, SuccessButtonColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, SuccessButtonHoveredColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, SuccessButtonActiveColor);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }
    
    /// <summary>
    /// Creates a primary-styled button (blue-purple).
    /// </summary>
    public static bool PrimaryButton(string label)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, PrimaryButtonColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, PrimaryButtonHoveredColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, PrimaryButtonActiveColor);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(3);
        return clicked;
    }
    
    /// <summary>
    /// Creates a primary-styled button (blue-purple) with specified size.
    /// </summary>
    public static bool PrimaryButton(string label, Vector2 size)
    {
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.Button, PrimaryButtonColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonHovered, PrimaryButtonHoveredColor);
        ImGui.PushStyleColor(Dalamud.Bindings.ImGui.ImGuiCol.ButtonActive, PrimaryButtonActiveColor);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }
    
    #endregion
    
    #region Stat Display
    
    /// <summary>
    /// Standard color for stat/info labels.
    /// </summary>
    public static readonly Vector4 StatLabelColor = new(0.7f, 0.7f, 0.7f, 1f);
    
    /// <summary>
    /// Standard color for stat/info values.
    /// </summary>
    public static readonly Vector4 StatValueColor = new(0.9f, 0.9f, 0.9f, 1f);
    
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
    
    #endregion
}

