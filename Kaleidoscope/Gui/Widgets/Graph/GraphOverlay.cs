using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Shared hit-testing helpers for interactive graph overlays (inside legend, controls drawer).
/// </summary>
public static class GraphOverlay
{
    /// <summary>
    /// Returns true if the current window can process mouse interactions,
    /// i.e. it is hovered and not blocked by another window on top.
    /// </summary>
    public static bool CanProcessInteraction()
        => ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

    /// <summary>
    /// Axis-aligned bounding-box containment test (inclusive on both corners).
    /// </summary>
    public static bool RectContains(Vector2 min, Vector2 max, Vector2 point)
        => point.X >= min.X && point.X <= max.X &&
           point.Y >= min.Y && point.Y <= max.Y;
}

/// <summary>
/// A rectangular overlay region used for input-blocking hit tests.
/// Shared by the inside legend and the controls drawer.
/// </summary>
public readonly struct OverlayRegion
{
    /// <summary>Minimum (top-left) corner of the region.</summary>
    public readonly Vector2 BoundsMin;

    /// <summary>Maximum (bottom-right) corner of the region.</summary>
    public readonly Vector2 BoundsMax;

    /// <summary>Whether this region represents a drawn overlay.</summary>
    public readonly bool IsValid;

    public OverlayRegion(Vector2 boundsMin, Vector2 boundsMax)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        IsValid = true;
    }

    /// <summary>Checks whether a point lies within this region's bounds.</summary>
    public bool Contains(Vector2 point) => GraphOverlay.RectContains(BoundsMin, BoundsMax, point);

    /// <summary>
    /// True if the region is valid, the window is hovered, and the mouse lies within the bounds.
    /// </summary>
    public bool IsMouseOver()
        => IsValid && GraphOverlay.CanProcessInteraction() && Contains(ImGui.GetMousePos());
}
