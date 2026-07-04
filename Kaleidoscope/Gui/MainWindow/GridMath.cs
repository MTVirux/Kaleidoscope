using System.Numerics;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Shared grid↔pixel conversion and snap-to-grid helpers.
/// Centralizes the arithmetic previously duplicated across the drawing loop,
/// tool interactions, snap-ghost rendering, and the add-tool context menu.
/// </summary>
internal static class GridMath
{
    /// <summary>Pixel size of one snap subdivision along an axis (cell / subdivisions, subdivisions clamped to >= 1).</summary>
    public static float SubdivisionSize(float cell, int subdivisions)
        => cell / Math.Max(1, subdivisions);

    /// <summary>Converts a grid coordinate or span to pixels.</summary>
    public static float GridToPixel(float grid, float cell) => grid * cell;

    /// <summary>Converts a pixel measure back to grid units. Callers guard against zero-size cells.</summary>
    public static float PixelToGrid(float pixel, float cell) => pixel / cell;

    /// <summary>Converts a grid (col, row) coordinate to a pixel position.</summary>
    public static Vector2 GridToPixelPos(float gridCol, float gridRow, float cellW, float cellH)
        => new(GridToPixel(gridCol, cellW), GridToPixel(gridRow, cellH));

    /// <summary>Converts a grid (colSpan, rowSpan) to a pixel size, clamped to the given minimums.</summary>
    public static Vector2 GridToPixelSize(float colSpan, float rowSpan, float cellW, float cellH, float minW, float minH)
        => new(MathF.Max(minW, GridToPixel(colSpan, cellW)), MathF.Max(minH, GridToPixel(rowSpan, cellH)));

    /// <summary>
    /// Snaps a value to the nearest multiple of <paramref name="step"/>.
    /// When <paramref name="guardZeroStep"/> is true, a non-positive step leaves the value unchanged
    /// (matches the snap-ghost preview); otherwise the raw rounding is always applied.
    /// </summary>
    public static float Snap(float value, float step, bool guardZeroStep = false)
    {
        if (guardZeroStep && !(step > 0f))
            return value;
        return MathF.Round(value / step) * step;
    }
}
