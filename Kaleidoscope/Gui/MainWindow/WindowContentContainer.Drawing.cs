using System.Numerics;
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
                foreach (var te in Tools)
                {
                    var t = te.Tool;
                    if (t.HasGridCoords)
                    {
                        t.Position = new Vector2(t.GridCol * cellW, t.GridRow * cellH);
                        t.Size = new Vector2(
                            MathF.Max(MinToolWidth, t.GridColSpan * cellW),
                            MathF.Max(MinToolHeight, t.GridRowSpan * cellH));
                        if (cellW > 0) t.GridColSpan = t.Size.X / cellW;
                        if (cellH > 0) t.GridRowSpan = t.Size.Y / cellH;
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
                t.GridCol = t.Position.X / cellW;
                t.GridRow = t.Position.Y / cellH;
                t.GridColSpan = t.Size.X / cellW;
                t.GridRowSpan = t.Size.Y / cellH;
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
        ContextMenus.DrawContentContextMenu(ctx, Host!, ToolRegistry, AddToolInstance, _currentGridSettings, Dialogs);

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

        // ── 10. Render Tools ────────────────────────────────────────────
        for (var i = 0; i < Tools.Count; i++)
        {
            var te = Tools[i];
            var t = te.Tool;
            if (!t.Visible) continue;

            ImGui.SetCursorScreenPos(t.Position + contentOrigin);
            var id = $"tool_{i}_{t.Id}";

            ImGui.PushID(id);

            // Background
            try
            {
                if (t.BackgroundEnabled)
                {
                    var screenMin = t.Position + contentOrigin;
                    var screenMax = screenMin + t.Size;
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
            ImGui.BeginChild(id, t.Size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.PopStyleVar();

            var contentWidth = t.Size.X - internalPadding * 2;
            ImGui.BeginGroup();
            ImGui.PushItemWidth(MathF.Max(50f, contentWidth));

            if (t.HeaderVisible)
            {
                ImGui.TextUnformatted(t.DisplayTitle);
                ImGui.Separator();
            }

            // Draw tool content with optional profiling
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

            ImGui.PopItemWidth();
            ImGui.EndGroup();

            var isChildFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows);
            ImGui.EndChild();

            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();

            // ── 11. Edit-Mode Interactions ──────────────────────────────
            if (editMode)
            {
                if (t.OutlineEnabled)
                    dl.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border));

                var mainWindowInteracting = Host?.IsMainWindowInteracting ?? false;
                var anotherToolInteracting = ToolInteractionManager.IsAnotherToolInteracting(Tools, i);

                Interactions.HandleDrag(ctx, te, i, _currentGridSettings,
                    mainWindowInteracting, anotherToolInteracting, isChildFocused, MarkLayoutDirty);

                Interactions.HandleResize(ctx, te, i, _currentGridSettings,
                    mainWindowInteracting, anotherToolInteracting, isChildFocused, MarkLayoutDirty);
            }

            ImGui.PopID();
        }

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
}