using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable time range selector widget combining value input with unit selection.
/// Used for filtering time-series data in graphs and data displays.
/// </summary>
public static class TimeRangeSelectorWidget
{
    /// <summary>Display names for time range units.</summary>
    private static readonly string[] TimeRangeUnitNames = { "Minutes", "Hours", "Days", "Weeks", "Months", "All (no limit)" };

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
        ref TimeRangeUnit timeRangeUnit,
        float valueWidth = 100f,
        float unitWidth = 150f)
    {
        bool changed = false;

        ImGui.PushID(label);

        // Unit dropdown first
        ImGui.SetNextItemWidth(unitWidth);
        var unitIndex = (int)timeRangeUnit;
        if (ImGui.Combo($"##Unit", ref unitIndex, TimeRangeUnitNames, TimeRangeUnitNames.Length))
        {
            timeRangeUnit = (TimeRangeUnit)unitIndex;
            changed = true;
        }

        // Only show value input if not "All"
        if (timeRangeUnit != TimeRangeUnit.All)
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
        ref TimeRangeUnit timeRangeUnit,
        float inputWidth = 150f)
    {
        bool changed = false;

        // Unit dropdown first
        var unitIndex = (int)timeRangeUnit;
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.Combo("Range unit", ref unitIndex, TimeRangeUnitNames, TimeRangeUnitNames.Length))
        {
            timeRangeUnit = (TimeRangeUnit)unitIndex;
            changed = true;
        }

        // Only show value input if not "All"
        if (timeRangeUnit != TimeRangeUnit.All)
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
    public static TimeSpan? GetTimeSpan(int value, TimeRangeUnit unit)
    {
        return unit switch
        {
            TimeRangeUnit.Minutes => TimeSpan.FromMinutes(value),
            TimeRangeUnit.Hours => TimeSpan.FromHours(value),
            TimeRangeUnit.Days => TimeSpan.FromDays(value),
            TimeRangeUnit.Weeks => TimeSpan.FromDays(value * 7),
            TimeRangeUnit.Months => TimeSpan.FromDays(value * 30),
            TimeRangeUnit.All => null,
            _ => null
        };
    }

    /// <summary>
    /// Gets the start time based on the time range settings.
    /// </summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="unit">The time unit.</param>
    /// <returns>The start DateTime (UTC), or DateTime.MinValue if unit is All.</returns>
    public static DateTime GetStartTime(int value, TimeRangeUnit unit)
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
    public static string GetDescription(int value, TimeRangeUnit unit)
    {
        if (unit == TimeRangeUnit.All)
            return "All time";

        var unitName = unit switch
        {
            TimeRangeUnit.Minutes => value == 1 ? "minute" : "minutes",
            TimeRangeUnit.Hours => value == 1 ? "hour" : "hours",
            TimeRangeUnit.Days => value == 1 ? "day" : "days",
            TimeRangeUnit.Weeks => value == 1 ? "week" : "weeks",
            TimeRangeUnit.Months => value == 1 ? "month" : "months",
            _ => "units"
        };

        return $"Last {value} {unitName}";
    }
}
