using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

public partial class WindowContentContainer
{

        public void Draw(bool editMode) => Draw(editMode, null);

        public void Draw(bool editMode, ProfilerService? profilerService)
        {
            var dl = ImGui.GetWindowDrawList();

            // Compute content origin and available region once using the
            // window content region APIs so we cover the full client area.
            var windowPos = ImGui.GetWindowPos();
            var contentMinRel = ImGui.GetWindowContentRegionMin();
            var contentMaxRel = ImGui.GetWindowContentRegionMax();
            var contentMin = windowPos + contentMinRel;
            var contentMax = windowPos + contentMaxRel;
            var contentOrigin = contentMin;
            var availRegion = contentMax - contentMin;

            // Get effective grid dimensions from layout settings
            var effectiveCols = GetEffectiveColumns(availRegion);
            var effectiveRows = GetEffectiveRows(availRegion);
            
            // Compute grid cell size based on layout settings
            var cellW = availRegion.X / MathF.Max(1f, effectiveCols);
            var cellH = availRegion.Y / MathF.Max(1f, effectiveRows);
            
            // Handle window resize: update tool positions based on grid coordinates
            // Also handle initial layout pass when _lastContentSize is zero so tools
            // that have grid coordinates are placed correctly on first render
            if (_lastContentSize != availRegion)
            {
                try
                {
                    // Update all tools to new positions based on their grid coordinates
                    foreach (var te in _tools)
                    {
                        var t = te.Tool;
                        if (t.HasGridCoords)
                        {
                            // Recalculate pixel position from grid coordinates
                            t.Position = new Vector2(t.GridCol * cellW, t.GridRow * cellH);
                            t.Size = new Vector2(
                                MathF.Max(MinToolWidth, t.GridColSpan * cellW),
                                MathF.Max(MinToolHeight, t.GridRowSpan * cellH)
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Debug(LogCategory.UI, $"Window resize tool update error: {ex.Message}");
                }
            }
            _lastContentSize = availRegion;
            
            // Initialize grid coordinates for tools that don't have them
            foreach (var te in _tools)
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

            // If in edit mode, draw a grid overlay to help alignment
            if (editMode)
            {
                try
                {
                    var subdivisions = Math.Max(1, _currentGridSettings.Subdivisions);
                    // minor (subdivision) lines color (very faint)
                    var minorColor = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.03f));
                    // major (cell) lines color (slightly stronger)
                    var majorColor = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.08f));

                    // Draw grid lines for each column and row
                    // Major lines at cell boundaries, minor lines at subdivision boundaries
                    var subW = cellW / subdivisions;
                    var subH = cellH / subdivisions;

                    // To avoid heavy rendering, cap the number of lines drawn
                    const int MaxLines = ConfigStatic.MaxGridLines; // total per axis

                    // Vertical lines
                    var totalV = effectiveCols * subdivisions + 1;
                    var vStep = 1;
                    if (totalV > MaxLines) vStep = (int)MathF.Ceiling((float)totalV / MaxLines);
                    var vx = contentMin.X;
                    for (var iV = 0; iV <= totalV; iV++, vx += subW)
                    {
                        if (iV % vStep != 0) continue;
                        var isMajor = (iV % subdivisions == 0);
                        dl.AddLine(new Vector2(vx, contentMin.Y), new Vector2(vx, contentMax.Y), isMajor ? majorColor : minorColor, 1f);
                    }

                    // Horizontal lines
                    var totalH = effectiveRows * subdivisions + 1;
                    var hStep = 1;
                    if (totalH > MaxLines) hStep = (int)MathF.Ceiling((float)totalH / MaxLines);
                    var hy = contentMin.Y;
                    for (var iH = 0; iH <= totalH; iH++, hy += subH)
                    {
                        if (iH % hStep != 0) continue;
                        var isMajor = (iH % subdivisions == 0);
                        dl.AddLine(new Vector2(contentMin.X, hy), new Vector2(contentMax.X, hy), isMajor ? majorColor : minorColor, 1f);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Debug(LogCategory.UI, $"Grid drawing error: {ex.Message}");
                }
            }

            // Right-click detection for context menus (works outside edit mode for tool menus)
            {
                var io = ImGui.GetIO();
                var mouse = io.MousePos;
                var isOverContent = mouse.X >= contentMin.X && mouse.X <= contentMax.X && mouse.Y >= contentMin.Y && mouse.Y <= contentMax.Y;
                if (isOverContent && io.MouseClicked[1])
                    {
                        // If the click is over an existing tool, open the tool-specific popup (works without edit mode)
                        var clickedTool = -1;
                        try
                        {
                            for (var ti = 0; ti < _tools.Count; ti++)
                            {
                                var tt = _tools[ti].Tool;
                                if (!tt.Visible) continue;
                                var tmin = tt.Position + contentOrigin;
                                var tmax = tmin + tt.Size;
                                if (mouse.X >= tmin.X && mouse.X <= tmax.X && mouse.Y >= tmin.Y && mouse.Y <= tmax.Y)
                                {
                                    clickedTool = ti;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug(LogCategory.UI, $"Tool click detection error: {ex.Message}");
                        }

                        if (clickedTool >= 0)
                        {
                            // Tool context menu works without edit mode
                            _contextToolIndex = clickedTool;
                            _lastContextClickRel = mouse - contentOrigin;
                            // Defer popup opening by one frame to prevent z-order issues
                            _pendingPopup = "tool_context_menu";
                            _pendingPopupPos = mouse;
                        }
                        else if (editMode)
                        {
                            // Content context menu (add tools) only works in edit mode
                            _lastContextClickRel = mouse - contentOrigin;
                            // Defer popup opening by one frame to prevent z-order issues
                            _pendingPopup = "content_context_menu";
                            _pendingPopupPos = mouse;
                        }
                    }
                }

                // Content context menu for adding tools (edit mode only)
                if (editMode && ImGui.BeginPopup("content_context_menu"))
                {
                    try
                    {
                        if (ImGui.BeginMenu("Add tool"))
                        {
                            // Build a tree of categories from registrations
                            MenuNode rootNode = new MenuNode();

                            foreach (var reg in _toolRegistry)
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

                            // recursive draw
                            void DrawNode(MenuNode node)
                            {
                                // Draw items at this node first
                                foreach (var reg in node.Items)
                                {
                                    if (ImGui.MenuItem(reg.Label))
                                    {
                                        try
                                        {
                                            var tool = reg.Factory(_lastContextClickRel);
                                            if (tool != null)
                                            {
                                                // Set the tool's Id to match the registration so it can be duplicated/found later
                                                tool.Id = reg.Id;
                                                try
                                                {
                                                    var subdivisions = Math.Max(1, _currentGridSettings.Subdivisions);
                                                    var subW = cellW / subdivisions;
                                                    var subH = cellH / subdivisions;
                                                    tool.Position = new Vector2(
                                                        MathF.Round(tool.Position.X / subW) * subW,
                                                        MathF.Round(tool.Position.Y / subH) * subH
                                                    );

                                                    if (cellW > 0 && cellH > 0)
                                                    {
                                                        tool.GridCol = tool.Position.X / cellW;
                                                        tool.GridRow = tool.Position.Y / cellH;
                                                        tool.GridColSpan = tool.Size.X / cellW;
                                                        tool.GridRowSpan = tool.Size.Y / cellH;
                                                        tool.HasGridCoords = true;
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    LogService.Debug(LogCategory.UI, $"Tool snap error: {ex.Message}");
                                                }
                                                AddToolInstance(tool);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            LogService.Error(LogCategory.UI, $"Failed to create tool '{reg.Id}'", ex);
                                        }
                                    }
                                }

                                // Draw child menus
                                foreach (var kv in node.Children)
                                {
                                    var name = kv.Key;
                                    var child = kv.Value;
                                    if (ImGui.BeginMenu(name))
                                    {
                                        DrawNode(child);
                                        ImGui.EndMenu();
                                    }
                                }
                            }

                            DrawNode(rootNode);

                            ImGui.EndMenu();
                        }
                        ImGui.Separator();
                        
                        // Show current layout name and dirty indicator
                        var layoutName = GetCurrentLayoutName?.Invoke() ?? "Default";
                        var isDirty = GetIsDirty?.Invoke() ?? false;
                        var displayName = isDirty ? $"{layoutName} *" : layoutName;
                        ImGui.TextDisabled($"Layout: {displayName}");
                        
                        // Save Layout (explicit save action) - always shown, enabled only when dirty
                        if (ImGui.MenuItem("Save Layout", isDirty))
                        {
                            try
                            {
                                OnSaveLayoutExplicit?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                LogService.Error(LogCategory.UI, "Failed to save layout", ex);
                            }
                        }
                        
                        // Discard Changes - only shown when dirty
                        if (isDirty)
                        {
                            if (ImGui.MenuItem("Discard Changes"))
                            {
                                try
                                {
                                    OnDiscardChanges?.Invoke();
                                }
                                catch (Exception ex)
                                {
                                    LogService.Error(LogCategory.UI, "Failed to discard changes", ex);
                                }
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
                                var names = GetAvailableLayoutNames?.Invoke() ?? new List<string>();
                                foreach (var n in names)
                                {
                                    if (ImGui.MenuItem(n))
                                    {
                                        try
                                        {
                                            OnLoadLayout?.Invoke(n);
                                        }
                                        catch (Exception ex)
                                        {
                                            LogService.Error(LogCategory.UI, $"Failed to load layout '{n}'", ex);
                                        }
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
                            _editingGridSettings = _currentGridSettings.Clone();
                            _previousColumns = GetEffectiveColumns(availRegion);
                            _previousRows = GetEffectiveRows(availRegion);
                            _gridResolutionPopupOpen = true;
                        }
                        
                        // Manage Layouts - opens config window to layouts tab
                        if (ImGui.MenuItem("Manage Layouts..."))
                        {
                            try
                            {
                                OnManageLayouts?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                LogService.Error(LogCategory.UI, "Failed to open layouts manager", ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(LogCategory.UI, "Error in context menu", ex);
                    }

                    ImGui.EndPopup();
                }
                
                // Layout modals (edit mode only)
                if (editMode)
                {
                    // Save layout modal - open popup if flag is set but popup is not yet open
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
                                    try
                                    {
                                        OnSaveLayout?.Invoke(_layoutNameBuffer, ExportLayout());
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Error(LogCategory.UI, $"Failed to save layout '{_layoutNameBuffer}'", ex);
                                    }
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

                    // New layout modal - open popup if flag is set but popup is not yet open
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
                                    try
                                    {
                                        // Create an empty layout for the new name
                                        OnSaveLayout?.Invoke(_newLayoutNameBuffer, new List<ToolLayoutState>());
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Error(LogCategory.UI, $"Failed to create layout '{_newLayoutNameBuffer}'", ex);
                                    }
                                    try
                                    {
                                        // Switch to the newly created layout
                                        OnLoadLayout?.Invoke(_newLayoutNameBuffer);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Error(LogCategory.UI, $"Failed to load new layout '{_newLayoutNameBuffer}'", ex);
                                    }
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
                    
                    // Grid resolution modal
                    DrawGridResolutionModal(availRegion, cellW, cellH);
                }
            
            // Unsaved changes dialog - drawn outside edit mode so it can be shown anytime
            DrawUnsavedChangesDialog();

            for (var i = 0; i < _tools.Count; i++)
                {
                    var te = _tools[i];
                    var t = te.Tool;
                    if (!t.Visible) continue;
                // Provide absolute screen coords for the tool
                ImGui.SetCursorScreenPos(t.Position + contentOrigin);
                var id = $"tool_{i}_{t.Id}";

                ImGui.PushID(id);
                // If the tool has a background enabled, draw a filled rectangle behind
                // the child at the tool's absolute screen position. Drawing it before
                // the child ensures it remains behind the tool's UI.
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
                
                // Calculate internal padding in pixels (replaces default window padding)
                // Check for external source first (allows real-time config window updates)
                var externalPadding = GetExternalToolInternalPadding?.Invoke() ?? -1;
                if (externalPadding >= 0)
                {
                    // Sync external padding to local settings so it persists correctly on layout changes
                    _currentGridSettings.ToolInternalPaddingPx = externalPadding;
                }
                var internalPaddingPx = _currentGridSettings.ToolInternalPaddingPx;
                internalPaddingPx = Math.Max(0, internalPaddingPx);
                var internalPadding = (float)internalPaddingPx;
                
                // Push custom window padding (replaces default, not additive)
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(internalPadding, internalPadding));
                ImGui.BeginChild(id, t.Size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                ImGui.PopStyleVar();
                
                // Begin a group to contain the content
                var contentWidth = t.Size.X - internalPadding * 2;
                ImGui.BeginGroup();
                ImGui.PushItemWidth(MathF.Max(50f, contentWidth));
                
                // Title bar inside the child (toggleable)
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
                
                // Capture focus state before ending child - must be called inside BeginChild/EndChild block
                var isChildFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows);
                ImGui.EndChild();

                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();

                if (editMode)
                {
                    // draw border (if enabled)
                    if (t.OutlineEnabled)
                        dl.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border));

                    // Check if main window is being interacted with - if so, block new tool interactions
                    var mainWindowInteracting = false;
                    try { mainWindowInteracting = IsMainWindowInteracting?.Invoke() ?? false; }
                    catch (Exception ex) { LogService.Debug(LogCategory.UI, $"[WindowContentContainer] IsMainWindowInteracting callback error: {ex.Message}"); }

                    // Check if any OTHER tool is already being dragged or resized - if so, block new interactions
                    var anotherToolInteracting = false;
                    for (var otherIdx = 0; otherIdx < _tools.Count; otherIdx++)
                    {
                        if (otherIdx != i && (_tools[otherIdx].Dragging || _tools[otherIdx].Resizing))
                        {
                            anotherToolInteracting = true;
                            break;
                        }
                    }
                    
                    // Only allow starting new drag/resize if this tool's window is focused
                    var canInteract = isChildFocused || te.Dragging || te.Resizing;

                    // Define interaction regions
                    var io = ImGui.GetIO();
                    var mouse = io.MousePos;
                    var titleHeight = MathF.Min(24f, t.Size.Y);
                    var titleMin = min;
                    var titleMax = new Vector2(max.X, min.Y + titleHeight);

                    // Resize handle (bottom-right): detect mouse in corner region and drag to resize
                    var handleSize = 12f;
                    var handleMin = new Vector2(max.X - handleSize, max.Y - handleSize);
                    var isMouseOverHandle = mouse.X >= handleMin.X && mouse.X <= max.X && mouse.Y >= handleMin.Y && mouse.Y <= max.Y;

                    // Dragging via mouse drag when hovering the child (title area)
                    // Prioritize resize over drag - don't allow drag start if mouse is over resize handle
                    var isMouseOverTitle = mouse.X >= titleMin.X && mouse.X <= titleMax.X && mouse.Y >= titleMin.Y && mouse.Y <= titleMax.Y;
                    var canStartDrag = isMouseOverTitle && !isMouseOverHandle && canInteract;

                    // Start drag only when clicking the title (not resize handle), but continue dragging while mouse is down
                    // Block starting new drags if main window is being moved/resized, another tool is being interacted with, or this tool is not focused
                    // IMPORTANT: Only start dragging if the click originated in the title area (IsMouseClicked), 
                    // but continue dragging while mouse is held (MouseDown) once started
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
                        // Use mouse-start based delta and clamp large jumps to avoid UI lockups
                        var rawDelta = io.MousePos - te.DragMouseStart;
                        const float MaxDelta = ConfigStatic.MaxDragDelta;
                        rawDelta.X = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.X));
                        rawDelta.Y = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.Y));
                        var newPos = te.OrigPos + rawDelta;

                        // Clamp position to content bounds so it can't become huge
                        var minX = contentMin.X - contentOrigin.X; // relative pos space (usually 0)
                        var minY = contentMin.Y - contentOrigin.Y;
                        var maxX = (contentMax.X - contentOrigin.X) - t.Size.X;
                        var maxY = (contentMax.Y - contentOrigin.Y) - t.Size.Y;
                        newPos.X = MathF.Max(minX, MathF.Min(maxX, newPos.X));
                        newPos.Y = MathF.Max(minY, MathF.Min(maxY, newPos.Y));

                        // During drag: follow mouse freely (no snapping). Snap on release.
                        t.Position = newPos;
                    }
                    else if (!io.MouseDown[0])
                    {
                        // mouse released: if we were dragging, snap to sub-grid now
                        if (te.Dragging)
                        {
                            try
                            {
                                var subdivisions = Math.Max(1, _currentGridSettings.Subdivisions);
                                var subW = cellW / subdivisions;
                                var subH = cellH / subdivisions;
                                var snapped = t.Position;
                                snapped.X = MathF.Round(snapped.X / subW) * subW;
                                snapped.Y = MathF.Round(snapped.Y / subH) * subH;
                                // Clamp again after snapping
                                var minX2 = contentMin.X - contentOrigin.X;
                                var minY2 = contentMin.Y - contentOrigin.Y;
                                var maxX2 = (contentMax.X - contentOrigin.X) - t.Size.X;
                                var maxY2 = (contentMax.Y - contentOrigin.Y) - t.Size.Y;
                                snapped.X = MathF.Max(minX2, MathF.Min(maxX2, snapped.X));
                                snapped.Y = MathF.Max(minY2, MathF.Min(maxY2, snapped.Y));
                                t.Position = snapped;
                                
                                // Update grid coordinates
                                if (cellW > 0 && cellH > 0)
                                {
                                    t.GridCol = t.Position.X / cellW;
                                    t.GridRow = t.Position.Y / cellH;
                                    t.HasGridCoords = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.Debug(LogCategory.UI, $"Drag snap error: {ex.Message}");
                            }
                            // mark layout changed on drag end so host can persist
                            MarkLayoutDirty();
                        }

                        te.Dragging = false;
                    }

                    // Only allow starting resize if the tool is focused
                    var canStartResize = isMouseOverHandle && canInteract;

                    // Start resize when clicking the handle, but continue resizing while mouse is down
                    // Block starting new resizes if main window is being moved/resized, another tool is being interacted with, or this tool is not focused
                    // IMPORTANT: Only start resizing if the click originated in the resize handle (IsMouseClicked),
                    // but continue resizing while mouse is held (MouseDown) once started
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
                        // Use mouse-start based delta and clamp large jumps
                        var rawDelta = io.MousePos - te.ResizeMouseStart;
                        const float MaxDelta = ConfigStatic.MaxDragDelta;
                        rawDelta.X = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.X));
                        rawDelta.Y = MathF.Max(-MaxDelta, MathF.Min(MaxDelta, rawDelta.Y));
                        var newSize = new Vector2(MathF.Max(MinToolWidth, te.OrigSize.X + rawDelta.X), MathF.Max(MinToolHeight, te.OrigSize.Y + rawDelta.Y));
                        // Clamp size so it doesn't exceed content while dragging
                        var maxW = (contentMax.X - contentOrigin.X) - t.Position.X;
                        var maxH = (contentMax.Y - contentOrigin.Y) - t.Position.Y;
                        newSize.X = MathF.Min(newSize.X, MathF.Max(MinToolWidth, maxW));
                        newSize.Y = MathF.Min(newSize.Y, MathF.Max(MinToolHeight, maxH));
                        // During resize: follow mouse freely (no snapping). Snap on release.
                        t.Size = newSize;
                    }
                    else if (!io.MouseDown[0])
                    {
                        // mouse released: if we were resizing, snap size to sub-grid now
                        if (te.Resizing)
                        {
                            try
                            {
                                var subdivisions = Math.Max(1, _currentGridSettings.Subdivisions);
                                var subW = cellW / subdivisions;
                                var subH = cellH / subdivisions;
                                var snappedSize = t.Size;
                                snappedSize.X = MathF.Max(MinToolWidth, MathF.Round(snappedSize.X / subW) * subW);
                                snappedSize.Y = MathF.Max(MinToolHeight, MathF.Round(snappedSize.Y / subH) * subH);
                                // Clamp so size doesn't exceed content after snapping
                                var maxW2 = (contentMax.X - contentOrigin.X) - t.Position.X;
                                var maxH2 = (contentMax.Y - contentOrigin.Y) - t.Position.Y;
                                snappedSize.X = MathF.Min(snappedSize.X, MathF.Max(MinToolWidth, maxW2));
                                snappedSize.Y = MathF.Min(snappedSize.Y, MathF.Max(MinToolHeight, maxH2));
                                t.Size = snappedSize;
                                
                                // Update grid coordinates
                                if (cellW > 0 && cellH > 0)
                                {
                                    t.GridColSpan = t.Size.X / cellW;
                                    t.GridRowSpan = t.Size.Y / cellH;
                                    t.HasGridCoords = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.Debug(LogCategory.UI, $"Resize snap error: {ex.Message}");
                            }
                            // mark layout changed on resize end so host can persist
                            MarkLayoutDirty();
                        }

                        te.Resizing = false;
                    }
                }

                ImGui.PopID();
            }

            // Open any pending popup after tools have been rendered
            if (_pendingPopup != null)
            {
                ImGui.SetNextWindowPos(_pendingPopupPos);
                ImGui.OpenPopup(_pendingPopup);
                _pendingPopup = null;
            }

            // Update global interaction state by checking all tools
            var anyToolDragging = false;
            var anyToolResizing = false;
            foreach (var te in _tools)
            {
                if (te.Dragging) anyToolDragging = true;
                if (te.Resizing) anyToolResizing = true;
            }
            SetDraggingState(anyToolDragging);
            SetResizingState(anyToolResizing);

            // Tool-specific context popup (opened when right-clicking a tool in edit mode)
            if (ImGui.BeginPopup("tool_context_menu"))
            {
                try
                {
                    // If the stored index is invalid (e.g., layout changed between
                    // the right-click and popup render), try to resolve the tool by
                    // the last recorded click position.
                    if (!(_contextToolIndex >= 0 && _contextToolIndex < _tools.Count))
                    {
                        try
                        {
                            var wp = ImGui.GetWindowPos();
                            var co = wp + ImGui.GetWindowContentRegionMin();
                            var absClick = co + _lastContextClickRel;
                            var found = -1;
                            for (var ti = 0; ti < _tools.Count; ti++)
                            {
                                var tt = _tools[ti].Tool;
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

                    if (_contextToolIndex >= 0 && _contextToolIndex < _tools.Count)
                    {
                        var t = _tools[_contextToolIndex].Tool;
                        ImGui.TextUnformatted(t.DisplayTitle ?? "Tool");
                        ImGui.Separator();
                        
                        // Tool-specific context menu options (shown first)
                        var customOptions = t.GetContextMenuOptions();
                        if (customOptions != null && customOptions.Count > 0)
                        {
                            foreach (var option in customOptions)
                            {
                                if (option.SeparatorBefore)
                                    ImGui.Separator();
                                
                                // Build the label with optional icon
                                var label = option.Icon != null ? $"{option.Icon} {option.Label}" : option.Label;
                                
                                // Handle checked items vs regular menu items
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
                                
                                // Show tooltip if available
                                if (option.Tooltip != null && ImGui.IsItemHovered())
                                {
                                    ImGui.SetTooltip(option.Tooltip);
                                }
                                
                                if (option.SeparatorAfter)
                                    ImGui.Separator();
                            }
                            ImGui.Separator();
                        }
                        
                        // Rename option
                        if (ImGui.MenuItem("Rename..."))
                        {
                            ImGui.CloseCurrentPopup();
                            _renameToolIndex = _contextToolIndex;
                            _renameBuffer = t.CustomTitle ?? t.Title ?? "";
                            _renamePopupOpen = true;
                        }
                        
                        // Duplicate option
                        if (ImGui.MenuItem("Duplicate"))
                        {
                            try
                            {
                                DuplicateTool(t);
                            }
                            catch (Exception ex)
                            {
                                LogService.Error(LogCategory.UI, "Failed to duplicate tool", ex);
                            }
                            ImGui.CloseCurrentPopup();
                        }
                        
                        ImGui.Separator();
                        var bg = t.BackgroundEnabled;
                        if (ImGui.Checkbox("Show background", ref bg)) t.BackgroundEnabled = bg;
                        var hdr = t.HeaderVisible;
                        if (ImGui.Checkbox("Show header", ref hdr)) t.HeaderVisible = hdr;
                        var outline = t.OutlineEnabled;
                        if (ImGui.Checkbox("Show outline", ref outline)) t.OutlineEnabled = outline;
                        
                        // Background color with right-click to reset
                        var defaultBgColor = new Vector4(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
                        var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithReset(
                            "Background color", t.BackgroundColor, defaultBgColor, "Background color");
                        if (colorChanged) t.BackgroundColor = newColor;
                        
                        ImGui.Separator();

                        // Tool-specific settings (if supported by the tool)
                        if (t.HasSettings && ImGui.MenuItem("Settings..."))
                        {
                            // Close context menu and open settings modal
                            ImGui.CloseCurrentPopup();
                            _settingsToolIndex = _contextToolIndex;
                            _settingsPopupOpen = true;
                        }

                        // Save as Preset option (for Data tools - check by type, not Id)
                        if (t is Tools.Data.DataTool && OnSavePreset != null)
                        {
                            if (ImGui.MenuItem($"Save {t.ToolName} Preset"))
                            {
                                ImGui.CloseCurrentPopup();
                                _savePresetToolIndex = _contextToolIndex;
                                _savePresetPopupOpen = true;
                                _savePresetName = string.Empty;
                                _savePresetDescription = string.Empty;
                            }
                        }

                        ImGui.Separator();;
                        if (ImGui.MenuItem("Remove component"))
                        {
                            try
                            {
                                _tools.RemoveAt(_contextToolIndex);
                                MarkLayoutDirty();
                            }
                            catch (Exception ex)
                            {
                                LogService.Error(LogCategory.UI, "Failed to remove component", ex);
                            }
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

            // Tool settings window (renders when a tool has opened its settings)
            DrawToolSettingsWindow();
            
            // Tool rename modal
            DrawToolRenameModal();
            
            // Save as Preset modal
            DrawSavePresetModal();
        }

}