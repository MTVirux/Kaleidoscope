using System.Numerics;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Manages drag and resize interactions for tools in edit mode.
/// Extracted from WindowContentContainer.Drawing.cs for single-responsibility.
/// </summary>
internal sealed class ToolInteractionManager
{
    // Track global interaction state
    private bool _anyDragging = false;
    private bool _anyResizing = false;

    /// <summary>Whether any tool is currently being dragged.</summary>
    public bool IsDragging => _anyDragging;

    /// <summary>Whether any tool is currently being resized.</summary>
    public bool IsResizing => _anyResizing;

    /// <summary>Whether any interaction (drag or resize) is in progress.</summary>
    public bool IsInteracting => _anyDragging || _anyResizing;

    /// <summary>Minimum tool width (constant).</summary>
    private static float MinToolWidth => WindowContentContainer.MinToolWidth;

    /// <summary>Minimum tool height based on current text line height.</summary>
    private static float MinToolHeight => WindowContentContainer.MinToolHeight;

    /// <summary>
    /// Handles drag interaction for a single tool. Call inside the edit-mode block after rendering.
    /// </summary>
    public void HandleDrag(
        DrawContext ctx, ToolEntry te, int toolIndex,
        LayoutGridSettings gridSettings,
        bool mainWindowInteracting, bool anotherToolInteracting,
        bool isChildFocused, Action markDirty)
    {
        var t = te.Tool;
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var min = t.Position + ctx.ContentOrigin;
        var max = min + t.Size;
        var titleHeight = MathF.Min(24f, t.Size.Y);
        var titleMin = min;
        var titleMax = new Vector2(max.X, min.Y + titleHeight);

        // Resize handle region (bottom-right corner)
        var handleSize = 12f;
        var handleMin = new Vector2(max.X - handleSize, max.Y - handleSize);
        var isMouseOverHandle = mouse.X >= handleMin.X && mouse.X <= max.X && mouse.Y >= handleMin.Y && mouse.Y <= max.Y;

        // Title area detection for drag start
        var isMouseOverTitle = mouse.X >= titleMin.X && mouse.X <= titleMax.X && mouse.Y >= titleMin.Y && mouse.Y <= titleMax.Y;
        var canInteract = isChildFocused || te.Dragging || te.Resizing;
        var canStartDrag = isMouseOverTitle && !isMouseOverHandle && canInteract;

        var shouldStartDrag = canStartDrag && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !mainWindowInteracting && !anotherToolInteracting && !te.Resizing;
        var shouldContinueDrag = te.Dragging && io.MouseDown[0];

        if (shouldStartDrag || shouldContinueDrag)
        {
            if (!te.Dragging)
            {
                te.Dragging = true;
                te.OrigPos = t.Position;
                te.DragMouseStart = io.MousePos;
            }
            var rawDelta = io.MousePos - te.DragMouseStart;
            const float MaxDelta = ConfigStatic.MaxDragDelta;
            rawDelta.X = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.X));
            rawDelta.Y = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.Y));
            var newPos = te.OrigPos + rawDelta;

            // Clamp position to content bounds
            var minX = ctx.ContentMin.X - ctx.ContentOrigin.X;
            var minY = ctx.ContentMin.Y - ctx.ContentOrigin.Y;
            var maxX = (ctx.ContentMax.X - ctx.ContentOrigin.X) - t.Size.X;
            var maxY = (ctx.ContentMax.Y - ctx.ContentOrigin.Y) - t.Size.Y;
            newPos.X = MathF.Max(minX, MathF.Min(maxX, newPos.X));
            newPos.Y = MathF.Max(minY, MathF.Min(maxY, newPos.Y));

            // During drag: follow mouse freely (no snapping). Snap on release.
            t.Position = newPos;
        }
        else if (!io.MouseDown[0])
        {
            if (te.Dragging)
            {
                SnapPosition(t, ctx, gridSettings);
                markDirty();
            }
            te.Dragging = false;
        }
    }

    /// <summary>
    /// Handles resize interaction for a single tool. Call inside the edit-mode block after drag handling.
    /// </summary>
    public void HandleResize(
        DrawContext ctx, ToolEntry te, int toolIndex,
        LayoutGridSettings gridSettings,
        bool mainWindowInteracting, bool anotherToolInteracting,
        bool isChildFocused, Action markDirty)
    {
        var t = te.Tool;
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var min = t.Position + ctx.ContentOrigin;
        var max = min + t.Size;

        // Resize handle (bottom-right corner)
        var handleSize = 12f;
        var handleMin = new Vector2(max.X - handleSize, max.Y - handleSize);
        var isMouseOverHandle = mouse.X >= handleMin.X && mouse.X <= max.X && mouse.Y >= handleMin.Y && mouse.Y <= max.Y;
        var canInteract = isChildFocused || te.Dragging || te.Resizing;
        var canStartResize = isMouseOverHandle && canInteract;

        var shouldStartResize = canStartResize && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !mainWindowInteracting && !anotherToolInteracting;
        var shouldContinueResize = te.Resizing && io.MouseDown[0];

        if (shouldStartResize || shouldContinueResize)
        {
            if (!te.Resizing)
            {
                te.Resizing = true;
                te.OrigSize = t.Size;
                te.ResizeMouseStart = io.MousePos;
            }
            var rawDelta = io.MousePos - te.ResizeMouseStart;
            const float MaxDelta = ConfigStatic.MaxDragDelta;
            rawDelta.X = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.X));
            rawDelta.Y = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.Y));
            var newSize = new Vector2(
                MathF.Max(MinToolWidth, te.OrigSize.X + rawDelta.X),
                MathF.Max(MinToolHeight, te.OrigSize.Y + rawDelta.Y));
            // Clamp size so it doesn't exceed content
            var maxW = (ctx.ContentMax.X - ctx.ContentOrigin.X) - t.Position.X;
            var maxH = (ctx.ContentMax.Y - ctx.ContentOrigin.Y) - t.Position.Y;
            newSize.X = MathF.Min(newSize.X, MathF.Max(MinToolWidth, maxW));
            newSize.Y = MathF.Min(newSize.Y, MathF.Max(MinToolHeight, maxH));
            t.Size = newSize;
        }
        else if (!io.MouseDown[0])
        {
            if (te.Resizing)
            {
                SnapSize(t, ctx, gridSettings);
                markDirty();
            }
            te.Resizing = false;
        }
    }

    /// <summary>
    /// Updates global interaction state by scanning all tools.
    /// Notifies the host of changes via ILayoutHost.
    /// </summary>
    public void UpdateGlobalState(IReadOnlyList<ToolEntry> tools, ILayoutHost host)
    {
        var anyDragging = false;
        var anyResizing = false;
        foreach (var te in tools)
        {
            if (te.Dragging) anyDragging = true;
            if (te.Resizing) anyResizing = true;
        }

        if (_anyDragging != anyDragging)
        {
            _anyDragging = anyDragging;
            try { host.NotifyDraggingChanged(anyDragging); }
            catch (Exception ex) { LogService.Debug(LogCategory.UI, $"NotifyDraggingChanged error: {ex.Message}"); }
        }
        if (_anyResizing != anyResizing)
        {
            _anyResizing = anyResizing;
            try { host.NotifyResizingChanged(anyResizing); }
            catch (Exception ex) { LogService.Debug(LogCategory.UI, $"NotifyResizingChanged error: {ex.Message}"); }
        }
    }

    /// <summary>Checks whether any other tool in the list is currently interacting.</summary>
    public static bool IsAnotherToolInteracting(IReadOnlyList<ToolEntry> tools, int currentIndex)
    {
        for (var i = 0; i < tools.Count; i++)
        {
            if (i != currentIndex && (tools[i].Dragging || tools[i].Resizing))
                return true;
        }
        return false;
    }

    // ── Private Snap Helpers ────────────────────────────────────────────

    private static void SnapPosition(ToolComponent t, DrawContext ctx, LayoutGridSettings gridSettings)
    {
        try
        {
            var subdivisions = Math.Max(1, gridSettings.Subdivisions);
            var subW = ctx.CellW / subdivisions;
            var subH = ctx.CellH / subdivisions;
            var snapped = t.Position;
            snapped.X = MathF.Round(snapped.X / subW) * subW;
            snapped.Y = MathF.Round(snapped.Y / subH) * subH;
            // Clamp after snapping
            var minX = ctx.ContentMin.X - ctx.ContentOrigin.X;
            var minY = ctx.ContentMin.Y - ctx.ContentOrigin.Y;
            var maxX = (ctx.ContentMax.X - ctx.ContentOrigin.X) - t.Size.X;
            var maxY = (ctx.ContentMax.Y - ctx.ContentOrigin.Y) - t.Size.Y;
            snapped.X = MathF.Max(minX, MathF.Min(maxX, snapped.X));
            snapped.Y = MathF.Max(minY, MathF.Min(maxY, snapped.Y));
            t.Position = snapped;

            // Update grid coordinates
            if (ctx.CellW > 0 && ctx.CellH > 0)
            {
                t.GridCol = t.Position.X / ctx.CellW;
                t.GridRow = t.Position.Y / ctx.CellH;
                t.HasGridCoords = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"Drag snap error: {ex.Message}");
        }
    }

    private static void SnapSize(ToolComponent t, DrawContext ctx, LayoutGridSettings gridSettings)
    {
        try
        {
            var subdivisions = Math.Max(1, gridSettings.Subdivisions);
            var subW = ctx.CellW / subdivisions;
            var subH = ctx.CellH / subdivisions;
            var snappedSize = t.Size;
            snappedSize.X = MathF.Max(MinToolWidth, MathF.Round(snappedSize.X / subW) * subW);
            snappedSize.Y = MathF.Max(MinToolHeight, MathF.Round(snappedSize.Y / subH) * subH);
            // Clamp so size doesn't exceed content
            var maxW = (ctx.ContentMax.X - ctx.ContentOrigin.X) - t.Position.X;
            var maxH = (ctx.ContentMax.Y - ctx.ContentOrigin.Y) - t.Position.Y;
            snappedSize.X = MathF.Min(snappedSize.X, MathF.Max(MinToolWidth, maxW));
            snappedSize.Y = MathF.Min(snappedSize.Y, MathF.Max(MinToolHeight, maxH));
            t.Size = snappedSize;

            // Update grid coordinates
            if (ctx.CellW > 0 && ctx.CellH > 0)
            {
                t.GridColSpan = t.Size.X / ctx.CellW;
                t.GridRowSpan = t.Size.Y / ctx.CellH;
                t.HasGridCoords = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"Resize snap error: {ex.Message}");
        }
    }
}
