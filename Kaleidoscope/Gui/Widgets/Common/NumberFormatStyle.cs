namespace Kaleidoscope.Gui.Widgets.Common;

/// <summary>
/// Number formatting style options for widgets.
/// </summary>
public enum NumberFormatStyle
{
    /// <summary>
    /// Standard formatted with thousands separators (123,456,789).
    /// </summary>
    Standard,
    
    /// <summary>
    /// Compact format with K/M/B suffixes (1.5M, 10K).
    /// </summary>
    Compact,
    
    /// <summary>
    /// Raw unformatted number without any grouping or abbreviation.
    /// </summary>
    Raw,
    
    // Future options can be added here:
    // Scientific,   // 1.23e6
    // Percentage,   // 45.5%
    // Currency,     // $1,234.56
}
