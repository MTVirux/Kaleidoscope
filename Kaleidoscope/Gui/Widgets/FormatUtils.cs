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
    /// Formats a gil amount with K/M/B abbreviation (double overload for averages).
    /// </summary>
    /// <param name="amount">The amount to format.</param>
    /// <returns>Formatted string like "1.5M" or "500K".</returns>
    public static string FormatGil(double amount)
    {
        return amount switch
        {
            >= 1_000_000_000 => $"{amount / 1_000_000_000.0:F2}B",
            >= 1_000_000 => $"{amount / 1_000_000.0:F1}M",
            >= 1_000 => $"{amount / 1_000.0:F1}K",
            _ => $"{amount:N0}"
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

    /// <summary>
    /// Formats a time span as a relative time string (e.g., "5m ago", "2h ago").
    /// </summary>
    /// <param name="timeSince">The time span since the event.</param>
    /// <returns>Formatted string like "just now", "5m ago", "2h ago", "3d ago".</returns>
    public static string FormatTimeAgo(TimeSpan timeSince)
    {
        if (timeSince.TotalMinutes < 1)
            return "just now";
        if (timeSince.TotalHours < 1)
            return $"{(int)timeSince.TotalMinutes}m ago";
        if (timeSince.TotalDays < 1)
            return $"{(int)timeSince.TotalHours}h ago";
        return $"{(int)timeSince.TotalDays}d ago";
    }

    /// <summary>
    /// Formats a DateTime as a relative time string, with fallback to date format for older dates.
    /// </summary>
    /// <param name="dateTime">The date/time to format relative to now.</param>
    /// <returns>Formatted string like "Just now", "5m ago", "2d ago", "Jan 15".</returns>
    public static string FormatTimeAgo(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;

        if (span.TotalMinutes < 1)
            return "Just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)}w ago";

        return dateTime.ToString("MMM d");
    }

    /// <summary>
    /// Converts HSV color values to an RGB Vector4.
    /// </summary>
    /// <param name="h">Hue (0-1).</param>
    /// <param name="s">Saturation (0-1).</param>
    /// <param name="v">Value/Brightness (0-1).</param>
    /// <returns>RGB color as Vector4 with alpha = 1.</returns>
    public static System.Numerics.Vector4 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;

        int i = (int)(h * 6);
        float f = h * 6 - i;
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float t = v * (1 - (1 - f) * s);

        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }

        return new System.Numerics.Vector4(r, g, b, 1f);
    }
}
