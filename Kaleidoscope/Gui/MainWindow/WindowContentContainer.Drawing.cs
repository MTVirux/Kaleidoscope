using System.Numerics;
using Kaleidoscope.Gui.Animation;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

public sealed partial class WindowContentContainer
{
    public void Draw(bool editMode) => Draw(editMode, null);

    public void Draw(bool editMode, ProfilerService? profilerService)
    {
        var dl = ImGui.GetWindowDrawList();

        // ── 1. Compute DrawContext ──────────────────────────────────────
        var windowPos = ImGui.GetWindowPos();
        var contentMinRel = ImGui.GetWindowContentRegionMin();
        var contentMaxRel = ImGui.GetWindowContentRegionMax();
        var contentMin = windowPos + contentMinRel;
        var contentMax = windowPos + contentMaxRel;
        var contentOrigin = contentMin;
        var availRegion = contentMax - contentMin;

        var effectiveCols = GetEffectiveColumns(availRegion);
        var effectiveRows = GetEffectiveRows(availRegion);
        var cellW = availRegion.X / MathF.Max(1f, effectiveCols);
        var cellH = availRegion.Y / MathF.Max(1f, effectiveRows);

        var ctx = new DrawContext(
            dl, contentMin, contentMax, contentOrigin,
            availRegion, effectiveCols, effectiveRows, cellW, cellH, editMode);

        // ── 2. Handle Window Resize ─────────────────────────────────────
        if (_lastContentSize != availRegion)
        {
            try
            {
                if (CurrentArrangement != LayoutArrangement.Grid && Tools.Count > 0)
                {
                    // Auto-layout: re-apply preset so tools redistribute for the new size
                    var toolList = new List<ToolComponent>(Tools.Count);
                    foreach (var te in Tools) toolList.Add(te.Tool);
                    AutoLayoutEngine.ApplyPreset(CurrentArrangement, toolList, effectiveCols, effectiveRows);
                }

                foreach (var te in Tools)
                {
                    var t = te.Tool;
                    if (t.HasGridCoords)
                    {
                        t.Position = GridMath.GridToPixelPos(t.GridCol, t.GridRow, cellW, cellH);
                        t.Size = GridMath.GridToPixelSize(t.GridColSpan, t.GridRowSpan, cellW, cellH, MinToolWidth, MinToolHeight);
                        if (cellW > 0) t.GridColSpan = GridMath.PixelToGrid(t.Size.X, cellW);
                        if (cellH > 0) t.GridRowSpan = GridMath.PixelToGrid(t.Size.Y, cellH);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.UI, $"Window resize tool update error: {ex.Message}");
            }
        }
        _lastContentSize = availRegion;

        // ── 3. Initialize Grid Coords ───────────────────────────────────
        foreach (var te in Tools)
        {
            var t = te.Tool;
            if (!t.HasGridCoords && cellW > 0 && cellH > 0)
            {
                t.GridCol = GridMath.PixelToGrid(t.Position.X, cellW);
                t.GridRow = GridMath.PixelToGrid(t.Position.Y, cellH);
                t.GridColSpan = GridMath.PixelToGrid(t.Size.X, cellW);
                t.GridRowSpan = GridMath.PixelToGrid(t.Size.Y, cellH);
                t.HasGridCoords = true;
            }
        }

        // ── 4. Grid Overlay (edit mode) ─────────────────────────────────
        if (editMode)
        {
            GridRenderer.DrawGrid(ctx, _currentGridSettings);
        }

        // ── 5. Right-Click Detection ────────────────────────────────────
        ContextMenus.HandleRightClick(ctx, Tools);

        // ── 6. Content Context Menu ─────────────────────────────────────
        ContextMenus.DrawContentContextMenu(ctx, Host!, ToolRegistry, AddToolInstance, _currentGridSettings, Dialogs, ApplyArrangement);

        // ── 7. Layout Modals ────────────────────────────────────────────
        ContextMenus.DrawLayoutModals(ctx, editMode, Host!, Dialogs);

        // ── 8. Grid Resolution Modal ────────────────────────────────────
        if (editMode || Dialogs.IsGridResolutionOpen)
        {
            Dialogs.DrawGridResolutionModal(ctx, _currentGridSettings,
                UpdateGridSettings,
                gs => { try { Host?.NotifyGridSettingsChanged(gs); } catch (Exception ex) { LogService.Debug(LogCategory.UI, $"NotifyGridSettingsChanged error: {ex.Message}"); } });
        }

        // ── 9. Unsaved Changes Dialog ───────────────────────────────────
        if (Host != null) Dialogs.DrawUnsavedChangesDialog(Host);

        // ── 9b. Update Animations ───────────────────────────────────────
        var dt = ImGui.GetIO().DeltaTime;
        Animator.Update(dt);

        // ── 9c. Reap Completed Fade-Outs ────────────────────────────────
        for (var i = Tools.Count - 1; i >= 0; i--)
        {
            var te = Tools[i];
            if (te.PendingRemoval && !Animator.IsAnimating($"{te.AnimKey}_alpha"))
            {
                Animator.Cancel($"{te.AnimKey}_pos");
                Animator.Cancel($"{te.AnimKey}_size");
                Animator.Cancel($"{te.AnimKey}_hover");
                te.Tool.Dispose();
                Tools.RemoveAt(i);
                MarkLayoutDirty();
            }
        }

        // ── 10-11. Render Tools + Edit-Mode Interactions ────────────────
        for (var i = 0; i < Tools.Count; i++)
            RenderTool(i, Tools[i], ctx, profilerService);

        // ── 12. Open Pending Popups ─────────────────────────────────────
        ContextMenus.OpenPendingPopup();

        // ── 13. Update Global Interaction State ─────────────────────────
        if (Host != null) Interactions.UpdateGlobalState(Tools, Host);

        // ── 14. Tool Context Menu ───────────────────────────────────────
        ContextMenus.DrawToolContextMenu(ctx, Tools, Host!, Dialogs, DuplicateTool, MarkLayoutDirty, RemoveTool);

        // ── 15. Tool Settings / Rename / Preset Dialogs ─────────────────
        if (Host != null) Dialogs.DrawToolSettingsWindow(Tools, Host);
        Dialogs.DrawToolRenameModal(Tools, MarkLayoutDirty);
        if (Host != null) Dialogs.DrawSavePresetModal(Tools, Host);
    }

    /// <summary>
    /// Renders a single tool (steps 10-11 of the frame pipeline): background, child window,
    /// tool content, and — in edit mode — outline, hover highlight, snap ghost, and drag/resize handling.
    /// </summary>
    private void RenderTool(int i, ToolEntry te, DrawContext ctx, ProfilerService? profilerService)
    {
        var dl = ctx.DrawList;
        var contentMin = ctx.ContentMin;
        var contentMax = ctx.ContentMax;
        var contentOrigin = ctx.ContentOrigin;
        var editMode = ctx.EditMode;

        var t = te.Tool;
        if (!t.Visible && !te.PendingRemoval) return;

        // Occlusion culling: skip tools entirely outside the visible content region
        var toolScreenMin = t.Position + contentOrigin;
        var toolScreenMax = toolScreenMin + t.Size;
        if (toolScreenMax.X < contentMin.X || toolScreenMin.X > contentMax.X ||
            toolScreenMax.Y < contentMin.Y || toolScreenMin.Y > contentMax.Y)
            return;

        // Resolve animated position/size (falls back to model values when no animation is active)
        var animPos = Animator.GetVec2($"{te.AnimKey}_pos", t.Position);
        var animSize = Animator.GetVec2($"{te.AnimKey}_size", t.Size);

        ImGui.SetCursorScreenPos(animPos + contentOrigin);
        var id = $"tool_{i}_{t.Id}";

        ImGui.PushID(id);

        // Alpha: use animation value (1.0 = fully visible when no animation active)
        var alpha = Animator.Get($"{te.AnimKey}_alpha", 1f);
        var pushedAlpha = false;
        if (alpha < 0.999f)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * alpha);
            pushedAlpha = true;
        }

