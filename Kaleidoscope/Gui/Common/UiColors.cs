using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Common;

/// <summary>
/// Shared UI color constants for consistent styling across the plugin.
/// Provides semantic color names for common status and information display.
/// </summary>
public static class UiColors
{
    // Status colors
    /// <summary>Connected/Success status - green.</summary>
    public static readonly Vector4 Connected = new(0.2f, 0.8f, 0.2f, 1f);
    
    /// <summary>Alias for Connected - semantic name for "good" state.</summary>
    public static readonly Vector4 Good = Connected;
    
    /// <summary>Disconnected/Error status - red.</summary>
    public static readonly Vector4 Disconnected = new(0.8f, 0.2f, 0.2f, 1f);
    
    /// <summary>Alias for Disconnected - semantic name for "bad" state.</summary>
    public static readonly Vector4 Bad = Disconnected;
    
    /// <summary>Alias for Disconnected - semantic name for error state.</summary>
    public static readonly Vector4 Error = Disconnected;
    
    /// <summary>Warning status - yellow/amber.</summary>
    public static readonly Vector4 Warning = new(0.9f, 0.7f, 0.2f, 1f);
    
    /// <summary>Disabled/Inactive status - gray.</summary>
    public static readonly Vector4 Disabled = new(0.5f, 0.5f, 0.5f, 1f);

    // Text colors
    /// <summary>Informational text - muted gray.</summary>
    public static readonly Vector4 Info = new(0.7f, 0.7f, 0.7f, 1f);
    
    /// <summary>Primary value display - bright white.</summary>
    public static readonly Vector4 Value = new(0.9f, 0.9f, 0.9f, 1f);
    
    /// <summary>Secondary/muted text.</summary>
    public static readonly Vector4 Muted = new(0.5f, 0.5f, 0.5f, 1f);
    
    /// <summary>Highlighted text - blue.</summary>
    public static readonly Vector4 Highlight = new(0.4f, 0.6f, 1.0f, 1f);

    // Size tier colors (for file/memory size displays)
    /// <summary>Small size - green (good).</summary>
    public static readonly Vector4 SizeSmall = Good;
    
    /// <summary>Normal size - white (neutral).</summary>
    public static readonly Vector4 SizeNormal = Value;
    
    /// <summary>Large size - yellow (warning).</summary>
    public static readonly Vector4 SizeLarge = Warning;
    
    /// <summary>Very large size - orange (concern).</summary>
    public static readonly Vector4 SizeVeryLarge = new(0.8f, 0.4f, 0.2f, 1f);

    /// <summary>
    /// Gets an appropriate color for a byte size value.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <param name="smallThreshold">Threshold for "small" (green). Default 10 MB.</param>
    /// <param name="normalThreshold">Threshold for "normal" (white). Default 50 MB.</param>
    /// <param name="largeThreshold">Threshold for "large" (yellow). Default 100 MB.</param>
    /// <returns>Appropriate color for the size tier.</returns>
    public static Vector4 GetSizeColor(long bytes, 
        long smallThreshold = 10 * 1024 * 1024,
        long normalThreshold = 50 * 1024 * 1024,
        long largeThreshold = 100 * 1024 * 1024)
    {
        if (bytes < smallThreshold)
            return SizeSmall;
        if (bytes < normalThreshold)
            return SizeNormal;
        if (bytes < largeThreshold)
            return SizeLarge;
        return SizeVeryLarge;
    }

    /// <summary>
    /// Draws a status indicator with a colored icon, status text, and optional tooltip.
    /// Reduces code duplication across status tools.
    /// </summary>
    /// <param name="isConnected">Whether the status represents a connected/positive state.</param>
    /// <param name="status">The status text to display.</param>
    /// <param name="tooltip">Optional tooltip shown on hover.</param>
    /// <param name="overrideColor">Optional color override (otherwise uses Connected/Disconnected).</param>
    public static void DrawStatusIndicator(bool isConnected, string status, string? tooltip = null, Vector4? overrideColor = null)
    {
        var color = overrideColor ?? (isConnected ? Connected : Disconnected);
        var icon = isConnected ? "●" : "○";
        
        ImGui.TextColored(color, icon);
        ImGui.SameLine();
        ImGui.TextUnformatted(status);
        
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(tooltip))
        {
            ImGui.SetTooltip(tooltip);
        }
    }
}
