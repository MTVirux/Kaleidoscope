using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Computes grid coordinates for tools based on a <see cref="LayoutArrangement"/> preset.
/// All algorithms work in grid-coordinate space (columns/rows) and set
/// <see cref="ToolComponent.GridCol"/>/<see cref="ToolComponent.GridRow"/>/<see cref="ToolComponent.GridColSpan"/>/<see cref="ToolComponent.GridRowSpan"/>
/// so the normal grid→pixel conversion handles actual positioning.
/// </summary>
internal static class AutoLayoutEngine
{
    /// <summary>
    /// Applies the given arrangement to the provided tools, distributing them
    /// across the <paramref name="gridColumns"/> × <paramref name="gridRows"/> grid.
    /// Has no effect for <see cref="LayoutArrangement.Grid"/> (manual mode).
    /// </summary>
    public static void ApplyPreset(
        LayoutArrangement arrangement,
        IReadOnlyList<ToolComponent> tools,
        int gridColumns,
        int gridRows)
    {
        if (tools.Count == 0) return;

        switch (arrangement)
        {
            case LayoutArrangement.SingleColumn:
                LayoutSingleColumn(tools, gridColumns, gridRows);
                break;
            case LayoutArrangement.TwoColumn:
                LayoutColumns(tools, gridColumns, gridRows, 2);
                break;
            case LayoutArrangement.ThreeColumn:
                LayoutColumns(tools, gridColumns, gridRows, 3);
                break;
            case LayoutArrangement.SplitHorizontal:
                LayoutSplitHorizontal(tools, gridColumns, gridRows);
                break;
            case LayoutArrangement.SplitVertical:
                LayoutSplitVertical(tools, gridColumns, gridRows);
                break;
            case LayoutArrangement.Dashboard:
                LayoutDashboard(tools, gridColumns, gridRows);
                break;
            case LayoutArrangement.Grid:
            default:
                // Manual mode — no auto-layout
                break;
        }
    }

    /// <summary>
    /// Applies the same preset logic to <see cref="ToolLayoutState"/> objects directly,
    /// for use when creating layouts from presets in the config UI (no live tools yet).
    /// </summary>
    public static void ApplyPreset(
        LayoutArrangement arrangement,
        IReadOnlyList<ToolLayoutState> tools,
        int gridColumns,
        int gridRows)
    {
        if (tools.Count == 0) return;

        // Wrap in lightweight adapters so we can share algorithms
        var adapters = new List<GridAdapter>(tools.Count);
        for (var i = 0; i < tools.Count; i++)
            adapters.Add(new GridAdapter(tools[i]));

        ApplyPreset(arrangement, adapters, gridColumns, gridRows);

        // Write back
        for (var i = 0; i < tools.Count; i++)
        {
            var a = adapters[i];
            tools[i].GridCol = a.GridCol;
            tools[i].GridRow = a.GridRow;
            tools[i].GridColSpan = a.GridColSpan;
            tools[i].GridRowSpan = a.GridRowSpan;
            tools[i].HasGridCoords = true;
        }
    }

    // ── Algorithms ──────────────────────────────────────────────────────

    /// <summary>Each tool gets full width, equal height shares.</summary>
    private static void LayoutSingleColumn(IReadOnlyList<ToolComponent> tools, int cols, int rows)
    {
        var rowsPerTool = (float)rows / tools.Count;
        for (var i = 0; i < tools.Count; i++)
        {
            var t = tools[i];
            t.GridCol = 0;
            t.GridRow = i * rowsPerTool;
            t.GridColSpan = cols;
            t.GridRowSpan = rowsPerTool;
            t.HasGridCoords = true;
        }
    }

    /// <summary>Distributes tools across N equal-width columns, filling left-to-right, top-to-bottom.</summary>
    private static void LayoutColumns(IReadOnlyList<ToolComponent> tools, int cols, int rows, int numColumns)
    {
        numColumns = Math.Clamp(numColumns, 1, cols);
        var colWidth = (float)cols / numColumns;
        var rowsPerColumn = (int)MathF.Ceiling((float)tools.Count / numColumns);
        var rowHeight = (float)rows / MathF.Max(1f, rowsPerColumn);

        for (var i = 0; i < tools.Count; i++)
        {
            var col = i % numColumns;
            var row = i / numColumns;
            var t = tools[i];
            t.GridCol = col * colWidth;
            t.GridRow = row * rowHeight;
            t.GridColSpan = colWidth;
            t.GridRowSpan = rowHeight;
            t.HasGridCoords = true;
        }
    }

