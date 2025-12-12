using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using OtterGui.Raii;

namespace Kaleidoscope.Gui.Widgets;

public static class ContentContainer
{
    /// <summary>
    /// Begins a child region that covers the current window's content area with a margin from the content edges.
    /// Use as: `using var c = ContentContainer.Begin(10f); /* draw inside */`.
    /// </summary>
    /// <param name="margin">Margin in pixels from each window edge.</param>
    /// <param name="id">ImGui id for the child region.</param>
    /// <returns>An RAII end-object that ends the child on Dispose.</returns>
    public static ImRaii.IEndObject Begin(float margin = 10f, string id = "##ContentContainer", bool enableGrid = false)
    {
        // Use the content region to avoid overlapping titlebars, headers and window padding.
        var winPos = ImGui.GetWindowPos();
        var contentMin = ImGui.GetWindowContentRegionMin();
        var contentMax = ImGui.GetWindowContentRegionMax();

        var contentPos = winPos + contentMin;
        var contentSize = contentMax - contentMin;

        var pos = contentPos + new Vector2(margin, margin);
        var size = new Vector2(Math.Max(0f, contentSize.X - 2 * margin), Math.Max(0f, contentSize.Y - 2 * margin));

        // Add a small inner padding so child contents are inset from the outline.
        const float padding = 5f;
        var innerPos = pos + new Vector2(padding, padding);
        var innerSize = new Vector2(Math.Max(0f, size.X - 2 * padding), Math.Max(0f, size.Y - 2 * padding));

        // Position the cursor at the inset position for the child region.
        ImGui.SetCursorScreenPos(innerPos);

        // Begin the child region with scrolling disabled so content does not scroll.
        // Use NoScrollbar and NoScrollWithMouse to prevent both visible scrollbars
        // and mouse-driven scrolling inside the container.
        var child = ImRaii.Child(id, innerSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        // Determine whether grid behavior should be enabled. Grid can be
        // explicitly requested via `enableGrid`, or turned on globally via
        // the TopBar's `EditMode` toggle.
        var enableGridActual = enableGrid;
        try { enableGridActual = enableGridActual || Kaleidoscope.Gui.TopBar.TopBar.EditMode; } catch { }

        // If grid behavior is requested, start an ImGui table sized according to
        // the configured cell percentage values. The table will be disposed
        // before the child is disposed so the outline draws on top of content.
        if (enableGridActual)
        {
            float cellW = 25f;
            float cellH = 25f;
            try
            {
                var cfg = ECommons.DalamudServices.Svc.PluginInterface.GetPluginConfig() as Kaleidoscope.Configuration;
                if (cfg != null)
                {
                    cellW = Math.Clamp(cfg.ContentGridCellWidthPercent, 1f, 100f);
                    cellH = Math.Clamp(cfg.ContentGridCellHeightPercent, 1f, 100f);
                }
            }
            catch { }

            var cols = Math.Max(1, (int)Math.Floor(100f / Math.Max(1f, cellW)));
            var tableId = id + "_table";
            // Do not enable ImGui table borders to avoid double-drawing the grid.
            // We draw the grid overlay manually in Dispose so the borders don't collide.
            var tableFlags = ImGuiTableFlags.None;
            var table = ImRaii.Table(tableId, cols, tableFlags, innerSize);
            try
            {
                // Set initial column widths based on the configured percent
                var colWidth = innerSize.X * (Math.Max(1f, cellW) / 100f);
                for (var i = 0; i < cols; ++i)
                {
                    try { ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.None, colWidth, (uint)i); } catch { }
                }
            }
            catch { }

            return new ContentContainerEnd(child, pos, size, table);
        }

        return new ContentContainerEnd(child, pos, size);
    }
}

// RAII end-object that wraps the child end-object and draws an outline
// around the previously reserved child rectangle after the child is ended.
internal sealed class ContentContainerEnd : ImRaii.IEndObject
{
    private readonly ImRaii.IEndObject _childEnd;
    private readonly ImRaii.IEndObject? _tableEnd;
    private readonly Vector2 _pos;
    private readonly Vector2 _size;
    private bool _disposed = false;

    public ContentContainerEnd(ImRaii.IEndObject childEnd, Vector2 pos, Vector2 size)
    {
        _childEnd = childEnd;
        _pos = pos;
        _size = size;
    }

