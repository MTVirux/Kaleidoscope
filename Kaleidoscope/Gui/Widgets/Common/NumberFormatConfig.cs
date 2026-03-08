namespace Kaleidoscope.Gui.Widgets.Common;

/// <summary>
/// Configuration for number formatting in widgets.
/// </summary>
public class NumberFormatConfig
{
    /// <summary>
    /// The formatting style to use.
    /// </summary>
    public virtual NumberFormatStyle Style { get; set; } = NumberFormatStyle.Compact;
    
    /// <summary>
    /// Number of decimal places to display (0-2).
    /// Primarily used for Compact style, but may apply to other styles.
    /// </summary>
    public virtual int DecimalPlaces { get; set; } = 2;
    
    /// <summary>
    /// Whether to use thousands separators in Standard mode.
    /// </summary>
    public virtual bool UseThousandsSeparator { get; set; } = true;
    
    /// <summary>
    /// Creates a deep copy of this configuration.
    /// </summary>
    public NumberFormatConfig Clone() => new()
    {
        Style = Style,
        DecimalPlaces = DecimalPlaces,
        UseThousandsSeparator = UseThousandsSeparator
    };
    
    /// <summary>
    /// Copies settings from another configuration.
    /// </summary>
    public void CopyFrom(NumberFormatConfig other)
    {
        Style = other.Style;
        DecimalPlaces = other.DecimalPlaces;
        UseThousandsSeparator = other.UseThousandsSeparator;
    }
    
    /// <summary>
    /// Compact configuration with 2 decimal places. Immutable — do not cast to mutate.
    /// </summary>
    public static readonly NumberFormatConfig Compact = new FrozenNumberFormatConfig();
    
    /// <summary>
    /// Default configuration — alias for <see cref="Compact"/>. Immutable — do not cast to mutate.
    /// </summary>
    public static readonly NumberFormatConfig Default = Compact;
}

/// <summary>
/// An immutable NumberFormatConfig that throws on attempted mutation.
/// Used for shared static instances to prevent accidental corruption.
/// </summary>
file sealed class FrozenNumberFormatConfig : NumberFormatConfig
{
    internal NumberFormatStyle _style = NumberFormatStyle.Compact;
    internal int _decimalPlaces = 2;
    internal bool _useThousandsSeparator = true;
    
    public override NumberFormatStyle Style
    {
        get => _style;
        set => throw new InvalidOperationException("Cannot mutate a shared static NumberFormatConfig instance. Use Clone() to create a mutable copy.");
    }
    
    public override int DecimalPlaces
    {
        get => _decimalPlaces;
        set => throw new InvalidOperationException("Cannot mutate a shared static NumberFormatConfig instance. Use Clone() to create a mutable copy.");
    }
    
    public override bool UseThousandsSeparator
    {
        get => _useThousandsSeparator;
        set => throw new InvalidOperationException("Cannot mutate a shared static NumberFormatConfig instance. Use Clone() to create a mutable copy.");
    }
}
