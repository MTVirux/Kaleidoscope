using Kaleidoscope.Gui.Widgets.Graph;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Utility for converting time range settings to TimeSpan values.
/// </summary>
public static class TimeRangeSelectorWidget
{
    /// <summary>
    /// Calculates the TimeSpan for the given time range settings.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The time unit.</param>
    /// <returns>The calculated TimeSpan, or null if unit is All.</returns>
    public static TimeSpan? GetTimeSpan(int value, TimeUnit unit)
    {
        return unit switch
        {
            TimeUnit.Seconds => TimeSpan.FromSeconds(value),
            TimeUnit.Minutes => TimeSpan.FromMinutes(value),
            TimeUnit.Hours => TimeSpan.FromHours(value),
            TimeUnit.Days => TimeSpan.FromDays(value),
            TimeUnit.Weeks => TimeSpan.FromDays(value * 7),
            TimeUnit.Months => TimeSpan.FromDays(value * 30),
            TimeUnit.All => null,
            _ => null
        };
    }
}
