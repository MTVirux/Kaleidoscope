using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Kaleidoscope;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow
{
    public class WindowContentContainer
    {
        private readonly Func<float> _getCellWidthPercent;
        private readonly Func<float> _getCellHeightPercent;
        private readonly Func<int> _getSubdivisions;
        private Action<string, Vector2>? _toolFactory;
        private Vector2 _lastContextClickRel;
        // Index of the tool that was right-clicked to open the tool-specific context menu
        private int _contextToolIndex = -1;
        // Index of the tool whose settings modal is currently open (-1 = none)
        private int _settingsToolIndex = -1;
        // Whether the settings modal is currently open (used as ref for ImGui modal)
        private bool _settingsPopupOpen = false;
        
        // Grid resolution modal state
        private bool _gridResolutionPopupOpen = false;
        private LayoutGridSettings _editingGridSettings = new LayoutGridSettings();
        private int _previousColumns = 0;
        private int _previousRows = 0;
        
        // Current layout grid settings
        private LayoutGridSettings _currentGridSettings = new LayoutGridSettings();
        
        // Last known content region size for detecting window resize
        private Vector2 _lastContentSize = Vector2.Zero;

        private class ToolRegistration
        {
            public string Id = string.Empty;
            public string Label = string.Empty;
            public string? Description;
            // Category path for nested menus, components separated by '>' (e.g. "Gil>Graph")
            public string? CategoryPath;
            public Func<Vector2, ToolComponent?> Factory = (_) => null;
        }

        private readonly List<ToolRegistration> _toolRegistry = new List<ToolRegistration>();
        private class ToolEntry
        {
            public ToolComponent Tool;
            public Vector2 OrigPos;
            public Vector2 OrigSize;
            public bool Dragging;
            public bool Resizing;
            public Vector2 DragMouseStart;
            public Vector2 ResizeMouseStart;
            public ToolEntry(ToolComponent t)
            {
                Tool = t;
                OrigPos = t.Position;
                OrigSize = t.Size;
            }
        }

        private class MenuNode
        {
            public Dictionary<string, MenuNode> Children = new Dictionary<string, MenuNode>();
            public List<ToolRegistration> Items = new List<ToolRegistration>();
        }

        private readonly List<ToolEntry> _tools = new List<ToolEntry>();
        
        // Layout callbacks (host can set these to persist/load named layouts)
        public Action<string, List<ToolLayoutState>>? OnSaveLayout;
        public Action<string>? OnLoadLayout;
        public Func<List<string>>? GetAvailableLayoutNames;
        private bool _saveLayoutPopupOpen = false;
        private string _layoutNameBuffer = string.Empty;
        private bool _layoutDirty = false;

        // Callback invoked when the layout changes. Host should persist the provided tool layout.
        public Action<List<ToolLayoutState>>? OnLayoutChanged;

        // Callback invoked to open the layouts management UI (config window layouts tab).
        public Action? OnManageLayouts;

        // Callbacks for interaction state changes (dragging/resizing)
        // Host can use these to update the StateService
        public Action<bool>? OnDraggingChanged;
        public Action<bool>? OnResizingChanged;

        // Callback to check if main window is currently being moved or resized
        // When true, tool interactions should be blocked to prevent accidental moves
        public Func<bool>? IsMainWindowInteracting;

        // Track global interaction state for this container
        private bool _anyDragging = false;
        private bool _anyResizing = false;

        /// <summary>
        /// Returns true if any tool is currently being dragged.
        /// </summary>
        public bool IsDragging => _anyDragging;

        /// <summary>
        /// Returns true if any tool is currently being resized.
        /// </summary>
        public bool IsResizing => _anyResizing;

        /// <summary>
        /// Returns true if any interaction (drag or resize) is in progress.
        /// </summary>
        public bool IsInteracting => _anyDragging || _anyResizing;

        // Update the global dragging state and notify if changed
        private void SetDraggingState(bool dragging)
        {
            if (_anyDragging == dragging) return;
            _anyDragging = dragging;
            try { OnDraggingChanged?.Invoke(dragging); }
            catch (Exception ex) { LogService.Debug($"OnDraggingChanged error: {ex.Message}"); }
        }

        // Update the global resizing state and notify if changed
        private void SetResizingState(bool resizing)
        {
            if (_anyResizing == resizing) return;
            _anyResizing = resizing;
            try { OnResizingChanged?.Invoke(resizing); }
            catch (Exception ex) { LogService.Debug($"OnResizingChanged error: {ex.Message}"); }
        }

        // Mark the layout as dirty (changed) so hosts can persist it.
        private void MarkLayoutDirty()
        {
            _layoutDirty = true;
            try
            {
                LogService.Debug($"Layout marked dirty ({_tools.Count} tools)");
                OnLayoutChanged?.Invoke(ExportLayout());
            }
            catch (Exception ex)
            {
                LogService.Error("Error while invoking OnLayoutChanged", ex);
            }
        }

        // Attempt to consume the dirty flag. Returns true if it was set.
        public bool TryConsumeLayoutDirty()
        {
            if (!_layoutDirty) return false;
            _layoutDirty = false;
            LogService.Debug("TryConsumeLayoutDirty: consumed dirty flag");
            return true;
        }

        public WindowContentContainer(Func<float>? getCellWidthPercent = null, Func<float>? getCellHeightPercent = null, Func<int>? getSubdivisions = null)
        {
            _getCellWidthPercent = getCellWidthPercent ?? (() => 25f);
            _getCellHeightPercent = getCellHeightPercent ?? (() => 25f);
            _getSubdivisions = getSubdivisions ?? (() => 4);
        }

        /// <summary>
        /// Gets the current grid settings for this layout.
        /// </summary>
        public LayoutGridSettings GridSettings => _currentGridSettings;

        /// <summary>
        /// Gets the effective number of columns for the current grid settings and content size.
        /// </summary>
        public int GetEffectiveColumns(Vector2 contentSize)
        {
            // Grid resolution remains fixed for the current layout.
            // If AutoAdjustResolution is enabled, treat the multiplier as a scale
            // of a 16:9 base grid (multiplier=2 => 32x18). These counts are
            // independent of the window pixel size and do not change on resize.
            if (_currentGridSettings.AutoAdjustResolution)
            {
                var multiplier = Math.Max(1, _currentGridSettings.GridResolutionMultiplier);
                return Math.Max(1, multiplier * 16);
            }

            return Math.Max(1, _currentGridSettings.Columns);
        }

        /// <summary>
        /// Gets the effective number of rows for the current grid settings and content size.
        /// </summary>
        public int GetEffectiveRows(Vector2 contentSize)
        {
            if (_currentGridSettings.AutoAdjustResolution)
            {
                var multiplier = Math.Max(1, _currentGridSettings.GridResolutionMultiplier);
                return Math.Max(1, multiplier * 9);
            }

            return Math.Max(1, _currentGridSettings.Rows);
        }

        private static int GCD(int a, int b)
        {
            while (b != 0)
            {
                var t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        /// <summary>
        /// Updates the grid settings and repositions tools to maintain their relative positions.
        /// </summary>
        public void UpdateGridSettings(LayoutGridSettings newSettings, Vector2 contentSize)
        {
            if (newSettings == null) return;
            
            var oldCols = GetEffectiveColumns(contentSize);
            var oldRows = GetEffectiveRows(contentSize);
            
            _currentGridSettings.CopyFrom(newSettings);
            
            var newCols = GetEffectiveColumns(contentSize);
            var newRows = GetEffectiveRows(contentSize);
            
            // Calculate new cell sizes
            var newCellW = contentSize.X / MathF.Max(1f, newCols);
            var newCellH = contentSize.Y / MathF.Max(1f, newRows);
            
            // Reposition tools to maintain relative positions
            if (oldCols > 0 && oldRows > 0 && newCols > 0 && newRows > 0 && (oldCols != newCols || oldRows != newRows))
            {
                var colScale = (float)newCols / oldCols;
                var rowScale = (float)newRows / oldRows;
                
                foreach (var te in _tools)
                {
                    var t = te.Tool;
                    // Scale grid coordinates to maintain relative position
                    t.GridCol *= colScale;
                    t.GridRow *= rowScale;
                    t.GridColSpan *= colScale;
                    t.GridRowSpan *= rowScale;
                    
                    // Update pixel positions immediately
                    t.Position = new Vector2(t.GridCol * newCellW, t.GridRow * newCellH);
                    t.Size = new Vector2(
                        MathF.Max(50f, t.GridColSpan * newCellW),
                        MathF.Max(50f, t.GridRowSpan * newCellH)
                    );
                }
                
                MarkLayoutDirty();
            }
        }

        /// <summary>
        /// Sets the grid settings from a layout state without repositioning tools.
        /// </summary>
        public void SetGridSettingsFromLayout(ContentLayoutState? layout)
        {
            if (layout == null) return;
            _currentGridSettings = LayoutGridSettings.FromLayoutState(layout);
        }

        /// <summary>
        /// Callback invoked when grid settings change. Host should persist the settings.
        /// </summary>
        public Action<LayoutGridSettings>? OnGridSettingsChanged;

        // Allows the host (e.g. MainWindow) to supply a factory to create tools
        public void SetToolFactory(Action<string, Vector2> factory)
        {
            _toolFactory = factory;
        }

        // Register a tool for the "Add tool" menu. The factory receives the click-relative
        // position and should return a configured ToolComponent (position may be adjusted by
        // the container snapping logic afterwards). Factory may return null if tool creation fails.
        public void RegisterTool(string id, string label, Func<Vector2, ToolComponent?> factory, string? description = null, string? categoryPath = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id");
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _toolRegistry.Add(new ToolRegistration { Id = id, Label = label ?? id, Description = description, Factory = factory, CategoryPath = categoryPath });
        }

        public void UnregisterTool(string id)
        {
            var idx = _toolRegistry.FindIndex(x => x.Id == id);
            if (idx >= 0) _toolRegistry.RemoveAt(idx);
        }

        public void AddTool(ToolComponent tool)
        {
            _tools.Add(new ToolEntry(tool));
            LogService.Debug($"AddTool: added tool '{tool?.Title ?? tool?.Id ?? "<unknown>"}' total={_tools.Count}");
            MarkLayoutDirty();
        }

        public List<ToolLayoutState> ExportLayout()
        {
            var ret = new List<ToolLayoutState>();
            foreach (var te in _tools)
            {
                var t = te.Tool;
                ret.Add(new ToolLayoutState
                {
                    Id = t.Id,
                    Type = t.GetType().FullName ?? t.GetType().Name,
                    Title = t.Title,
                    Position = t.Position,
                    Size = t.Size,
                    Visible = t.Visible,
                    BackgroundEnabled = t is { } ? t.BackgroundEnabled : false,
                    BackgroundColor = t is { } ? t.BackgroundColor : new System.Numerics.Vector4(0f, 0f, 0f, 0.5f),
                    HeaderVisible = t is { } ? t.HeaderVisible : true,
                    // Include grid coordinates
                    GridCol = t.GridCol,
                    GridRow = t.GridRow,
                    GridColSpan = t.GridColSpan,
                    GridRowSpan = t.GridRowSpan,
                    HasGridCoords = t.HasGridCoords,
                });
            }
            LogService.Debug($"ExportLayout: exported {ret.Count} tools");
            return ret;
        }

        public void ApplyLayout(List<ToolLayoutState>? layout)
        {
            if (layout == null) return;
            LogService.Debug($"ApplyLayout: applying {layout.Count} entries to {_tools.Count} existing tools");
            if (_toolRegistry.Count > 0)
            {
                LogService.Debug($"ApplyLayout: registered tool factories ({_toolRegistry.Count})");
            }
            var matchedIndices = new System.Collections.Generic.HashSet<int>();
            for (var li = 0; li < layout.Count; li++)
            {
                var entry = layout[li];
                try
                {
                    // Try to match by Id first, then by Title, then by Type.
                    // Only consider existing tools that have not already been matched to another layout entry.
                    ToolComponent? match = null;
                    var matchIdx = -1;
                    for (var i = 0; i < _tools.Count; i++)
                    {
                        if (matchedIndices.Contains(i)) continue;
                        if (_tools[i].Tool.Id == entry.Id) { match = _tools[i].Tool; matchIdx = i; break; }
                    }
                    if (match == null)
                    {
                        for (var i = 0; i < _tools.Count; i++)
                        {
                            if (matchedIndices.Contains(i)) continue;
                            if (_tools[i].Tool.Title == entry.Title) { match = _tools[i].Tool; matchIdx = i; break; }
                        }
                    }
                    if (match == null)
                    {
                        for (var i = 0; i < _tools.Count; i++)
                        {
                            if (matchedIndices.Contains(i)) continue;
                            if (_tools[i].Tool.GetType().FullName == entry.Type) { match = _tools[i].Tool; matchIdx = i; break; }
                        }
                    }

                    if (match != null)
                    {
                        match.Position = entry.Position;
                        match.Size = entry.Size;
                        match.Visible = entry.Visible;
                        match.BackgroundEnabled = entry.BackgroundEnabled;
                        match.HeaderVisible = entry.HeaderVisible;
                        // Apply grid coordinates
                        match.GridCol = entry.GridCol;
                        match.GridRow = entry.GridRow;
                        match.GridColSpan = entry.GridColSpan;
                        match.GridRowSpan = entry.GridRowSpan;
                        match.HasGridCoords = entry.HasGridCoords;
                        if (matchIdx >= 0) matchedIndices.Add(matchIdx);
                        LogService.Debug($"ApplyLayout: matched existing tool for entry '{entry.Id}' (type={entry.Type}, title={entry.Title})");
                        continue;
                    }

                    // No existing tool matched — attempt to create a new instance from the registered tool factories.
                    // First, try to find a registration by factory id (common case when Id contains a factory name).
                    var createdAny = false;
                    var reg = _toolRegistry.Find(r => string.Equals(r.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                    if (reg != null && reg.Factory != null)
                    {
                        LogService.Debug($"ApplyLayout: attempting registry factory by id='{reg.Id}' for entry '{entry.Id}'");
                        try
                        {
                            var created = reg.Factory(entry.Position);
                            if (created != null)
                            {
                                created.Position = entry.Position;
                                created.Size = entry.Size;
                                created.Visible = entry.Visible;
                                created.BackgroundEnabled = entry.BackgroundEnabled;
                                created.HeaderVisible = entry.HeaderVisible;
                                created.BackgroundColor = entry.BackgroundColor;
                                // Apply grid coordinates
                                created.GridCol = entry.GridCol;
                                created.GridRow = entry.GridRow;
                                created.GridColSpan = entry.GridColSpan;
                                created.GridRowSpan = entry.GridRowSpan;
                                created.HasGridCoords = entry.HasGridCoords;
                                if (!string.IsNullOrWhiteSpace(entry.Title)) created.Title = entry.Title;
                                AddTool(created);
                                // Mark newly added tool as matched so it won't be reused for another entry
                                matchedIndices.Add(_tools.Count - 1);
                                LogService.Debug($"ApplyLayout: created tool via registry id='{reg.Id}' for entry '{entry.Id}' (type={entry.Type})");
                                createdAny = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug($"ApplyLayout: registry factory '{reg.Id}' threw: {ex.Message}");
                        }
                    }

                    if (!createdAny)
                    {
                        // If not found by id, try each registered factory and match by resulting type FullName.
                        foreach (var candReg in _toolRegistry)
                        {
                            try
                            {
                                var cand = candReg.Factory(entry.Position);
                                if (cand == null) continue;
                                if (cand.GetType().FullName == entry.Type)
                                {
                                    cand.Position = entry.Position;
                                    cand.Size = entry.Size;
                                    cand.Visible = entry.Visible;
                                    cand.BackgroundEnabled = entry.BackgroundEnabled;
                                    cand.HeaderVisible = entry.HeaderVisible;
                                    cand.BackgroundColor = entry.BackgroundColor;
                                    // Apply grid coordinates
                                    cand.GridCol = entry.GridCol;
                                    cand.GridRow = entry.GridRow;
                                    cand.GridColSpan = entry.GridColSpan;
                                    cand.GridRowSpan = entry.GridRowSpan;
                                    cand.HasGridCoords = entry.HasGridCoords;
                                    if (!string.IsNullOrWhiteSpace(entry.Title)) cand.Title = entry.Title;
                                    AddTool(cand);
                                    // Mark newly added tool as matched so it won't be reused for another entry
                                    matchedIndices.Add(_tools.Count - 1);
                                    LogService.Debug($"ApplyLayout: created tool via factory '{candReg.Id}' matched by type for entry '{entry.Id}'");
                                    createdAny = true;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.Debug($"Factory invocation failed for registry entry '{candReg.Id}': {ex.Message}");
                            }
                        }
                    }

                    if (createdAny) continue;

                    // If no registry factories matched, try reflection-based creation by type name
                    if (!createdAny && !string.IsNullOrWhiteSpace(entry.Type))
                    {
                        try
                        {
                            Type? found = null;
                            try
                            {
                                found = Type.GetType(entry.Type);
                            }
                            catch { found = null; }
                            if (found == null)
                            {
                                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                                {
                                    try
                                    {
                                        var t = asm.GetType(entry.Type);
                                        if (t != null) { found = t; break; }
                                    }
                                    catch { }
                                }
                            }

                            if (found != null && typeof(ToolComponent).IsAssignableFrom(found))
                            {
                                try
                                {
                                    var inst = Activator.CreateInstance(found) as ToolComponent;
                                    if (inst != null)
                                    {
                                        inst.Position = entry.Position;
                                        inst.Size = entry.Size;
                                        inst.Visible = entry.Visible;
                                        inst.BackgroundEnabled = entry.BackgroundEnabled;
                                        inst.HeaderVisible = entry.HeaderVisible;
                                        inst.BackgroundColor = entry.BackgroundColor;
                                        // Apply grid coordinates
                                        inst.GridCol = entry.GridCol;
                                        inst.GridRow = entry.GridRow;
                                        inst.GridColSpan = entry.GridColSpan;
                                        inst.GridRowSpan = entry.GridRowSpan;
                                        inst.HasGridCoords = entry.HasGridCoords;
                                        if (!string.IsNullOrWhiteSpace(entry.Title)) inst.Title = entry.Title;
                                        AddTool(inst);
                                        // Mark newly added tool as matched so it won't be reused for another entry
                                        matchedIndices.Add(_tools.Count - 1);
                                        LogService.Debug($"ApplyLayout: created tool via reflection type='{entry.Type}' for entry '{entry.Id}'");
                                        createdAny = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogService.Debug($"Reflection creation failed for type '{entry.Type}': {ex.Message}");
                                }
                            }
                            else
                            {
                                LogService.Debug($"ApplyLayout: reflection could not find type '{entry.Type}'");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug($"ApplyLayout: reflection attempt failed for '{entry.Type}': {ex.Message}");
                        }
                    }

                    if (!createdAny)
                    {
                        LogService.Debug($"ApplyLayout: no existing tool matched and creation failed for '{entry.Id}' / '{entry.Type}'");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"Failed to apply layout entry '{entry.Id}'", ex);
                }
            }
        }

        public void Draw(bool editMode)
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
                                MathF.Max(50f, t.GridColSpan * cellW),
                                MathF.Max(50f, t.GridRowSpan * cellH)
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Debug($"Window resize tool update error: {ex.Message}");
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
                    LogService.Debug($"Grid drawing error: {ex.Message}");
                }

                // Right-click on the content region should open a context menu to add tools
                if (editMode)
                {
                    var io = ImGui.GetIO();
                    var mouse = io.MousePos;
                    var isOverContent = mouse.X >= contentMin.X && mouse.X <= contentMax.X && mouse.Y >= contentMin.Y && mouse.Y <= contentMax.Y;
                    if (isOverContent && io.MouseClicked[1])
                    {
                        // If the click is over an existing tool, open the tool-specific popup
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
                            LogService.Debug($"Tool click detection error: {ex.Message}");
                        }

                        if (clickedTool >= 0)
                        {
                            _contextToolIndex = clickedTool;
                            _lastContextClickRel = mouse - contentOrigin;
                            ImGui.SetNextWindowPos(mouse);
                            ImGui.OpenPopup("tool_context_menu");
                        }
                        else
                        {
                            _lastContextClickRel = mouse - contentOrigin;
                            ImGui.SetNextWindowPos(mouse);
                            ImGui.OpenPopup("content_context_menu");
                        }
                    }

                    if (ImGui.BeginPopup("content_context_menu"))
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
                                                        LogService.Debug($"Tool snap error: {ex.Message}");
                                                    }
                                                    AddTool(tool);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                LogService.Error($"Failed to create tool '{reg.Id}'", ex);
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
                            // Save / Load layouts
                            if (ImGui.MenuItem("Save layout..."))
                            {
                                _layoutNameBuffer = "";
                                _saveLayoutPopupOpen = true;
                                ImGui.OpenPopup("save_layout_popup");
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
                                                LogService.Error($"Failed to load layout '{n}'", ex);
                                            }
                                            ImGui.CloseCurrentPopup();
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogService.Error("Failed to get layout names", ex);
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
                                    LogService.Error("Failed to open layouts manager", ex);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Error("Error in context menu", ex);
                        }

                        ImGui.EndPopup();
                    }
                    // Save layout modal
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
                                        LogService.Error($"Failed to save layout '{_layoutNameBuffer}'", ex);
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
                            LogService.Error("Error in save layout popup", ex);
                        }
                        ImGui.EndPopup();
                    }
                    
                    // Grid resolution modal
                    DrawGridResolutionModal(availRegion, cellW, cellH);
                }
            }

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
                    LogService.Debug($"Background draw error: {ex.Message}");
                }
                ImGui.BeginChild(id, t.Size, true);
                // Title bar inside the child (toggleable)
                if (t.HeaderVisible)
                {
                    ImGui.TextUnformatted(t.Title);
                    ImGui.Separator();
                }
                t.DrawContent();
                ImGui.EndChild();

                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();

                if (editMode)
                {
                    // draw border
                    dl.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border));

                    // Check if main window is being interacted with - if so, block new tool interactions
                    var mainWindowInteracting = false;
                    try { mainWindowInteracting = IsMainWindowInteracting?.Invoke() ?? false; }
                    catch { /* ignore callback errors */ }

                    // Dragging via mouse drag when hovering the child (title area)
                    var io = ImGui.GetIO();
                    var mouse = io.MousePos;
                    var titleHeight = MathF.Min(24f, t.Size.Y);
                    var titleMin = min;
                    var titleMax = new Vector2(max.X, min.Y + titleHeight);
                    var isMouseOverTitle = mouse.X >= titleMin.X && mouse.X <= titleMax.X && mouse.Y >= titleMin.Y && mouse.Y <= titleMax.Y;

                    // Start drag only when clicking the title, but continue dragging while mouse is down
                    // Block starting new drags if main window is being moved/resized
                    if ((isMouseOverTitle || te.Dragging) && io.MouseDown[0] && (!mainWindowInteracting || te.Dragging))
                    {
                        if (!te.Dragging && !mainWindowInteracting)
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
                                LogService.Debug($"Drag snap error: {ex.Message}");
                            }
                            // mark layout changed on drag end so host can persist
                            MarkLayoutDirty();
                        }

                        te.Dragging = false;
                    }

                    // Resize handle (bottom-right): detect mouse in corner region and drag to resize
                    var handleSize = 12f;
                    var handleMin = new Vector2(max.X - handleSize, max.Y - handleSize);
                    var isMouseOverHandle = mouse.X >= handleMin.X && mouse.X <= max.X && mouse.Y >= handleMin.Y && mouse.Y <= max.Y;

                    // Start resize when clicking the handle, but continue resizing while mouse is down
                    // Block starting new resizes if main window is being moved/resized
                    if ((isMouseOverHandle || te.Resizing) && io.MouseDown[0] && (!mainWindowInteracting || te.Resizing))
                    {
                        if (!te.Resizing && !mainWindowInteracting)
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
                        var newSize = new Vector2(MathF.Max(50f, te.OrigSize.X + rawDelta.X), MathF.Max(50f, te.OrigSize.Y + rawDelta.Y));
                        // Clamp size so it doesn't exceed content while dragging
                        var maxW = (contentMax.X - contentOrigin.X) - t.Position.X;
                        var maxH = (contentMax.Y - contentOrigin.Y) - t.Position.Y;
                        newSize.X = MathF.Min(newSize.X, MathF.Max(50f, maxW));
                        newSize.Y = MathF.Min(newSize.Y, MathF.Max(50f, maxH));
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
                                snappedSize.X = MathF.Max(50f, MathF.Round(snappedSize.X / subW) * subW);
                                snappedSize.Y = MathF.Max(50f, MathF.Round(snappedSize.Y / subH) * subH);
                                // Clamp so size doesn't exceed content after snapping
                                var maxW2 = (contentMax.X - contentOrigin.X) - t.Position.X;
                                var maxH2 = (contentMax.Y - contentOrigin.Y) - t.Position.Y;
                                snappedSize.X = MathF.Min(snappedSize.X, MathF.Max(50f, maxW2));
                                snappedSize.Y = MathF.Min(snappedSize.Y, MathF.Max(50f, maxH2));
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
                                LogService.Debug($"Resize snap error: {ex.Message}");
                            }
                            // mark layout changed on resize end so host can persist
                            MarkLayoutDirty();
                        }

                        te.Resizing = false;
                    }
                }

                ImGui.PopID();
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
                            LogService.Debug($"Tool context find error: {ex.Message}");
                        }
                    }

                    if (_contextToolIndex >= 0 && _contextToolIndex < _tools.Count)
                    {
                        var t = _tools[_contextToolIndex].Tool;
                        ImGui.TextUnformatted(t.Title ?? "Tool");
                        ImGui.Separator();
                        var bg = t.BackgroundEnabled;
                        if (ImGui.Checkbox("Show background", ref bg)) t.BackgroundEnabled = bg;
                        var hdr = t.HeaderVisible;
                        if (ImGui.Checkbox("Show header", ref hdr)) t.HeaderVisible = hdr;
                        var col = t.BackgroundColor;
                        if (ImGui.ColorEdit4("Background color", ref col, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs)) t.BackgroundColor = col;
                        ImGui.Separator();

                        // Tool-specific settings (if supported by the tool)
                        if (t.HasSettings && ImGui.MenuItem("Settings..."))
                        {
                            // Close context menu and open settings modal
                            ImGui.CloseCurrentPopup();
                            _settingsToolIndex = _contextToolIndex;
                            _settingsPopupOpen = true;
                        }

                        ImGui.Separator();
                        if (ImGui.MenuItem("Remove component"))
                        {
                            try
                            {
                                _tools.RemoveAt(_contextToolIndex);
                                MarkLayoutDirty();
                            }
                            catch (Exception ex)
                            {
                                LogService.Error("Failed to remove component", ex);
                            }
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.Separator();
                        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error("Error in tool context menu", ex);
                }
                ImGui.EndPopup();
                _contextToolIndex = -1;
            }

            // Tool settings modal (renders when a tool has opened its settings)
            DrawToolSettingsModal();
        }

        /// <summary>
        /// Draws the tool settings modal if one is currently open.
        /// </summary>
        private void DrawToolSettingsModal()
        {
        if (_settingsToolIndex < 0 || _settingsToolIndex >= _tools.Count)
            return;

        const string popupName = "tool_settings_popup";
        var toolForSettings = _tools[_settingsToolIndex].Tool;

        // The popup must be opened each frame until it appears
        if (_settingsPopupOpen && !ImGui.IsPopupOpen(popupName))
        {
            ImGui.OpenPopup(popupName);
        }

        if (!ImGui.BeginPopupModal(popupName, ref _settingsPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            // Modal not showing - if user closed it, reset state
            if (!_settingsPopupOpen)
            {
                _settingsToolIndex = -1;
            }
            return;
        }

        try
        {
            ImGui.TextUnformatted(toolForSettings.Title ?? "Tool Settings");
            ImGui.Separator();

            try
            {
                toolForSettings.DrawSettings();
            }
            catch (Exception ex)
            {
                LogService.Error("Error while drawing tool settings", ex);
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "Error rendering settings");
            }

            ImGui.Separator();
            if (ImGui.Button("OK", new Vector2(80, 0)))
            {
                ImGui.CloseCurrentPopup();
                _settingsPopupOpen = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(80, 0)))
            {
                ImGui.CloseCurrentPopup();
                _settingsPopupOpen = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Error in tool settings modal", ex);
        }

        ImGui.EndPopup();

        if (!_settingsPopupOpen)
        {
            _settingsToolIndex = -1;
        }
    }

    /// <summary>
    /// Draws the grid resolution editing modal.
    /// </summary>
    private void DrawGridResolutionModal(Vector2 contentSize, float cellW, float cellH)
    {
        const string popupName = "grid_resolution_popup";
        
        // Open the popup if flagged
        if (_gridResolutionPopupOpen && !ImGui.IsPopupOpen(popupName))
        {
            ImGui.OpenPopup(popupName);
        }
        
        if (!ImGui.BeginPopupModal(popupName, ref _gridResolutionPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }
        
        try
        {
            ImGui.TextUnformatted("Edit Grid Resolution");
            ImGui.Separator();
            ImGui.Spacing();
            
            // Auto-adjust checkbox
            var autoAdjust = _editingGridSettings.AutoAdjustResolution;
            if (ImGui.Checkbox("Auto-adjust resolution", ref autoAdjust))
            {
                _editingGridSettings.AutoAdjustResolution = autoAdjust;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("When enabled, grid resolution is calculated from aspect ratio.\nColumns = AspectWidth × Multiplier\nRows = AspectHeight × Multiplier");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            if (_editingGridSettings.AutoAdjustResolution)
            {
                // Show only the resolution multiplier slider
                var multiplier = _editingGridSettings.GridResolutionMultiplier;
                ImGui.TextUnformatted("Grid Resolution Multiplier:");
                if (ImGui.SliderInt("##resolution", ref multiplier, 1, 10))
                {
                    _editingGridSettings.GridResolutionMultiplier = multiplier;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Higher values create a finer grid.\nFor 16:9 aspect ratio:\n  1 = 16×9 grid\n  2 = 32×18 grid\n  4 = 64×36 grid");
                }
                
                ImGui.Spacing();
                
                // Show preview of calculated values
                var previewCols = _editingGridSettings.GetEffectiveColumns(16f, 9f);
                var previewRows = _editingGridSettings.GetEffectiveRows(16f, 9f);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Preview (16:9): {previewCols} columns × {previewRows} rows");
            }
            else
            {
                // Show manual column/row inputs
                ImGui.TextUnformatted("Columns:");
                var cols = _editingGridSettings.Columns;
                if (ImGui.InputInt("##cols", ref cols))
                {
                    _editingGridSettings.Columns = Math.Max(1, Math.Min(100, cols));
                }
                
                ImGui.TextUnformatted("Rows:");
                var rows = _editingGridSettings.Rows;
                if (ImGui.InputInt("##rows", ref rows))
                {
                    _editingGridSettings.Rows = Math.Max(1, Math.Min(100, rows));
                }
                
                ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Grid: {_editingGridSettings.Columns} columns × {_editingGridSettings.Rows} rows");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // OK / Cancel buttons
            if (ImGui.Button("OK", new Vector2(80, 0)))
            {
                try
                {
                    // Apply the new settings and reposition tools
                    UpdateGridSettings(_editingGridSettings, contentSize);
                    
                    // Notify host to persist the settings
                    try { OnGridSettingsChanged?.Invoke(_currentGridSettings); }
                    catch (Exception ex) { LogService.Debug($"OnGridSettingsChanged error: {ex.Message}"); }
                }
                catch (Exception ex)
                {
                    LogService.Error("Error applying grid settings", ex);
                }
                
                ImGui.CloseCurrentPopup();
                _gridResolutionPopupOpen = false;
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel", new Vector2(80, 0)))
            {
                ImGui.CloseCurrentPopup();
                _gridResolutionPopupOpen = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Error in grid resolution modal", ex);
        }
        
        ImGui.EndPopup();
    }
    }
}