    public ContentContainerEnd(ImRaii.IEndObject childEnd, Vector2 pos, Vector2 size, ImRaii.IEndObject tableEnd)
    {
        _childEnd = childEnd;
        _tableEnd = tableEnd;
        _pos = pos;
        _size = size;
    }

    public bool Success => _childEnd != null && _childEnd.Success;

    public void Dispose()
    {
        if (_disposed) return;

        // If a table was created, end it first (it is inside the child),
        // then end the child so the outline is drawn on top of its contents.
        try { _tableEnd?.Dispose(); } catch { }
        try { _childEnd.Dispose(); } catch { }

            try
            {
                var drawList = ImGui.GetWindowDrawList();
                var color = ImGui.GetColorU32(ImGuiCol.Border);
                // Draw with zero rounding to ensure square corners.
                var rounding = 0f;
                var thickness = Math.Max(1f, ImGui.GetStyle().ChildBorderSize);
                drawList.AddRect(_pos, _pos + _size, color, rounding, ImDrawFlags.RoundCornersAll, thickness);
            }
        catch { }

        // If a table (grid) was created, draw overlay grid lines based on configured
        // cell percent values so Edit Mode shows the expected number of rows/cols.
        try
        {
            if (_tableEnd != null)
            {
                float cellW = 25f;
                float cellH = 25f;
                try
                {
                    var cfg = ECommons.DalamudServices.Svc.PluginInterface.GetPluginConfig() as Kaleidoscope.Configuration;
                    if (cfg != null)
                    {
                        cellW = Math.Clamp(cfg.ContentGridCellWidthPercent, 1f, 100f);
                        cellH = Math.Clamp(cfg.ContentGridCellHeightPercent, 1f, 100f);
                    }
                }
                catch { }

                var cols = Math.Max(1, (int)Math.Floor(100f / Math.Max(1f, cellW)));
                var rows = Math.Max(1, (int)Math.Floor(100f / Math.Max(1f, cellH)));

                var style = ImGui.GetStyle();
                var baseBorder = style.Colors[(int)ImGuiCol.Border];
                var gridCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(baseBorder.X, baseBorder.Y, baseBorder.Z, baseBorder.W * 0.75f));

                var drawList2 = ImGui.GetWindowDrawList();
                var cellWpx = _size.X / (float)cols;
                var cellHpx = _size.Y / (float)rows;
                var thicknessGrid = Math.Max(1f, ImGui.GetStyle().ChildBorderSize * 0.7f);

                // Vertical lines
                for (var i = 1; i < cols; ++i)
                {
                    var x = _pos.X + cellWpx * i;
                    drawList2.AddLine(new Vector2(x, _pos.Y), new Vector2(x, _pos.Y + _size.Y), gridCol, thicknessGrid);
                }

                // Horizontal lines
                for (var j = 1; j < rows; ++j)
                {
                    var y = _pos.Y + cellHpx * j;
                    drawList2.AddLine(new Vector2(_pos.X, y), new Vector2(_pos.X + _size.X, y), gridCol, thicknessGrid);
                }

                // Highlight hovered cell
                try
                {
                    var mouse = ImGui.GetMousePos();
                    if (mouse.X >= _pos.X && mouse.Y >= _pos.Y && mouse.X <= _pos.X + _size.X && mouse.Y <= _pos.Y + _size.Y)
                    {
                        var localX = mouse.X - _pos.X;
                        var localY = mouse.Y - _pos.Y;
                        var col = Math.Clamp((int)Math.Floor(localX / cellWpx), 0, cols - 1);
                        var row = Math.Clamp((int)Math.Floor(localY / cellHpx), 0, rows - 1);

                        var cellMin = new Vector2(_pos.X + col * cellWpx, _pos.Y + row * cellHpx);
                        var cellMax = cellMin + new Vector2(cellWpx, cellHpx);

                        var hlCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.5f, 0.9f, 0.18f));
                        var hlBorder = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.5f, 0.9f, 0.9f));

                        drawList2.AddRectFilled(cellMin, cellMax, hlCol, 2f);
                        drawList2.AddRect(cellMin, cellMax, hlBorder, 2f, ImDrawFlags.RoundCornersAll, Math.Max(1f, thicknessGrid));

                        // Show a small tooltip with cell indices
                        try { ImGui.SetTooltip($"Cell: {col}, {row}"); } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }

        _disposed = true;
    }
}
