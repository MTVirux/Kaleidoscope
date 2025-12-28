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
}
