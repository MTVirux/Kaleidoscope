using System.Globalization;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.DatePicker;

/// <summary>
/// A calendar-based date picker widget with optional time input.
/// Displays a popup calendar when clicked, allowing month/year navigation.
/// </summary>
public sealed class DatePickerWidget
{
    private static readonly string[] DayNames = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    private static readonly string[] MonthNames = 
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    private readonly string _id;
    private readonly bool _showTime;
    
    private DateTime _selectedDate;
    private DateTime _displayedMonth;
    private int _hour;
    private int _minute;

    /// <summary>
    /// Gets the currently selected date and time.
    /// </summary>
    public DateTime SelectedDateTime
    {
        get => new(_selectedDate.Year, _selectedDate.Month, _selectedDate.Day, _hour, _minute, 0);
        set
        {
            _selectedDate = value.Date;
            _displayedMonth = new DateTime(value.Year, value.Month, 1);
            _hour = value.Hour;
            _minute = value.Minute;
        }
    }

    /// <summary>
    /// Event raised when the selected date/time changes.
    /// </summary>
    public event Action<DateTime>? DateTimeChanged;

    /// <summary>
    /// Creates a new date picker widget.
    /// </summary>
    /// <param name="id">Unique ImGui ID for this widget.</param>
    /// <param name="showTime">Whether to show hour/minute inputs.</param>
    /// <param name="initialDate">Initial selected date. Defaults to now if not specified.</param>
    public DatePickerWidget(string id, bool showTime = true, DateTime? initialDate = null)
    {
        _id = id;
        _showTime = showTime;
        var initial = initialDate ?? DateTime.Now;
        _selectedDate = initial.Date;
        _displayedMonth = new DateTime(initial.Year, initial.Month, 1);
        _hour = initial.Hour;
        _minute = initial.Minute;
    }

    /// <summary>
    /// Draws the date picker button and popup.
    /// </summary>
    /// <param name="width">Width of the button. Use -1 for auto.</param>
    /// <returns>True if the date changed this frame.</returns>
    public bool Draw(float width = -1f)
    {
        var changed = false;
        var popupId = $"{_id}_popup";

        // Format the display string
        var displayText = _showTime
            ? SelectedDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : SelectedDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Draw the button that opens the popup
        if (width > 0)
        {
            ImGui.SetNextItemWidth(width);
        }

        if (ImGui.Button($"{displayText}##{_id}"))
        {
            ImGui.OpenPopup(popupId);
            _displayedMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        }

        // Draw the calendar popup
        if (ImGui.BeginPopup(popupId))
        {
            changed = DrawCalendarPopup();
            ImGui.EndPopup();
        }

        return changed;
    }

    private bool DrawCalendarPopup()
    {
        var changed = DrawCalendarContent();

        ImGui.Separator();

        // Close button
        var buttonWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.Button("Close", new Vector2(buttonWidth, 0)))
        {
            ImGui.CloseCurrentPopup();
        }

