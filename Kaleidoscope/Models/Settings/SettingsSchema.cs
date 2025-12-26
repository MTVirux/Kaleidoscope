using System.Linq.Expressions;
using System.Numerics;

namespace Kaleidoscope.Models.Settings;

/// <summary>
/// Fluent builder for creating a settings schema with strongly-typed property bindings.
/// </summary>
/// <typeparam name="TSettings">The settings class type.</typeparam>
public sealed class SettingsSchema<TSettings> where TSettings : class
{
    private readonly List<SettingDefinitionBase> _definitions = new();
    
    /// <summary>
    /// Gets all setting definitions in the schema.
    /// </summary>
    public IReadOnlyList<SettingDefinitionBase> Definitions => _definitions;
    
    /// <summary>
    /// Adds a checkbox (boolean) setting.
    /// </summary>
    public SettingsSchema<TSettings> Checkbox(
        Expression<Func<TSettings, bool>> property,
        string label,
        string? tooltip = null,
        bool defaultValue = false,
        bool sameLine = false)
    {
        var def = SettingDefinition<TSettings, bool>.FromExpression(property, SettingType.Checkbox, label, tooltip, defaultValue);
        _definitions.Add(def with { SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds a float slider setting.
    /// </summary>
    public SettingsSchema<TSettings> SliderFloat(
        Expression<Func<TSettings, float>> property,
        string label,
        float min,
        float max,
        string? tooltip = null,
        string format = "%.1f",
        float defaultValue = 0f,
        bool sameLine = false)
    {
        var def = SettingDefinition<TSettings, float>.FromExpression(property, SettingType.SliderFloat, label, tooltip, defaultValue);
        _definitions.Add(def with { Min = min, Max = max, Format = format, SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds an integer slider setting.
    /// </summary>
    public SettingsSchema<TSettings> SliderInt(
        Expression<Func<TSettings, int>> property,
        string label,
        int min,
        int max,
        string? tooltip = null,
        string format = "%d",
        int defaultValue = 0,
        bool sameLine = false)
    {
        var def = SettingDefinition<TSettings, int>.FromExpression(property, SettingType.SliderInt, label, tooltip, defaultValue);
        _definitions.Add(def with { Min = min, Max = max, Format = format, SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds an enum combo box setting.
    /// </summary>
    public SettingsSchema<TSettings> Combo<TEnum>(
        Expression<Func<TSettings, TEnum>> property,
        string label,
        string? tooltip = null,
        TEnum defaultValue = default!,
        string[]? enumNames = null,
        bool sameLine = false)
        where TEnum : struct, Enum
    {
        var def = SettingDefinition<TSettings, TEnum>.FromExpression(property, SettingType.Combo, label, tooltip, defaultValue);
        _definitions.Add(def with { EnumType = typeof(TEnum), EnumNames = enumNames, SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds an enum radio button group setting.
    /// </summary>
    public SettingsSchema<TSettings> RadioGroup<TEnum>(
        Expression<Func<TSettings, TEnum>> property,
        string label,
        string? tooltip = null,
        TEnum defaultValue = default!,
        string[]? enumNames = null,
        bool sameLine = false)
        where TEnum : struct, Enum
    {
        var def = SettingDefinition<TSettings, TEnum>.FromExpression(property, SettingType.RadioGroup, label, tooltip, defaultValue);
        _definitions.Add(def with { EnumType = typeof(TEnum), EnumNames = enumNames, SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds a color picker setting.
    /// </summary>
    public SettingsSchema<TSettings> ColorEdit(
        Expression<Func<TSettings, Vector4>> property,
        string label,
        string? tooltip = null,
        Vector4? defaultValue = null,
        bool sameLine = false)
    {
        var def = SettingDefinition<TSettings, Vector4>.FromExpression(property, SettingType.ColorEdit, label, tooltip, defaultValue ?? Vector4.One);
        _definitions.Add(def with { SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds a single-line text input setting.
    /// </summary>
    public SettingsSchema<TSettings> TextInput(
        Expression<Func<TSettings, string>> property,
        string label,
        string? tooltip = null,
        string defaultValue = "",
        uint maxLength = 256,
        bool sameLine = false)
    {
        var def = SettingDefinition<TSettings, string>.FromExpression(property, SettingType.TextInput, label, tooltip, defaultValue);
        _definitions.Add(def with { MaxLength = maxLength, SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds a multi-line text input setting.
    /// </summary>
    public SettingsSchema<TSettings> TextMultiline(
        Expression<Func<TSettings, string>> property,
        string label,
        string? tooltip = null,
        string defaultValue = "",
        uint maxLength = 1024,
        Vector2? size = null,
        bool sameLine = false)
    {
        var def = SettingDefinition<TSettings, string>.FromExpression(property, SettingType.TextMultiline, label, tooltip, defaultValue);
        _definitions.Add(def with { MaxLength = maxLength, MultilineSize = size ?? new Vector2(-1, 60), SameLine = sameLine });
        return this;
    }
    
    /// <summary>
    /// Adds a visual separator.
    /// </summary>
    public SettingsSchema<TSettings> Separator()
    {
        _definitions.Add(new VisualSettingDefinition { Type = SettingType.Separator });
        return this;
    }
    
    /// <summary>
    /// Adds vertical spacing.
    /// </summary>
    public SettingsSchema<TSettings> Spacing()
    {
        _definitions.Add(new VisualSettingDefinition { Type = SettingType.Spacing });
        return this;
    }
    
    /// <summary>
    /// Adds a section header.
    /// </summary>
    public SettingsSchema<TSettings> Header(string text)
    {
        _definitions.Add(new VisualSettingDefinition { Type = SettingType.Header, HeaderText = text });
        return this;
    }
    
    /// <summary>
    /// Exports settings values to a dictionary for persistence.
    /// </summary>
    public Dictionary<string, object?> ToDictionary(TSettings settings)
    {
        var dict = new Dictionary<string, object?>();
        
        foreach (var def in _definitions)
        {
            if (def is VisualSettingDefinition) continue;
            
            var value = GetValue(def, settings);
            
            // Handle Vector4 specially - store as individual components for compatibility
            if (value is Vector4 vec)
            {
                dict[$"{def.Key}R"] = vec.X;
                dict[$"{def.Key}G"] = vec.Y;
                dict[$"{def.Key}B"] = vec.Z;
                dict[$"{def.Key}A"] = vec.W;
            }
            // Handle enums as int
            else if (value?.GetType().IsEnum == true)
            {
                dict[def.Key] = Convert.ToInt32(value);
            }
            else
            {
                dict[def.Key] = value;
            }
        }
        
        return dict;
    }
    
    /// <summary>
    /// Imports settings values from a dictionary.
    /// </summary>
    public void FromDictionary(TSettings settings, Dictionary<string, object?>? dict)
    {
        if (dict == null) return;
        
        foreach (var def in _definitions)
        {
            if (def is VisualSettingDefinition) continue;
            
            SetValueFromDict(def, settings, dict);
        }
    }
    
    private static object? GetValue(SettingDefinitionBase def, TSettings settings)
    {
        return def switch
        {
            SettingDefinition<TSettings, bool> d => d.Getter(settings),
            SettingDefinition<TSettings, float> d => d.Getter(settings),
            SettingDefinition<TSettings, int> d => d.Getter(settings),
            SettingDefinition<TSettings, string> d => d.Getter(settings),
            SettingDefinition<TSettings, Vector4> d => d.Getter(settings),
            _ when def.GetType().IsGenericType => GetValueReflection(def, settings),
            _ => null
        };
    }
    
    private static object? GetValueReflection(SettingDefinitionBase def, TSettings settings)
    {
        var getterProp = def.GetType().GetProperty("Getter");
        var getter = getterProp?.GetValue(def) as Delegate;
        return getter?.DynamicInvoke(settings);
    }
    
    private static void SetValueFromDict(SettingDefinitionBase def, TSettings settings, Dictionary<string, object?> dict)
    {
        // Handle Vector4 specially
        if (def is SettingDefinition<TSettings, Vector4> vecDef)
        {
            if (dict.TryGetValue($"{def.Key}R", out var r) &&
                dict.TryGetValue($"{def.Key}G", out var g) &&
                dict.TryGetValue($"{def.Key}B", out var b) &&
                dict.TryGetValue($"{def.Key}A", out var a))
            {
                var vec = new Vector4(
                    ConvertValue<float>(r),
                    ConvertValue<float>(g),
                    ConvertValue<float>(b),
                    ConvertValue<float>(a));
                vecDef.Setter(settings, vec);
            }
            return;
        }
        
        if (!dict.TryGetValue(def.Key, out var value) || value == null) return;
        
        switch (def)
        {
            case SettingDefinition<TSettings, bool> d:
                d.Setter(settings, ConvertValue<bool>(value));
                break;
            case SettingDefinition<TSettings, float> d:
                d.Setter(settings, ConvertValue<float>(value));
                break;
            case SettingDefinition<TSettings, int> d:
                d.Setter(settings, ConvertValue<int>(value));
                break;
            case SettingDefinition<TSettings, string> d:
                d.Setter(settings, ConvertValue<string>(value) ?? string.Empty);
                break;
            default:
                // Handle enum types via reflection
                if (def.GetType().IsGenericType && def.GetType().GetGenericArguments().LastOrDefault()?.IsEnum == true)
                {
                    SetEnumValueReflection(def, settings, value);
                }
                break;
        }
    }
    
    private static void SetEnumValueReflection(SettingDefinitionBase def, TSettings settings, object value)
    {
        var defType = def.GetType();
        var enumType = defType.GetGenericArguments().Last();
        var setterProp = defType.GetProperty("Setter");
        var setter = setterProp?.GetValue(def) as Delegate;
        
        if (setter == null) return;
        
        var intValue = ConvertValue<int>(value);
        var enumValue = Enum.ToObject(enumType, intValue);
        setter.DynamicInvoke(settings, enumValue);
    }
    
    private static T ConvertValue<T>(object? value)
    {
        if (value == null) return default!;
        
        // Handle Newtonsoft.Json JToken
        if (value is Newtonsoft.Json.Linq.JToken jToken)
        {
            return jToken.ToObject<T>() ?? default!;
        }
        
        // Handle System.Text.Json JsonElement
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(jsonElement.GetRawText()) ?? default!;
        }
        
        // Direct cast
        if (value is T typed)
            return typed;
        
        // Convert
        return (T)Convert.ChangeType(value, typeof(T));
    }
}

/// <summary>
/// Factory for creating typed settings schemas.
/// </summary>
public static class SettingsSchema
{
    /// <summary>
    /// Creates a new settings schema builder for the specified settings type.
    /// </summary>
    public static SettingsSchema<TSettings> For<TSettings>() where TSettings : class
        => new();
}
