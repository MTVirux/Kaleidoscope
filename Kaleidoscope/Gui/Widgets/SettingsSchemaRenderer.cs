using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models.Settings;
using Kaleidoscope.Services;
using OtterGui.Text;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Renders settings UI from a SettingsSchema definition.
/// Handles all ImGui control types and change detection.
/// </summary>
public static class SettingsSchemaRenderer
{
    /// <summary>
    /// Draws all settings defined in the schema and returns whether any value changed.
    /// </summary>
    /// <typeparam name="TSettings">The settings class type.</typeparam>
    /// <param name="schema">The settings schema to render.</param>
    /// <param name="settings">The settings instance to read/write values.</param>
    /// <param name="showTooltips">Whether to show tooltips on hover.</param>
    /// <returns>True if any setting value was changed.</returns>
    public static bool Draw<TSettings>(SettingsSchema<TSettings> schema, TSettings settings, bool showTooltips = true)
        where TSettings : class
    {
        var anyChanged = false;
        
        foreach (var def in schema.Definitions)
        {
            try
            {
                if (def.SameLine)
                {
                    ImGui.SameLine();
                }
                
                var changed = DrawDefinition(def, settings, showTooltips);
                anyChanged |= changed;
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.UI, $"[SettingsSchemaRenderer] Error drawing {def.Key}: {ex.Message}");
            }
        }
        