        // Background
        try
        {
            if (t.BackgroundEnabled)
            {
                var screenMin = animPos + contentOrigin;
                var screenMax = screenMin + animSize;
                var col = ImGui.GetColorU32(t.BackgroundColor);
                dl.AddRectFilled(screenMin, screenMax, col);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"Background draw error: {ex.Message}");
        }

        // Internal padding from external source or local settings
        var externalPadding = Host?.GetExternalToolInternalPadding() ?? -1;
        if (externalPadding >= 0)
            _currentGridSettings.ToolInternalPaddingPx = externalPadding;
        var internalPadding = (float)Math.Max(0, _currentGridSettings.ToolInternalPaddingPx);

        // Child window with tool content
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(internalPadding, internalPadding));
        ImGui.BeginChild(id, animSize, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        var contentWidth = animSize.X - internalPadding * 2;
        ImGui.BeginGroup();
        ImGui.PushItemWidth(MathF.Max(50f, contentWidth));

        if (t.HeaderVisible)
        {
            ImGui.TextUnformatted(t.DisplayTitle);
            ImGui.Separator();
        }

        // Draw tool content with optional profiling (skip for tools being removed).
        // Guard each tool's render so a throwing tool can't tear down the whole container frame.
        if (!te.PendingRemoval)
        {
            try
            {
                if (profilerService != null)
                {
                    using (profilerService.BeginToolScope(t.Id, t.DisplayTitle))
                    {
                        t.RenderToolContent();
                    }
                }
                else
                {
                    t.RenderToolContent();
                }
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
                LogService.Debug(LogCategory.UI, $"Tool render error [{t.Id}]: {ex.Message}");
            }
        }

        ImGui.PopItemWidth();
        ImGui.EndGroup();

        var isChildFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows);
        ImGui.EndChild();

        if (pushedAlpha)
            ImGui.PopStyleVar();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        // ── 11. Edit-Mode Interactions ──────────────────────────────
        if (editMode && !te.PendingRemoval)
        {
            // Outline
            if (t.OutlineEnabled)
                dl.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border));

            // Hover highlight: subtle accent border on mouse hover
            var mouse = ImGui.GetIO().MousePos;
            var isHovered = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y
                            && !te.Dragging && !te.Resizing;
            if (isHovered && !Animator.IsAnimating($"{te.AnimKey}_hover"))
                Animator.Start($"{te.AnimKey}_hover", 0f, 1f, 0.10f, Easing.QuadOut);
            else if (!isHovered && Animator.Get($"{te.AnimKey}_hover", 0f) > 0f && !Animator.IsAnimating($"{te.AnimKey}_hover"))
                Animator.Start($"{te.AnimKey}_hover", Animator.Get($"{te.AnimKey}_hover", 0f), 0f, 0.10f, Easing.QuadIn);

            var hoverAlpha = Animator.Get($"{te.AnimKey}_hover", 0f);
            if (hoverAlpha > 0.01f)
            {
                var accentColor = new Vector4(0.26f, 0.59f, 0.98f, 0.5f * hoverAlpha);
                dl.AddRect(min, max, ImGui.GetColorU32(accentColor), 0f, ImDrawFlags.None, 2f);
            }

            // Snap ghost during drag: draw outline at snap-target position
            if (te.Dragging)
            {
                var snapTarget = ComputeSnapTarget(t, ctx, _currentGridSettings);
                var ghostMin = snapTarget + contentOrigin;
                var ghostMax = ghostMin + t.Size;
                var ghostColor = new Vector4(0.26f, 0.59f, 0.98f, 0.30f);
                dl.AddRect(ghostMin, ghostMax, ImGui.GetColorU32(ghostColor), 0f, ImDrawFlags.None, 1.5f);
            }

            var mainWindowInteracting = Host?.IsMainWindowInteracting ?? false;
            var anotherToolInteracting = ToolInteractionManager.IsAnotherToolInteracting(Tools, i);

            Interactions.HandleDrag(ctx, te, i, _currentGridSettings,
                mainWindowInteracting, anotherToolInteracting, isChildFocused, MarkLayoutDirtyManualOverride, Animator);

            Interactions.HandleResize(ctx, te, i, _currentGridSettings,
                mainWindowInteracting, anotherToolInteracting, isChildFocused, MarkLayoutDirtyManualOverride, Animator);
        }

        ImGui.PopID();
    }

    /// <summary>Computes the snap-target position for a tool without applying it (for ghost rendering).</summary>
    private static Vector2 ComputeSnapTarget(ToolComponent t, DrawContext ctx, LayoutGridSettings gridSettings)
    {
        var subW = GridMath.SubdivisionSize(ctx.CellW, gridSettings.Subdivisions);
        var subH = GridMath.SubdivisionSize(ctx.CellH, gridSettings.Subdivisions);
        var snapped = t.Position;
        snapped.X = GridMath.Snap(snapped.X, subW, guardZeroStep: true);
        snapped.Y = GridMath.Snap(snapped.Y, subH, guardZeroStep: true);
        var minX = ctx.ContentMin.X - ctx.ContentOrigin.X;
        var minY = ctx.ContentMin.Y - ctx.ContentOrigin.Y;
        var maxX = (ctx.ContentMax.X - ctx.ContentOrigin.X) - t.Size.X;
        var maxY = (ctx.ContentMax.Y - ctx.ContentOrigin.Y) - t.Size.Y;
        snapped.X = MathF.Max(minX, MathF.Min(maxX, snapped.X));
        snapped.Y = MathF.Max(minY, MathF.Min(maxY, snapped.Y));
        return snapped;
    }
}