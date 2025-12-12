using System.Numerics;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.Widgets;

public sealed class ContentComponent
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
    public readonly int Id;

    private static int _nextId = 1;

    public ContentComponent(int col, int row, int colSpan = 2, int rowSpan = 2)
    {
        Col = col;
        Row = row;
        ColSpan = Math.Max(1, colSpan);
        RowSpan = Math.Max(1, rowSpan);
        Id = _nextId++;
    }
}

internal static class ContentComponentManager
{
    private static readonly List<ContentComponent> Components = new();

    // Active grid information
    private static Vector2 _gridPos;
    private static Vector2 _gridSize;
    private static int _gridCols;
    private static int _gridRows;
    private static float _cellWPercent;
    private static float _cellHPercent;

    // Interaction state
    private static bool _dragging = false;
    private static int _dragIndex = -1;
    private static bool _resizing = false;
    private static int _resizeIndex = -1;
    private static string _resizeEdge = string.Empty; // "left","right","top","bottom"
    private static int _origCol, _origRow, _origColSpan, _origRowSpan;

    // Preview
    private static bool _hasPreview = false;
    private static int _previewCol, _previewRow, _previewColSpan, _previewRowSpan;

    public static void SetActiveGrid(Vector2 pos, Vector2 size, int cols, int rows, float cellWPercent, float cellHPercent)
    {
        _gridPos = pos;
        _gridSize = size;
        _gridCols = Math.Max(1, cols);
        _gridRows = Math.Max(1, rows);
        _cellWPercent = cellWPercent;
        _cellHPercent = cellHPercent;
    }

    public static void ClearActiveGrid()
    {
        _gridCols = 0;
        _gridRows = 0;
        _hasPreview = false;
    }

    public static void UpdateAndDraw(Vector2 pos, Vector2 size, int cols, int rows, float cellWpx, float cellHpx)
    {
        // Draw existing components
        var drawList = ImGui.GetWindowDrawList();

        // Background for each component and interaction handling
        var mouse = ImGui.GetMousePos();

        for (var i = 0; i < Components.Count; ++i)
        {
            var c = Components[i];
            var min = new Vector2(pos.X + c.Col * cellWpx, pos.Y + c.Row * cellHpx);
            var max = min + new Vector2(c.ColSpan * cellWpx, c.RowSpan * cellHpx);

            // Background color #DDDDDD
            var bgU = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.866f, 0.866f, 0.866f, 1f));
            drawList.AddRectFilled(min, max, bgU, 3f);

            // Border
            var borderU = ImGui.GetColorU32(ImGuiCol.Border);
            drawList.AddRect(min, max, borderU, 3f, ImDrawFlags.RoundCornersAll, Math.Max(1f, ImGui.GetStyle().ChildBorderSize));

            // Draw a small 'X' button in top-right corner
            var padding = 6f;
            var xSize = 18f;
            var xMin = new Vector2(max.X - padding - xSize, min.Y + padding);
            var xMax = new Vector2(max.X - padding, min.Y + padding + xSize);
            var hoverX = mouse.X >= xMin.X && mouse.Y >= xMin.Y && mouse.X <= xMax.X && mouse.Y <= xMax.Y;

