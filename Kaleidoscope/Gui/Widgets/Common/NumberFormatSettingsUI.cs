using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Common;

/// <summary>
/// Reusable ImGui UI components for number format settings.
/// </summary>
public static class NumberFormatSettingsUI
{
    private static readonly string[] StyleNames = { "Standard (1,234,567)", "Compact (1.5M)", "Raw (1234567)" };
    
    /// <summary>
    /// Draws the complete number format settings UI.
    /// </summary>
    /// <param name="id">Unique ID for ImGui elements.</param>
    /// <param name="config">The configuration to modify.</param>
    /// <param name="label">Optional label for the combo box. Defaults to "Number Format".</param>
    /// <returns>True if any setting was changed.</returns>
    public static bool Draw(string id, NumberFormatConfig config, string label = "Number Format")
    {
        var changed = false;
        
        // Style combo
        var style = (int)config.Style;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.Combo($"{label}##{id}", ref style, StyleNames, StyleNames.Length))
        {
            config.Style = (NumberFormatStyle)style;
            changed = true;
        }
        
        // Decimal places (only for Compact mode)
        if (config.Style == NumberFormatStyle.Compact)
        {
            var decimals = config.DecimalPlaces;
            ImGui.SetNextItemWidth(100f);
            if (ImGui.SliderInt($"Decimal Places##{id}", ref decimals, 0, 2))
            {
                config.DecimalPlaces = decimals;
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Number of decimal places in compact format.\n0 = 1M, 1 = 1.5M, 2 = 1.50M");
            }
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws a compact inline version of the settings (single row).
    /// </summary>
    /// <param name="id">Unique ID for ImGui elements.</param>
    /// <param name="config">The configuration to modify.</param>
    /// <returns>True if any setting was changed.</returns>
    public static bool DrawInline(string id, NumberFormatConfig config)
    {
        var changed = false;
        
        var style = (int)config.Style;
        ImGui.SetNextItemWidth(140f);
        if (ImGui.Combo($"##fmt_{id}", ref style, StyleNames, StyleNames.Length))
        {
            config.Style = (NumberFormatStyle)style;
            changed = true;
        }
        
        if (config.Style == NumberFormatStyle.Compact)
        {
            ImGui.SameLine();
            var decimals = config.DecimalPlaces;
            ImGui.SetNextItemWidth(60f);
            if (ImGui.SliderInt($"##dec_{id}", ref decimals, 0, 2))
            {
                config.DecimalPlaces = decimals;
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Decimal places");
            }
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws a labeled inline version with a label prefix.
    /// </summary>
    /// <param name="label">Label to display before the controls.</param>
    /// <param name="id">Unique ID for ImGui elements.</param>
    /// <param name="config">The configuration to modify.</param>
    /// <returns>True if any setting was changed.</returns>
    public static bool DrawLabeled(string label, string id, NumberFormatConfig config)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        return DrawInline(id, config);
    }
}