        return anyChanged;
    }
    
    private static bool DrawDefinition<TSettings>(SettingDefinitionBase def, TSettings settings, bool showTooltips)
        where TSettings : class
    {
        return def switch
        {
            VisualSettingDefinition visual => DrawVisual(visual),
            SettingDefinition<TSettings, bool> boolDef => DrawCheckbox(boolDef, settings, showTooltips),
            SettingDefinition<TSettings, float> floatDef => DrawSliderFloat(floatDef, settings, showTooltips),
            SettingDefinition<TSettings, int> intDef => DrawSliderInt(intDef, settings, showTooltips),
            SettingDefinition<TSettings, string> stringDef => DrawTextInput(stringDef, settings, showTooltips),
            SettingDefinition<TSettings, Vector4> vecDef => DrawColorEdit(vecDef, settings, showTooltips),
            _ when def.GetType().IsGenericType && def.EnumType != null => DrawEnumControl(def, settings, showTooltips),
            _ => false
        };
    }
    
    private static bool DrawVisual(VisualSettingDefinition def)
    {
        switch (def.Type)
        {
            case SettingType.Separator:
                ImGui.Separator();
                break;
            case SettingType.Spacing:
                ImGui.Spacing();
                break;
            case SettingType.Header:
                if (!string.IsNullOrEmpty(def.HeaderText))
                {
                    ImGui.TextUnformatted(def.HeaderText);
                }
                break;
        }
        return false;
    }
    
    private static bool DrawCheckbox<TSettings>(SettingDefinition<TSettings, bool> def, TSettings settings, bool showTooltips)
        where TSettings : class
    {
        var value = def.Getter(settings);
        if (ImGui.Checkbox(def.Label, ref value))
        {
            def.Setter(settings, value);
            return true;
        }
        ShowTooltip(def, showTooltips);
        return false;
    }
    
    private static bool DrawSliderFloat<TSettings>(SettingDefinition<TSettings, float> def, TSettings settings, bool showTooltips)
        where TSettings : class
    {
        var value = def.Getter(settings);
        var min = def.Min ?? 0f;
        var max = def.Max ?? 100f;
        var format = def.Format ?? "%.1f";
        
        if (ImGui.SliderFloat(def.Label, ref value, min, max, format))
        {
            def.Setter(settings, value);
            return true;
        }
        ShowTooltip(def, showTooltips);
        return false;
    }
    
    private static bool DrawSliderInt<TSettings>(SettingDefinition<TSettings, int> def, TSettings settings, bool showTooltips)
        where TSettings : class
    {
        var value = def.Getter(settings);
        var min = (int)(def.Min ?? 0);
        var max = (int)(def.Max ?? 100);
        var format = def.Format ?? "%d";
        
        if (ImGui.SliderInt(def.Label, ref value, min, max, format))
        {
            def.Setter(settings, value);
            return true;
        }
        ShowTooltip(def, showTooltips);
        return false;
    }
    
    private static bool DrawTextInput<TSettings>(SettingDefinition<TSettings, string> def, TSettings settings, bool showTooltips)
        where TSettings : class
    {
        var value = def.Getter(settings) ?? string.Empty;
        var changed = false;
        var maxLen = (int)def.MaxLength;
        
        if (def.Type == SettingType.TextMultiline)
        {
            ImGui.TextUnformatted(def.Label);
            var size = def.MultilineSize ?? new Vector2(-1, 60);
            if (ImUtf8.InputMultiLine($"##{def.Key}", ref value, size))
            {
                def.Setter(settings, value);
                changed = true;
            }
        }
        else
        {
            if (ImGui.InputText(def.Label, ref value, maxLen))
            {
                def.Setter(settings, value);
                changed = true;
            }
        }
        
        ShowTooltip(def, showTooltips);
        return changed;
    }
    
    private static bool DrawColorEdit<TSettings>(SettingDefinition<TSettings, Vector4> def, TSettings settings, bool showTooltips)
        where TSettings : class
    {
        var value = def.Getter(settings);
        // DefaultValue will be the actual Vector4 or default(Vector4) - use Vector4.One as fallback
        var defaultValue = def.DefaultValue != default ? def.DefaultValue : Vector4.One;
        var (changed, newValue) = ImGuiHelpers.ColorPickerWithReset(
            def.Label, value, defaultValue, def.Tooltip);
        if (changed)
        {
            def.Setter(settings, newValue);
        }
        return changed;
    }
    
    private static bool DrawEnumControl<TSettings>(SettingDefinitionBase def, TSettings settings, bool showTooltips)
        where TSettings : class
    {
        if (def.EnumType == null) return false;
        
        // Get getter/setter via reflection
        var getterProp = def.GetType().GetProperty("Getter");
        var setterProp = def.GetType().GetProperty("Setter");
        var getter = getterProp?.GetValue(def) as Delegate;
        var setter = setterProp?.GetValue(def) as Delegate;
        
        if (getter == null || setter == null) return false;
        
        var currentValue = getter.DynamicInvoke(settings);
        if (currentValue == null) return false;
        
        var enumValues = Enum.GetValues(def.EnumType);
        var enumNames = def.GetType().GetProperty("EnumNames")?.GetValue(def) as string[]
            ?? Enum.GetNames(def.EnumType);
        var currentIndex = Array.IndexOf(enumValues, currentValue);
        
        var changed = false;
        
        if (def.Type == SettingType.RadioGroup)
        {
            ImGui.TextUnformatted(def.Label);
            for (var i = 0; i < enumValues.Length; i++)
            {
                var radioId = $"{enumNames[i]}##{def.Key}";
                if (ImGui.RadioButton(radioId, ref currentIndex, i))
                {
                    setter.DynamicInvoke(settings, enumValues.GetValue(i));
                    changed = true;
                }
                if (i < enumValues.Length - 1)
                {
                    ImGui.SameLine();
                }
            }
        }
        else // Combo
        {
            if (ImGui.Combo(def.Label, ref currentIndex, enumNames, enumNames.Length))
            {
                setter.DynamicInvoke(settings, enumValues.GetValue(currentIndex));
                changed = true;
            }
        }
        
        ShowTooltip(def, showTooltips);
        return changed;
    }
    
    private static void ShowTooltip(SettingDefinitionBase def, bool showTooltips)
    {
        if (!showTooltips || string.IsNullOrEmpty(def.Tooltip) && string.IsNullOrEmpty(def.DefaultText))
            return;
        
        if (!ImGui.IsItemHovered()) return;
        
        try
        {
            ImGui.BeginTooltip();
            if (!string.IsNullOrEmpty(def.Tooltip))
            {
                ImGui.TextUnformatted(def.Tooltip);
            }
            if (!string.IsNullOrEmpty(def.DefaultText))
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Default: {def.DefaultText}");
            }
            ImGui.EndTooltip();
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"[SettingsSchemaRenderer] Tooltip error: {ex.Message}");
        }
    }
}
