using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets.Common;

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
    public static string FormatGil(long amount) => FormatGil((double)amount);

    /// <summary>
    /// Formats a gil amount with K/M/B abbreviation (double overload for averages).
    /// </summary>
    /// <param name="amount">The amount to format.</param>
    /// <returns>Formatted string like "1.5M" or "500K".</returns>
    public static string FormatGil(double amount) => NumberFormatter.FormatGil(amount);

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
    /// Formats a byte size in a human-readable format (B, KB, MB, GB).
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>Formatted string like "1.5 MB" or "500 KB".</returns>
    public static string FormatByteSize(long bytes)
    {
        if (bytes < 0)
            return "Unknown";

        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";

        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F2} MB";

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    /// <summary>
    /// Formats a countdown timer for display (e.g., "1:23:45" or "5:30").
    /// Used for retainer ventures, submersible voyages, etc.
    /// </summary>
    /// <param name="secondsRemaining">Seconds remaining on the timer.</param>
    /// <returns>Formatted string like "1:23:45" or "5:30".</returns>
    public static string FormatCountdown(long secondsRemaining) =>
        FormatCountdown(TimeSpan.FromSeconds(secondsRemaining));

    /// <summary>
    /// Formats a countdown timer with millisecond precision for real-time display.
    /// Used for venture/voyage status tools with frequent updates.
    /// </summary>
    /// <param name="endTimeUnix">Unix timestamp when the timer ends.</param>
    /// <param name="nowUnix">Current unix timestamp in seconds.</param>
    /// <param name="nowMs">Current time in milliseconds for precision.</param>
    /// <returns>Formatted string like "1:23:45.678" or "Ready!".</returns>
    public static string FormatCountdownPrecise(long endTimeUnix, long nowUnix, long nowMs)
    {
        var endTimeMs = endTimeUnix * 1000;
        var remainingMs = endTimeMs - nowMs;

        if (remainingMs <= 0)
            return "Ready!";

        var span = TimeSpan.FromMilliseconds(remainingMs);

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}.{span.Milliseconds:D3}";
        }
        
        if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}:{span.Seconds:D2}.{span.Milliseconds:D3}";
        }
        
        return $"{span.Seconds}.{span.Milliseconds:D3}";
    }

    /// <summary>
    /// Formats a countdown timer from a TimeSpan.
    /// </summary>
    /// <param name="timeSpan">The remaining time.</param>
    /// <returns>Formatted string like "1:23:45" or "5:30".</returns>
    public static string FormatCountdown(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds <= 0)
            return "Ready!";

        if (timeSpan.TotalHours >= 1)
        {
            return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }
        
        return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }

    /// <summary>
    /// Converts HSV color values to an RGB Vector4.
    /// </summary>
    /// <param name="h">Hue (0-1).</param>
    /// <param name="s">Saturation (0-1).</param>
    /// <param name="v">Value/Brightness (0-1).</param>
    /// <returns>RGB color as Vector4 with alpha = 1.</returns>
    public static System.Numerics.Vector4 HsvToRgb(float h, float s, float v)
        => ColorUtils.HsvToRgb(h, s, v);
}
