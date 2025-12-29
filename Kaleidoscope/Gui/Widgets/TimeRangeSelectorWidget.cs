using Dalamud.Bindings.ImGui;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable time range selector widget combining value input with unit selection.
/// Used for filtering time-series data in graphs and data displays.
/// </summary>
public static class TimeRangeSelectorWidget
{
    /// <summary>Display names for time range units (excludes Seconds for data range selection).</summary>
    private static readonly string[] TimeRangeUnitNames = { "Minutes", "Hours", "Days", "Weeks", "Months", "All (no limit)" };
    
    /// <summary>Offset to skip Seconds when indexing into TimeUnit enum for range selection.</summary>
    private const int TimeRangeUnitOffset = 1;

    /// <summary>
    /// Draws a time range selector with value input and unit dropdown.
    /// </summary>
    /// <param name="label">Label prefix for the controls.</param>
    /// <param name="timeRangeValue">Reference to the time range value.</param>
    /// <param name="timeRangeUnit">Reference to the time range unit.</param>
    /// <param name="valueWidth">Width of the value input field.</param>
    /// <param name="unitWidth">Width of the unit dropdown.</param>
    /// <returns>True if either value changed.</returns>
    public static bool Draw(
        string label,
        ref int timeRangeValue,
        ref TimeUnit timeRangeUnit,
        float valueWidth = 100f,
        float unitWidth = 150f)
    {
        bool changed = false;

        ImGui.PushID(label);

        // Unit dropdown first (skip Seconds, so offset by 1)
        ImGui.SetNextItemWidth(unitWidth);
        var unitIndex = (int)timeRangeUnit - TimeRangeUnitOffset;
        if (unitIndex < 0) unitIndex = 0; // Clamp Seconds to Minutes
        if (ImGui.Combo($"##Unit", ref unitIndex, TimeRangeUnitNames, TimeRangeUnitNames.Length))
        {
            timeRangeUnit = (TimeUnit)(unitIndex + TimeRangeUnitOffset);
            changed = true;
        }

        // Only show value input if not "All"
        if (timeRangeUnit != TimeUnit.All)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(valueWidth);
            var value = timeRangeValue;
            if (ImGui.InputInt($"##Value", ref value, 1, 10))
            {
                if (value < 1) value = 1;
                timeRangeValue = value;
                changed = true;
            }
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(label);

        ImGui.PopID();

        return changed;
    }

    /// <summary>
    /// Draws a time range selector with separate labeled controls (vertical layout).
    /// </summary>
    /// <param name="timeRangeValue">Reference to the time range value.</param>
    /// <param name="timeRangeUnit">Reference to the time range unit.</param>
    /// <param name="inputWidth">Width of input controls.</param>
    /// <returns>True if either value changed.</returns>
    public static bool DrawVertical(
        ref int timeRangeValue,
        ref TimeUnit timeRangeUnit,
        float inputWidth = 150f)
    {
        bool changed = false;

        // Unit dropdown first (skip Seconds, so offset by 1)
        var unitIndex = (int)timeRangeUnit - TimeRangeUnitOffset;
        if (unitIndex < 0) unitIndex = 0; // Clamp Seconds to Minutes
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.Combo("Range unit", ref unitIndex, TimeRangeUnitNames, TimeRangeUnitNames.Length))
        {
            timeRangeUnit = (TimeUnit)(unitIndex + TimeRangeUnitOffset);
            changed = true;
        }

        // Only show value input if not "All"
        if (timeRangeUnit != TimeUnit.All)
        {
            var value = timeRangeValue;
            ImGui.SetNextItemWidth(inputWidth);
            if (ImGui.InputInt("Range value", ref value, 1, 10))
            {
                if (value < 1) value = 1;
                timeRangeValue = value;
                changed = true;
            }
        }

        return changed;
    }

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

    /// <summary>
    /// Gets the start time based on the time range settings.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The time unit.</param>
    /// <returns>The start DateTime (UTC), or DateTime.MinValue if unit is All.</returns>
    public static DateTime GetStartTime(int value, TimeUnit unit)
    {
        var timeSpan = GetTimeSpan(value, unit);
        return timeSpan.HasValue ? DateTime.UtcNow - timeSpan.Value : DateTime.MinValue;
    }

    /// <summary>
    /// Gets a human-readable description of the time range.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The time unit.</param>
    /// <returns>Description like "Last 7 days" or "All time".</returns>
    public static string GetDescription(int value, TimeUnit unit)
    {
        if (unit == TimeUnit.All)
            return "All time";

        var unitName = unit switch
        {
            TimeUnit.Seconds => value == 1 ? "second" : "seconds",
            TimeUnit.Minutes => value == 1 ? "minute" : "minutes",
            TimeUnit.Hours => value == 1 ? "hour" : "hours",
            TimeUnit.Days => value == 1 ? "day" : "days",
            TimeUnit.Weeks => value == 1 ? "week" : "weeks",
            TimeUnit.Months => value == 1 ? "month" : "months",
            _ => "units"
        };

        return $"Last {value} {unitName}";
    }
}
