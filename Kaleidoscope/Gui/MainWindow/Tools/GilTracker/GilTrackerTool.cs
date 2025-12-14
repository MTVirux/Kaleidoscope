using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.GilTracker;

/// <summary>
/// Tool component wrapper for the Gil Tracker feature.
/// </summary>
public class GilTrackerTool : ToolComponent
{
    private readonly GilTrackerComponent _inner;

    public GilTrackerTool(GilTrackerComponent inner)
    {
        _inner = inner;
        Title = "Gil Tracker";
        Size = ConfigStatic.GilTrackerToolSize;
    }

    public override void DrawContent()
    {
        _inner.Draw();
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            var min = _inner.GraphMinValue;
            var max = _inner.GraphMaxValue;

            ImGui.TextUnformatted("Graph bounds (Y-axis)");
            ImGui.Separator();
            if (ImGui.InputFloat("Min value", ref min, 0f, 0f, "%.0f"))
            {
                // Ensure min isn't greater than or equal to max - clamp
                if (min >= max) min = MathF.Max(0f, max - 1f);
                _inner.GraphMinValue = min;
            }
            // Show tooltip for Min value
            ShowSettingTooltip("Minimum Y value displayed on the graph. Values below this will be clamped.", "0");

            if (ImGui.InputFloat("Max value", ref max, 0f, 0f, "%.0f"))
            {
                if (max <= min) max = MathF.Max(min + 1f, min + 1f);
                _inner.GraphMaxValue = max;
            }

            // Show tooltip for Max value
            ShowSettingTooltip($"Maximum Y value displayed on the graph. Values above this will be clamped.", ConfigStatic.GilTrackerMaxGil.ToString("N0"));
        }
        catch (Exception ex)
        {
            LogService.Error("Error drawing GilTracker settings", ex);
        }
    }
}