            var xBg = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.9f, 0.3f, 0.3f, hoverX ? 0.95f : 0.7f));
            drawList.AddRectFilled(xMin, xMax, xBg, 4f);
            var xCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(1f, 1f, 1f, 1f));
            var textPos = new Vector2(xMin.X + 4f, xMin.Y + 1f);
            drawList.AddText(textPos, xCol, "X");

            // Handles (left/right center, top/bottom center)
            var handleSize = 8f;
            var leftMin = new Vector2(min.X - handleSize / 2f, min.Y + (max.Y - min.Y) / 2f - handleSize / 2f);
            var leftMax = leftMin + new Vector2(handleSize, handleSize);
            var rightMin = new Vector2(max.X - handleSize / 2f, min.Y + (max.Y - min.Y) / 2f - handleSize / 2f);
            var rightMax = rightMin + new Vector2(handleSize, handleSize);
            var topMin = new Vector2(min.X + (max.X - min.X) / 2f - handleSize / 2f, min.Y - handleSize / 2f);
            var topMax = topMin + new Vector2(handleSize, handleSize);
            var bottomMin = new Vector2(min.X + (max.X - min.X) / 2f - handleSize / 2f, max.Y - handleSize / 2f);
            var bottomMax = bottomMin + new Vector2(handleSize, handleSize);

            var handleCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 0.95f));
            drawList.AddRectFilled(leftMin, leftMax, handleCol, 3f);
            drawList.AddRectFilled(rightMin, rightMax, handleCol, 3f);
            drawList.AddRectFilled(topMin, topMax, handleCol, 3f);
            drawList.AddRectFilled(bottomMin, bottomMax, handleCol, 3f);

            // Interaction: delete
            if (hoverX && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                Components.RemoveAt(i);
                if (_dragIndex == i) _dragging = false;
                return; // list mutated; exit early this frame
            }

            // Interaction: start resize
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (PointInRect(mouse, leftMin, leftMax)) { StartResize(i, "left"); return; }
                if (PointInRect(mouse, rightMin, rightMax)) { StartResize(i, "right"); return; }
                if (PointInRect(mouse, topMin, topMax)) { StartResize(i, "top"); return; }
                if (PointInRect(mouse, bottomMin, bottomMax)) { StartResize(i, "bottom"); return; }
            }

            // Interaction: start dragging when clicking inside (but not on X or handles)
            var inside = PointInRect(mouse, min, max);
            if (inside && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // ignore if clicked on X or handles
                if (!hoverX && !PointInRect(mouse, leftMin, leftMax) && !PointInRect(mouse, rightMin, rightMax) && !PointInRect(mouse, topMin, topMax) && !PointInRect(mouse, bottomMin, bottomMax))
                {
                    StartDrag(i);
                    return;
                }
            }
        }

        // Handle active dragging
        if (_dragging && _dragIndex >= 0 && _dragIndex < Components.Count)
        {
            var cur = Components[_dragIndex];
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var m = ImGui.GetMousePos();
                var rel = new Vector2(m.X - pos.X, m.Y - pos.Y);
                var col = Math.Clamp((int)Math.Floor(rel.X / cellWpx), 0, _gridCols - 1);
                var row = Math.Clamp((int)Math.Floor(rel.Y / cellHpx), 0, _gridRows - 1);

                // Clamp so the component doesn't overflow the grid
                col = Math.Min(col, _gridCols - cur.ColSpan);
                row = Math.Min(row, _gridRows - cur.RowSpan);

                _hasPreview = true;
                _previewCol = col;
                _previewRow = row;
                _previewColSpan = cur.ColSpan;
                _previewRowSpan = cur.RowSpan;

                // draw preview handled below
            }
            else
            {
                // mouse released -> commit. If no preview was prepared (fast click-drag-release),
                // compute final cell from mouse position and apply.
                var curc = Components[_dragIndex];
                var m2 = ImGui.GetMousePos();
                var rel2 = new Vector2(m2.X - pos.X, m2.Y - pos.Y);
                var finalCol = Math.Clamp((int)Math.Floor(rel2.X / cellWpx), 0, _gridCols - 1);
                var finalRow = Math.Clamp((int)Math.Floor(rel2.Y / cellHpx), 0, _gridRows - 1);
                finalCol = Math.Min(finalCol, _gridCols - curc.ColSpan);
                finalRow = Math.Min(finalRow, _gridRows - curc.RowSpan);

                if (_hasPreview)
                {
                    curc.Col = _previewCol;
                    curc.Row = _previewRow;
                }
                else
                {
                    curc.Col = finalCol;
                    curc.Row = finalRow;
                }

                _dragging = false;
                _dragIndex = -1;
                _hasPreview = false;
            }
        }

        // Handle active resize
        if (_resizing && _resizeIndex >= 0 && _resizeIndex < Components.Count)
        {
            var cur = Components[_resizeIndex];
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var m = ImGui.GetMousePos();
                var rel = new Vector2(m.X - pos.X, m.Y - pos.Y);
                var col = Math.Clamp((int)Math.Floor(rel.X / cellWpx), 0, _gridCols - 1);
                var row = Math.Clamp((int)Math.Floor(rel.Y / cellHpx), 0, _gridRows - 1);

                var newCol = cur.Col;
                var newRow = cur.Row;
                var newColSpan = cur.ColSpan;
                var newRowSpan = cur.RowSpan;

                if (_resizeEdge == "left")
                {
                    newCol = Math.Min(col, cur.Col + cur.ColSpan - 1);
                    newColSpan = cur.Col + cur.ColSpan - newCol;
                }
                else if (_resizeEdge == "right")
                {
                    var right = Math.Max(col + 1, cur.Col + 1);
                    newColSpan = Math.Clamp(right - cur.Col, 1, _gridCols - cur.Col);
                }
                else if (_resizeEdge == "top")
                {
                    newRow = Math.Min(row, cur.Row + cur.RowSpan - 1);
                    newRowSpan = cur.Row + cur.RowSpan - newRow;
                }
                else if (_resizeEdge == "bottom")
                {
                    var bottom = Math.Max(row + 1, cur.Row + 1);
                    newRowSpan = Math.Clamp(bottom - cur.Row, 1, _gridRows - cur.Row);
                }

                // Apply preview
                _hasPreview = true;
                _previewCol = newCol;
                _previewRow = newRow;
                _previewColSpan = newColSpan;
                _previewRowSpan = newRowSpan;
            }
            else
            {
                // commit. If no preview is present (fast click-release), compute final
                // values from current mouse position and apply.
                var curc = Components[_resizeIndex];
                var m2 = ImGui.GetMousePos();
                var rel2 = new Vector2(m2.X - pos.X, m2.Y - pos.Y);
                var col2 = Math.Clamp((int)Math.Floor(rel2.X / cellWpx), 0, _gridCols - 1);
                var row2 = Math.Clamp((int)Math.Floor(rel2.Y / cellHpx), 0, _gridRows - 1);

                var newCol = curc.Col;
                var newRow = curc.Row;
                var newColSpan = curc.ColSpan;
                var newRowSpan = curc.RowSpan;

                if (_resizeEdge == "left")
                {
                    newCol = Math.Min(col2, curc.Col + curc.ColSpan - 1);
                    newColSpan = curc.Col + curc.ColSpan - newCol;
                }
                else if (_resizeEdge == "right")
                {
                    var right = Math.Max(col2 + 1, curc.Col + 1);
                    newColSpan = Math.Clamp(right - curc.Col, 1, _gridCols - curc.Col);
                }
                else if (_resizeEdge == "top")
                {
                    newRow = Math.Min(row2, curc.Row + curc.RowSpan - 1);
                    newRowSpan = curc.Row + curc.RowSpan - newRow;
                }
                else if (_resizeEdge == "bottom")
                {
                    var bottom = Math.Max(row2 + 1, curc.Row + 1);
                    newRowSpan = Math.Clamp(bottom - curc.Row, 1, _gridRows - curc.Row);
                }

                if (_hasPreview)
                {
                    curc.Col = _previewCol;
                    curc.Row = _previewRow;
                    curc.ColSpan = _previewColSpan;
                    curc.RowSpan = _previewRowSpan;
                }
                else
                {
                    curc.Col = newCol;
                    curc.Row = newRow;
                    curc.ColSpan = newColSpan;
                    curc.RowSpan = newRowSpan;
                }

                _resizing = false;
                _resizeIndex = -1;
                _resizeEdge = string.Empty;
                _hasPreview = false;
            }
        }

        // Right-click to add a new 2x2 component if EditMode is active
        try
        {
            var edit = false;
            try { edit = Kaleidoscope.Gui.TopBar.TopBar.EditMode; } catch { }
            if (edit && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                var m = ImGui.GetMousePos();
                if (m.X >= pos.X && m.Y >= pos.Y && m.X <= pos.X + size.X && m.Y <= pos.Y + size.Y)
                {
                    var rel = new Vector2(m.X - pos.X, m.Y - pos.Y);
                    var col = Math.Clamp((int)Math.Floor(rel.X / cellWpx), 0, _gridCols - 1);
                    var row = Math.Clamp((int)Math.Floor(rel.Y / cellHpx), 0, _gridRows - 1);

                    // Try to find space for 2x2 starting from (col,row) scanning right/down
                    var placed = false;
                    var wantW = 2;
                    var wantH = 2;
                    for (var r = row; r < _gridRows && !placed; ++r)
                    {
                        for (var c = col; c < _gridCols && !placed; ++c)
                        {
                            if (c + wantW <= _gridCols && r + wantH <= _gridRows && !AreaOverlaps(c, r, wantW, wantH))
                            {
                                Components.Add(new ContentComponent(c, r, wantW, wantH));
                                placed = true;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // Draw preview if present
        if (_hasPreview)
        {
            var pMin = new Vector2(pos.X + _previewCol * cellWpx, pos.Y + _previewRow * cellHpx);
            var pMax = pMin + new Vector2(_previewColSpan * cellWpx, _previewRowSpan * cellHpx);
            var hlCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.5f, 0.9f, 0.18f));
            var hlBorder = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.5f, 0.9f, 0.9f));
            drawList.AddRectFilled(pMin, pMax, hlCol, 3f);
            drawList.AddRect(pMin, pMax, hlBorder, 3f, ImDrawFlags.RoundCornersAll, Math.Max(1f, ImGui.GetStyle().ChildBorderSize));
        }
    }

    private static bool AreaOverlaps(int col, int row, int w, int h)
    {
        foreach (var c in Components)
        {
            if (col + w <= c.Col) continue;
            if (c.Col + c.ColSpan <= col) continue;
            if (row + h <= c.Row) continue;
            if (c.Row + c.RowSpan <= row) continue;
            return true;
        }
        return false;
    }

    private static bool PointInRect(Vector2 p, Vector2 a, Vector2 b)
    {
        return p.X >= a.X && p.Y >= a.Y && p.X <= b.X && p.Y <= b.Y;
    }

    private static void StartDrag(int index)
    {
        _dragging = true;
        _dragIndex = index;
        _hasPreview = false;
    }

    private static void StartResize(int index, string edge)
    {
        _resizing = true;
        _resizeIndex = index;
        _resizeEdge = edge;
        _hasPreview = false;
        var cur = Components[index];
        _origCol = cur.Col; _origRow = cur.Row; _origColSpan = cur.ColSpan; _origRowSpan = cur.RowSpan;
    }
}