        return changed;
    }

    private bool DrawCalendarContent()
    {
        var changed = false;

        // Month/Year navigation header
        DrawNavigationHeader();

        ImGui.Spacing();

        // Day names header
        DrawDayNamesHeader();

        // Calendar grid
        changed |= DrawCalendarGrid();

        // Time input if enabled
        if (_showTime)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            changed |= DrawTimeInput();
        }

        return changed;
    }

    private void DrawNavigationHeader()
    {
        // Previous year
        if (ImGui.ArrowButton($"{_id}_prevYear", ImGuiDir.Left))
        {
            _displayedMonth = _displayedMonth.AddYears(-1);
        }
        ImGui.SameLine();

        // Previous month
        if (ImGui.ArrowButton($"{_id}_prevMonth", ImGuiDir.Left))
        {
            _displayedMonth = _displayedMonth.AddMonths(-1);
        }
        ImGui.SameLine();

        // Month and Year display (centered)
        var headerText = $"{MonthNames[_displayedMonth.Month - 1]} {_displayedMonth.Year}";
        var textWidth = ImGui.CalcTextSize(headerText).X;
        var availWidth = ImGui.GetContentRegionAvail().X;
        var arrowButtonWidth = ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.X;
        var centerPadding = (availWidth - arrowButtonWidth - textWidth) / 2;
        
        if (centerPadding > 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerPadding);
        }
        
        ImGui.TextUnformatted(headerText);
        ImGui.SameLine();

        // Position arrows at the right
        var arrowsStartX = ImGui.GetContentRegionAvail().X - arrowButtonWidth + ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(arrowsStartX);

        // Next month
        if (ImGui.ArrowButton($"{_id}_nextMonth", ImGuiDir.Right))
        {
            _displayedMonth = _displayedMonth.AddMonths(1);
        }
        ImGui.SameLine();

        // Next year
        if (ImGui.ArrowButton($"{_id}_nextYear", ImGuiDir.Right))
        {
            _displayedMonth = _displayedMonth.AddYears(1);
        }
    }

    private void DrawDayNamesHeader()
    {
        var cellWidth = GetCellWidth();
        
        for (var i = 0; i < 7; i++)
        {
            if (i > 0) ImGui.SameLine();
            
            var textSize = ImGui.CalcTextSize(DayNames[i]);
            var padding = (cellWidth - textSize.X) / 2;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);
            ImGui.TextDisabled(DayNames[i]);
            
            if (i < 6)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);
            }
        }
    }

    private bool DrawCalendarGrid()
    {
        var changed = false;
        var cellWidth = GetCellWidth();
        var cellSize = new Vector2(cellWidth, cellWidth);

        // Get first day of month and total days
        var firstOfMonth = new DateTime(_displayedMonth.Year, _displayedMonth.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_displayedMonth.Year, _displayedMonth.Month);
        var startDayOfWeek = (int)firstOfMonth.DayOfWeek; // Sunday = 0

        var today = DateTime.Today;
        var day = 1;

        // Draw up to 6 rows (max needed for any month)
        for (var row = 0; row < 6; row++)
        {
            if (day > daysInMonth) break;

            for (var col = 0; col < 7; col++)
            {
                if (col > 0) ImGui.SameLine();

                var cellIndex = row * 7 + col;

                if (cellIndex < startDayOfWeek || day > daysInMonth)
                {
                    // Empty cell
                    ImGui.InvisibleButton($"{_id}_empty_{row}_{col}", cellSize);
                }
                else
                {
                    var currentDate = new DateTime(_displayedMonth.Year, _displayedMonth.Month, day);
                    var isSelected = currentDate == _selectedDate;
                    var isToday = currentDate == today;

                    // Style the button
                    if (isSelected)
                    {
                        // Highlight selected date with a distinct color
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.59f, 0.98f, 0.80f)); // Blue
                    }
                    else if (isToday)
                    {
                        // Semi-transparent highlight for today
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.59f, 0.98f, 0.40f));
                    }

                    if (ImGui.Button($"{day}##{_id}_{day}", cellSize))
                    {
                        _selectedDate = currentDate;
                        changed = true;
                        DateTimeChanged?.Invoke(SelectedDateTime);
                    }

                    if (isSelected || isToday)
                    {
                        ImGui.PopStyleColor();
                    }

                    // Tooltip for today
                    if (isToday && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Today");
                    }

                    day++;
                }
            }
        }

        return changed;
    }

    private bool DrawTimeInput()
    {
        var changed = false;

        ImGui.TextUnformatted("Time:");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(50);
        var hour = _hour;
        if (ImGui.InputInt($"##{_id}_hour", ref hour, 0, 0))
        {
            _hour = Math.Clamp(hour, 0, 23);
            changed = true;
            DateTimeChanged?.Invoke(SelectedDateTime);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hour (0-23)");
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(":");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(50);
        var minute = _minute;
        if (ImGui.InputInt($"##{_id}_minute", ref minute, 0, 0))
        {
            _minute = Math.Clamp(minute, 0, 59);
            changed = true;
            DateTimeChanged?.Invoke(SelectedDateTime);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Minute (0-59)");
        }

        // Quick time buttons
        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();
        changed |= DrawTimePresetButton("00:00", "midnight", 0, 0);
        ImGui.SameLine();
        changed |= DrawTimePresetButton("12:00", "noon", 12, 0);
        ImGui.SameLine();
        changed |= DrawTimePresetButton("23:59", "endofday", 23, 59);

        return changed;
    }

    private bool DrawTimePresetButton(string label, string id, int hour, int minute)
    {
        if (!ImGui.SmallButton($"{label}##{_id}_{id}")) return false;
        _hour = hour;
        _minute = minute;
        DateTimeChanged?.Invoke(SelectedDateTime);
        return true;
    }

    private float GetCellWidth()
    {
        // Calculate cell width based on available space (7 columns)
        var availWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X * 6;
        return Math.Max(24f, (availWidth - spacing) / 7f);
    }

    /// <summary>
    /// Selects a date and raises the changed event.
    /// </summary>
    /// <param name="date">The date to select.</param>
    public void Select(DateTime date)
    {
        _selectedDate = date.Date;
        _hour = date.Hour;
        _minute = date.Minute;
        _displayedMonth = new DateTime(date.Year, date.Month, 1);
        DateTimeChanged?.Invoke(SelectedDateTime);
    }
}