    /// <summary>First half of tools fill the top half, second half fills the bottom.</summary>
    private static void LayoutSplitHorizontal(IReadOnlyList<ToolComponent> tools, int cols, int rows)
    {
        var midIndex = (tools.Count + 1) / 2; // Ceil division — top gets the odd one
        var topRows = (float)rows / 2f;
        var bottomRows = (float)rows / 2f;

        // Top half
        if (midIndex > 0)
        {
            var topColWidth = (float)cols / midIndex;
            for (var i = 0; i < midIndex; i++)
            {
                var t = tools[i];
                t.GridCol = i * topColWidth;
                t.GridRow = 0;
                t.GridColSpan = topColWidth;
                t.GridRowSpan = topRows;
                t.HasGridCoords = true;
            }
        }

        // Bottom half
        var bottomCount = tools.Count - midIndex;
        if (bottomCount > 0)
        {
            var bottomColWidth = (float)cols / bottomCount;
            for (var i = 0; i < bottomCount; i++)
            {
                var t = tools[midIndex + i];
                t.GridCol = i * bottomColWidth;
                t.GridRow = topRows;
                t.GridColSpan = bottomColWidth;
                t.GridRowSpan = bottomRows;
                t.HasGridCoords = true;
            }
        }
    }

    /// <summary>First half of tools fill the left half, second half fills the right.</summary>
    private static void LayoutSplitVertical(IReadOnlyList<ToolComponent> tools, int cols, int rows)
    {
        var midIndex = (tools.Count + 1) / 2;
        var leftCols = (float)cols / 2f;
        var rightCols = (float)cols / 2f;

        // Left half
        if (midIndex > 0)
        {
            var leftRowHeight = (float)rows / midIndex;
            for (var i = 0; i < midIndex; i++)
            {
                var t = tools[i];
                t.GridCol = 0;
                t.GridRow = i * leftRowHeight;
                t.GridColSpan = leftCols;
                t.GridRowSpan = leftRowHeight;
                t.HasGridCoords = true;
            }
        }

        // Right half
        var rightCount = tools.Count - midIndex;
        if (rightCount > 0)
        {
            var rightRowHeight = (float)rows / rightCount;
            for (var i = 0; i < rightCount; i++)
            {
                var t = tools[midIndex + i];
                t.GridCol = leftCols;
                t.GridRow = i * rightRowHeight;
                t.GridColSpan = rightCols;
                t.GridRowSpan = rightRowHeight;
                t.HasGridCoords = true;
            }
        }
    }

    /// <summary>
    /// First tool spans full width in the top 25%; remaining tools fill a grid in the bottom 75%.
    /// Falls back to single-column for 1 tool.
    /// </summary>
    private static void LayoutDashboard(IReadOnlyList<ToolComponent> tools, int cols, int rows)
    {
        if (tools.Count == 1)
        {
            LayoutSingleColumn(tools, cols, rows);
            return;
        }

        // Header tool: full width, top quarter
        var headerRows = rows * 0.25f;
        var t0 = tools[0];
        t0.GridCol = 0;
        t0.GridRow = 0;
        t0.GridColSpan = cols;
        t0.GridRowSpan = headerRows;
        t0.HasGridCoords = true;

        // Remaining tools: fill grid in the bottom 75%
        var remaining = tools.Count - 1;
        var bodyRows = rows - headerRows;
        var bodyCols = Math.Min(remaining, 3); // Up to 3 columns
        var bodyRowCount = (int)MathF.Ceiling((float)remaining / bodyCols);
        var bodyColWidth = (float)cols / bodyCols;
        var bodyRowHeight = bodyRows / MathF.Max(1f, bodyRowCount);

        for (var i = 0; i < remaining; i++)
        {
            var col = i % bodyCols;
            var row = i / bodyCols;
            var t = tools[1 + i];
            t.GridCol = col * bodyColWidth;
            t.GridRow = headerRows + row * bodyRowHeight;
            t.GridColSpan = bodyColWidth;
            t.GridRowSpan = bodyRowHeight;
            t.HasGridCoords = true;
        }
    }

    // ── Lightweight Adapter ─────────────────────────────────────────────

    /// <summary>
    /// Adapter that makes <see cref="ToolLayoutState"/> look like a <see cref="ToolComponent"/>
    /// for the auto-layout algorithms. Used when applying presets to persisted state (no live tools).
    /// </summary>
    private sealed class GridAdapter : ToolComponent
    {
        public GridAdapter(ToolLayoutState state)
        {
            Id = state.Id;
            Title = state.Title;
            GridCol = state.GridCol;
            GridRow = state.GridRow;
            GridColSpan = state.GridColSpan;
            GridRowSpan = state.GridRowSpan;
            HasGridCoords = state.HasGridCoords;
        }

        public override void RenderToolContent() { }
    }
}
