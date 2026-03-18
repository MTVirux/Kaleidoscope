using System.Numerics;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.MainWindow.Tools.Data;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Manages right-click context menus for the content area and individual tools.
/// Extracted from WindowContentContainer.Drawing.cs for single-responsibility.
/// </summary>
internal sealed class ContextMenuManager
{
    // Popup state
    private int _contextToolIndex = -1;
    private string? _pendingPopup = null;
    private Vector2 _pendingPopupPos = Vector2.Zero;
    private Vector2 _lastContextClickRel;

    // Layout modal state
    private bool _saveLayoutPopupOpen = false;
    private string _layoutNameBuffer = string.Empty;
    private bool _newLayoutPopupOpen = false;
    private string _newLayoutNameBuffer = string.Empty;

    /// <summary>
    /// Detects right-clicks over tools or empty content area and queues appropriate popups.
    /// </summary>
    public void HandleRightClick(DrawContext ctx, IReadOnlyList<ToolEntry> tools)
    {
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var isOverContent = mouse.X >= ctx.ContentMin.X && mouse.X <= ctx.ContentMax.X &&
                            mouse.Y >= ctx.ContentMin.Y && mouse.Y <= ctx.ContentMax.Y;

        if (!isOverContent || !io.MouseClicked[1]) return;

        // Check if click is over an existing tool's header or border
        var clickedTool = -1;
        try
        {
            for (var ti = 0; ti < tools.Count; ti++)
            {
                var tt = tools[ti].Tool;
                if (!tt.Visible) continue;
                var tmin = tt.Position + ctx.ContentOrigin;
                var tmax = tmin + tt.Size;

                if (mouse.X >= tmin.X && mouse.X <= tmax.X && mouse.Y >= tmin.Y && mouse.Y <= tmax.Y)
                {
                    const float borderThickness = 4f;
                    var titleHeight = MathF.Min(24f, tt.Size.Y);

                    var isInHeader = tt.HeaderVisible && mouse.Y <= tmin.Y + titleHeight;
                    var isInBorder = mouse.X <= tmin.X + borderThickness ||
                                     mouse.X >= tmax.X - borderThickness ||
                                     mouse.Y <= tmin.Y + borderThickness ||
                                     mouse.Y >= tmax.Y - borderThickness;

                    if (isInHeader || isInBorder)
                    {
                        clickedTool = ti;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"Tool click detection error: {ex.Message}");
        }

        if (clickedTool >= 0)
        {
            _contextToolIndex = clickedTool;
            _lastContextClickRel = mouse - ctx.ContentOrigin;
            _pendingPopup = "tool_context_menu";
            _pendingPopupPos = mouse;
        }
        else if (ctx.EditMode)
        {
            _lastContextClickRel = mouse - ctx.ContentOrigin;
            _pendingPopup = "content_context_menu";
            _pendingPopupPos = mouse;
        }
    }

    /// <summary>Opens any popup that was queued in the previous frame.</summary>
    public void OpenPendingPopup()
    {
        if (_pendingPopup != null)
        {
            ImGui.SetNextWindowPos(_pendingPopupPos);
            ImGui.OpenPopup(_pendingPopup);
            _pendingPopup = null;
        }
    }

    /// <summary>
    /// Draws the content area context menu (add tools, layout operations).
    /// </summary>
    public void DrawContentContextMenu(
        DrawContext ctx,
        ILayoutHost host,
        IReadOnlyList<ToolRegistration> toolRegistry,
        Action<ToolComponent> addToolInstance,
        LayoutGridSettings gridSettings,
        DialogManager dialogManager)
    {
        // Keep popup open even if temp edit mode keys are released
        if (!ctx.EditMode && !ImGui.IsPopupOpen("content_context_menu")) return;

        if (!ImGui.BeginPopup("content_context_menu")) return;

        try
        {
            if (ImGui.BeginMenu("Add tool"))
            {
                var rootNode = new MenuNode();

                foreach (var reg in toolRegistry)
                {
                    var path = (reg.CategoryPath ?? "").Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                    var cur = rootNode;
                    foreach (var part in path)
                    {
                        if (!cur.Children.TryGetValue(part, out var child))
                        {
                            child = new MenuNode();
                            cur.Children[part] = child;
                        }
                        cur = child;
                    }
                    cur.Items.Add(reg);
                }

                DrawMenuNode(rootNode, ctx, gridSettings, addToolInstance);
                ImGui.EndMenu();
            }

            ImGui.Separator();

            // Layout info line
            var layoutName = host.GetCurrentLayoutName();
            var isDirty = host.IsDirty;
            var displayName = isDirty ? $"{layoutName} *" : layoutName;
            ImGui.TextDisabled($"Layout: {displayName}");

            // Save Layout (enabled only when dirty)
            if (ImGui.MenuItem("Save Layout", isDirty))
            {
                try { host.SaveLayoutExplicit(); }
                catch (Exception ex) { LogService.Error(LogCategory.UI, "Failed to save layout", ex); }
            }

            // Discard Changes (only shown when dirty)
            if (isDirty)
            {
                if (ImGui.MenuItem("Discard Changes"))
                {
                    try { host.DiscardChanges(); }
                    catch (Exception ex) { LogService.Error(LogCategory.UI, "Failed to discard changes", ex); }
                }
            }

            ImGui.Separator();

            // New / Save As / Load layouts
            if (ImGui.MenuItem("New layout..."))
            {
                _newLayoutNameBuffer = "";
                _newLayoutPopupOpen = true;
            }

            if (ImGui.MenuItem("Save layout as.."))
            {
                _layoutNameBuffer = "";
                _saveLayoutPopupOpen = true;
            }

            if (ImGui.BeginMenu("Load layout"))
            {
                try
                {
                    var names = host.GetAvailableLayoutNames();
                    foreach (var n in names)
                    {
                        if (ImGui.MenuItem(n))
                        {
                            try { host.LoadLayout(n); }
                            catch (Exception ex) { LogService.Error(LogCategory.UI, $"Failed to load layout '{n}'", ex); }
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.UI, "Failed to get layout names", ex);
                }
                ImGui.EndMenu();
            }

            ImGui.Separator();

            // Edit grid resolution
            if (ImGui.MenuItem("Edit grid resolution..."))
            {
                dialogManager.OpenGridResolution(gridSettings, ctx.EffectiveCols, ctx.EffectiveRows);
            }

            // Manage Layouts
            if (ImGui.MenuItem("Manage Layouts..."))
            {
                try { host.OpenLayoutsManager(); }
                catch (Exception ex) { LogService.Error(LogCategory.UI, "Failed to open layouts manager", ex); }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, "Error in context menu", ex);
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// Draws the layout save/new modals that can be opened from the content context menu.
    /// </summary>
    public void DrawLayoutModals(DrawContext ctx, bool editMode, ILayoutHost host, DialogManager dialogManager)
    {
        if (!editMode && !_saveLayoutPopupOpen && !_newLayoutPopupOpen && !dialogManager.IsGridResolutionOpen)
            return;

        // Save layout modal
        if (_saveLayoutPopupOpen && !ImGui.IsPopupOpen("save_layout_popup"))
        {
            ImGui.OpenPopup("save_layout_popup");
        }
        if (ImGui.BeginPopupModal("save_layout_popup", ref _saveLayoutPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            try
            {
                ImGui.TextUnformatted("Enter a name for this layout:");
                ImGui.InputText("##layoutname", ref _layoutNameBuffer, ConfigStatic.TextInputBufferSize);
                if (ImGui.Button("Save"))
                {
                    if (!string.IsNullOrWhiteSpace(_layoutNameBuffer))
                    {
                        try { host.SaveLayout(_layoutNameBuffer, null!); }
                        catch (Exception ex) { LogService.Error(LogCategory.UI, $"Failed to save layout '{_layoutNameBuffer}'", ex); }
                        ImGui.CloseCurrentPopup();
                        _saveLayoutPopupOpen = false;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    _saveLayoutPopupOpen = false;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.UI, "Error in save layout popup", ex);
            }
            ImGui.EndPopup();
        }

        // New layout modal
        if (_newLayoutPopupOpen && !ImGui.IsPopupOpen("new_layout_popup"))
        {
            ImGui.OpenPopup("new_layout_popup");
        }
        if (ImGui.BeginPopupModal("new_layout_popup", ref _newLayoutPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            try
            {
                ImGui.TextUnformatted("Enter a name for the new layout:");
                ImGui.InputText("##newlayoutname", ref _newLayoutNameBuffer, ConfigStatic.TextInputBufferSize);
                if (ImGui.Button("Create"))
                {
                    if (!string.IsNullOrWhiteSpace(_newLayoutNameBuffer))
                    {
                        try { host.SaveLayout(_newLayoutNameBuffer, new List<ToolLayoutState>()); }
                        catch (Exception ex) { LogService.Error(LogCategory.UI, $"Failed to create layout '{_newLayoutNameBuffer}'", ex); }
                        try { host.LoadLayout(_newLayoutNameBuffer); }
                        catch (Exception ex) { LogService.Error(LogCategory.UI, $"Failed to load new layout '{_newLayoutNameBuffer}'", ex); }
                        ImGui.CloseCurrentPopup();
                        _newLayoutPopupOpen = false;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    _newLayoutPopupOpen = false;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.UI, "Error in new layout popup", ex);
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// Draws the tool-specific context menu (rename, duplicate, appearance, settings, remove).
    /// </summary>
    public void DrawToolContextMenu(
        DrawContext ctx,
        IReadOnlyList<ToolEntry> tools,
        ILayoutHost host,
        DialogManager dialogManager,
        Action<ToolComponent> duplicateTool,
        Action markDirty,
        Action<int> removeTool)
    {
        if (!ImGui.BeginPopup("tool_context_menu")) return;

        try
        {
            // If index became invalid, try to resolve by click position
            if (!(_contextToolIndex >= 0 && _contextToolIndex < tools.Count))
            {
                try
                {
                    var wp = ImGui.GetWindowPos();
                    var co = wp + ImGui.GetWindowContentRegionMin();
                    var absClick = co + _lastContextClickRel;
                    var found = -1;
                    for (var ti = 0; ti < tools.Count; ti++)
                    {
                        var tt = tools[ti].Tool;
                        if (!tt.Visible) continue;
                        var tmin = tt.Position + co;
                        var tmax = tmin + tt.Size;
                        if (absClick.X >= tmin.X && absClick.X <= tmax.X && absClick.Y >= tmin.Y && absClick.Y <= tmax.Y)
                        {
                            found = ti;
                            break;
                        }
                    }
                    if (found >= 0) _contextToolIndex = found;
                }
                catch (Exception ex)
                {
                    LogService.Debug(LogCategory.UI, $"Tool context find error: {ex.Message}");
                }
            }

            if (_contextToolIndex >= 0 && _contextToolIndex < tools.Count)
            {
                var t = tools[_contextToolIndex].Tool;
                ImGui.TextUnformatted(t.DisplayTitle ?? "Tool");
                ImGui.Separator();

                // Tool-specific context menu options (shown first)
                var customOptions = t.GetContextMenuOptions();
                if (customOptions != null && customOptions.Count > 0)
                {
                    foreach (var option in customOptions)
                    {
                        if (option.SeparatorBefore) ImGui.Separator();

                        var label = option.Icon != null ? $"{option.Icon} {option.Label}" : option.Label;

                        if (option.IsChecked.HasValue)
                        {
                            var isChecked = option.IsChecked.Value;
                            if (ImGui.MenuItem(label, option.Shortcut ?? "", isChecked, option.Enabled))
                            {
                                option.OnClick();
                                ImGui.CloseCurrentPopup();
                            }
                        }
                        else
                        {
                            if (ImGui.MenuItem(label, option.Shortcut ?? "", false, option.Enabled))
                            {
                                option.OnClick();
                                ImGui.CloseCurrentPopup();
                            }
                        }

                        if (option.Tooltip != null && ImGui.IsItemHovered())
                            ImGui.SetTooltip(option.Tooltip);

                        if (option.SeparatorAfter) ImGui.Separator();
                    }
                    ImGui.Separator();
                }

                // Rename option
                if (ImGui.MenuItem("Rename..."))
                {
                    ImGui.CloseCurrentPopup();
                    dialogManager.OpenRenameModal(_contextToolIndex, t.CustomTitle ?? t.Title ?? "");
                }

                // Duplicate option
                if (ImGui.MenuItem("Duplicate"))
                {
                    try { duplicateTool(t); }
                    catch (Exception ex) { LogService.Error(LogCategory.UI, "Failed to duplicate tool", ex); }
                    ImGui.CloseCurrentPopup();
                }

                ImGui.Separator();

                if (ImGui.BeginMenu("Appearance"))
                {
                    var bg = t.BackgroundEnabled;
                    if (ImGui.Checkbox("Show background", ref bg)) { t.BackgroundEnabled = bg; markDirty(); }
                    var hdr = t.HeaderVisible;
                    if (ImGui.Checkbox("Show header", ref hdr)) { t.HeaderVisible = hdr; markDirty(); }
                    var outline = t.OutlineEnabled;
                    if (ImGui.Checkbox("Show outline", ref outline)) { t.OutlineEnabled = outline; markDirty(); }

                    ImGui.Separator();

                    var defaultBgColor = host.ConfigService?.Config.UIColors.ToolBackground
                        ?? new Vector4(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
                    var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithReset(
                        "Background color", t.BackgroundColor, defaultBgColor, "Background color");
                    if (colorChanged) { t.BackgroundColor = newColor; markDirty(); }

                    ImGui.EndMenu();
                }

                ImGui.Separator();

                // Tool settings
                if (t.HasSettings && ImGui.MenuItem("Settings..."))
                {
                    ImGui.CloseCurrentPopup();
                    dialogManager.OpenToolSettings(_contextToolIndex);
                }

                // Save as Preset (for Data tools)
                if (t is DataTool && host.CanSavePresets)
                {
                    if (ImGui.MenuItem($"Save {t.ToolName} Preset"))
                    {
                        ImGui.CloseCurrentPopup();
                        dialogManager.OpenSavePreset(_contextToolIndex);
                    }
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Remove component"))
                {
                    try { removeTool(_contextToolIndex); }
                    catch (Exception ex) { LogService.Error(LogCategory.UI, "Failed to remove component", ex); }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.Separator();
                if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, "Error in tool context menu", ex);
        }
        ImGui.EndPopup();
        _contextToolIndex = -1;
    }

    // ── Private Helpers ─────────────────────────────────────────────────

    private void DrawMenuNode(
        MenuNode node, DrawContext ctx, LayoutGridSettings gridSettings,
        Action<ToolComponent> addToolInstance)
    {
        foreach (var reg in node.Items)
        {
            if (ImGui.MenuItem(reg.Label))
            {
                try
                {
                    var tool = reg.Factory(_lastContextClickRel);
                    if (tool != null)
                    {
                        tool.Id = reg.Id;
                        try
                        {
                            var subdivisions = Math.Max(1, gridSettings.Subdivisions);
                            var subW = ctx.CellW / subdivisions;
                            var subH = ctx.CellH / subdivisions;
                            tool.Position = new Vector2(
                                MathF.Round(tool.Position.X / subW) * subW,
                                MathF.Round(tool.Position.Y / subH) * subH);

                            if (ctx.CellW > 0 && ctx.CellH > 0)
                            {
                                tool.GridCol = tool.Position.X / ctx.CellW;
                                tool.GridRow = tool.Position.Y / ctx.CellH;
                                tool.GridColSpan = tool.Size.X / ctx.CellW;
                                tool.GridRowSpan = tool.Size.Y / ctx.CellH;
                                tool.HasGridCoords = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug(LogCategory.UI, $"Tool snap error: {ex.Message}");
                        }
                        addToolInstance(tool);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.UI, $"Failed to create tool '{reg.Id}'", ex);
                }
            }
        }

        foreach (var kv in node.Children)
        {
            if (ImGui.BeginMenu(kv.Key))
            {
                DrawMenuNode(kv.Value, ctx, gridSettings, addToolInstance);
                ImGui.EndMenu();
            }
        }
    }

    /// <summary>Helper class for building hierarchical tool menus.</summary>
    private sealed class MenuNode
    {
        public readonly Dictionary<string, MenuNode> Children = new();
        public readonly List<ToolRegistration> Items = new();
    }
}
