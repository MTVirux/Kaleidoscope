using System.Globalization;

namespace Kaleidoscope.Gui.Widgets.Common;

/// <summary>
/// Centralized number formatting utility that applies NumberFormatConfig settings.
/// Use this class for all number formatting.
/// </summary>
public static class NumberFormatter
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
    
    /// <summary>
    /// Formats a number according to the specified configuration.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="config">The format configuration. If null, uses default (Standard).</param>
    /// <returns>Formatted string representation of the number.</returns>
    public static string Format(double value, NumberFormatConfig? config = null)
    {
        config ??= NumberFormatConfig.Default;
        
        return config.Style switch
        {
            NumberFormatStyle.Compact => FormatCompact(value, config.DecimalPlaces),
            NumberFormatStyle.Raw => FormatRaw(value),
            _ => FormatStandard(value, config.UseThousandsSeparator)
        };
    }
    
    /// <summary>
    /// Formats a long value according to the specified configuration.
    /// </summary>
    public static string Format(long value, NumberFormatConfig? config = null) 
        => Format((double)value, config);
    
    /// <summary>
    /// Formats a float value according to the specified configuration.
    /// </summary>
    public static string Format(float value, NumberFormatConfig? config = null) 
        => Format((double)value, config);
    
    /// <summary>
    /// Formats a number in compact notation with K/M/B suffixes.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="decimals">Number of decimal places (0-2).</param>
    /// <returns>Compact formatted string like "1.5M" or "10K".</returns>
    public static string FormatCompact(double value, int decimals = 2)
    {
        decimals = Math.Clamp(decimals, 0, 2);
        var format = GetDecimalFormat(decimals);
        var absValue = Math.Abs(value);
        var sign = value < 0 ? -1.0 : 1.0;
        var factor = Math.Pow(10, decimals);

        double Trunc(double divided) =>
            Math.Truncate(divided * factor) / factor * sign;

        return absValue switch
        {
            >= 1_000_000_000 => $"{Trunc(absValue / 1_000_000_000).ToString(format, InvariantCulture)}B",
            >= 1_000_000 => $"{Trunc(absValue / 1_000_000).ToString(format, InvariantCulture)}M",
            >= 1_000 => $"{Trunc(absValue / 1_000).ToString(format, InvariantCulture)}K",
            _ => value.ToString(format, InvariantCulture)
        };
    }
    
    /// <summary>
    /// Formats a number with standard thousands separators.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="useSeparators">Whether to include thousands separators.</param>
    /// <returns>Formatted string like "1,234,567".</returns>
    public static string FormatStandard(double value, bool useSeparators = true)
    {
        // Check if the value is essentially an integer
        var isInteger = Math.Abs(value - Math.Truncate(value)) < 0.0001;
        
        if (isInteger)
        {
            return useSeparators 
                ? ((long)value).ToString("N0", InvariantCulture)
                : ((long)value).ToString(InvariantCulture);
        }
        
        return useSeparators 
            ? value.ToString("N2", InvariantCulture) 
            : value.ToString("F2", InvariantCulture);
    }
    
    /// <summary>
    /// Formats a number without any formatting.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>Raw string representation.</returns>
    public static string FormatRaw(double value)
    {
        return value.ToString(InvariantCulture);
    }
    
    /// <summary>
    /// Gets the decimal format string for the specified number of decimal places.
    /// </summary>
    private static string GetDecimalFormat(int decimals) => decimals switch
    {
        0 => "0",
        1 => "0.#",
        _ => "0.##"
    };
}
