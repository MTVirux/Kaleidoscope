namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Utility class for formatting values in a human-readable way.
/// Provides consistent formatting for gil amounts, large numbers, and other values.
/// </summary>
public static class FormatUtils
{
    /// <summary>
    /// Formats a gil amount with K/M/B abbreviation.
    /// </summary>
    /// <param name="amount">The amount to format.</param>
    /// <returns>Formatted string like "1.5M" or "500K".</returns>
    public static string FormatGil(long amount)
    {
        return amount switch
        {
            >= 1_000_000_000 => $"{amount / 1_000_000_000.0:F2}B",
            >= 1_000_000 => $"{amount / 1_000_000.0:F1}M",
            >= 1_000 => $"{amount / 1_000.0:F1}K",
            _ => amount.ToString("N0")
        };
    }

    /// <summary>
    /// Formats a numeric value with K/M/B abbreviation.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>Formatted string like "1.5M" or "500K".</returns>
    public static string FormatAbbreviated(double value)
    {
        return value switch
        {
            >= 1_000_000_000 => $"{value / 1_000_000_000:0.##}B",
            >= 1_000_000 => $"{value / 1_000_000:0.##}M",
            >= 1_000 => $"{value / 1_000:0.##}K",
            _ => $"{value:0.##}"
        };
    }

    /// <summary>
    /// Formats a numeric value with K/M/B abbreviation (integer overload).
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>Formatted string like "1.5M" or "500K".</returns>
    public static string FormatAbbreviated(long value)
    {
        return FormatAbbreviated((double)value);
    }

    /// <summary>
    /// Formats a quantity with optional suffix.
    /// </summary>
    /// <param name="quantity">The quantity to format.</param>
    /// <param name="suffix">Optional suffix like "items" or "units".</param>
    /// <returns>Formatted string.</returns>
    public static string FormatQuantity(long quantity, string? suffix = null)
    {
        var formatted = FormatAbbreviated(quantity);
        return string.IsNullOrEmpty(suffix) ? formatted : $"{formatted} {suffix}";
    }

    /// <summary>
    /// Formats a percentage value.
    /// </summary>
    /// <param name="value">The percentage value (0-100 scale).</param>
    /// <param name="decimals">Number of decimal places.</param>
    /// <returns>Formatted string like "45.5%".</returns>
    public static string FormatPercentage(double value, int decimals = 1)
    {
        return $"{value.ToString($"F{decimals}")}%";
    }

    /// <summary>
    /// Formats a time duration in a human-readable way.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>Formatted string like "2h 30m" or "5d 12h".</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalSeconds}s";
    }
}
