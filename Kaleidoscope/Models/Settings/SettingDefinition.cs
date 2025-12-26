using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace Kaleidoscope.Models.Settings;

/// <summary>
/// Base class for setting definitions. Contains metadata and rendering configuration.
/// </summary>
public abstract record SettingDefinitionBase
{
    /// <summary>The unique key for this setting (property name).</summary>
    public string Key { get; init; } = string.Empty;
    
    /// <summary>The display label shown in the UI.</summary>
    public string Label { get; init; } = string.Empty;
    
    /// <summary>Tooltip description shown on hover.</summary>
    public string? Tooltip { get; init; }
    
    /// <summary>Default value text shown in tooltip.</summary>
    public string? DefaultText { get; init; }
    
    /// <summary>The type of UI control to render.</summary>
    public SettingType Type { get; init; }
    
    /// <summary>Whether this setting should be sameline with the previous.</summary>
    public bool SameLine { get; init; }
    
    /// <summary>For enum types: the enum type.</summary>
    public Type? EnumType { get; init; }
}

/// <summary>
/// Generic setting definition with strongly-typed getter/setter delegates.
/// </summary>
/// <typeparam name="TSettings">The settings class type.</typeparam>
/// <typeparam name="TValue">The property value type.</typeparam>
public sealed record SettingDefinition<TSettings, TValue> : SettingDefinitionBase
    where TSettings : class
{
    /// <summary>Compiled getter for the property.</summary>
    public Func<TSettings, TValue> Getter { get; init; } = null!;
    
    /// <summary>Compiled setter for the property.</summary>
    public Action<TSettings, TValue> Setter { get; init; } = null!;
    
    /// <summary>Default value for the property.</summary>
    public TValue? DefaultValue { get; init; }
    
    // Type-specific configuration
    
    /// <summary>For sliders: minimum value.</summary>
    public float? Min { get; init; }
    
    /// <summary>For sliders: maximum value.</summary>
    public float? Max { get; init; }
    
    /// <summary>For sliders: display format string.</summary>
    public string? Format { get; init; }
    
    /// <summary>For text input: maximum character length.</summary>
    public uint MaxLength { get; init; } = 256;
    
    /// <summary>For multiline text: input box size.</summary>
    public Vector2? MultilineSize { get; init; }
    
    /// <summary>For radio groups: display names for enum values (optional).</summary>
    public string[]? EnumNames { get; init; }
    
    /// <summary>
    /// Creates a SettingDefinition from a property expression with compiled accessors.
    /// </summary>
    public static SettingDefinition<TSettings, TValue> FromExpression(
        Expression<Func<TSettings, TValue>> propertyExpression,
        SettingType type,
        string label,
        string? tooltip = null,
        TValue? defaultValue = default)
    {
        var memberExpression = propertyExpression.Body as MemberExpression
            ?? throw new ArgumentException("Expression must be a property access", nameof(propertyExpression));
        
        var propertyInfo = memberExpression.Member as PropertyInfo
            ?? throw new ArgumentException("Expression must be a property access", nameof(propertyExpression));
        
        // Compile getter
        var getter = propertyExpression.Compile();
        
        // Build setter
        var instanceParam = Expression.Parameter(typeof(TSettings), "instance");
        var valueParam = Expression.Parameter(typeof(TValue), "value");
        var propertyAccess = Expression.Property(instanceParam, propertyInfo);
        var assign = Expression.Assign(propertyAccess, valueParam);
        var setter = Expression.Lambda<Action<TSettings, TValue>>(assign, instanceParam, valueParam).Compile();
        
        return new SettingDefinition<TSettings, TValue>
        {
            Key = propertyInfo.Name,
            Label = label,
            Tooltip = tooltip,
            DefaultText = defaultValue?.ToString(),
            DefaultValue = defaultValue,
            Type = type,
            Getter = getter,
            Setter = setter,
            EnumType = typeof(TValue).IsEnum ? typeof(TValue) : null
        };
    }
}

/// <summary>
/// A non-value setting definition for visual elements (separators, headers, spacing).
/// </summary>
public sealed record VisualSettingDefinition : SettingDefinitionBase
{
    /// <summary>For headers: the header text.</summary>
    public string? HeaderText { get; init; }
}
