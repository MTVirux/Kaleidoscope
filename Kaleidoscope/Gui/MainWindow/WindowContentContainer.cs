using System;
using System.Numerics;
using System.Collections.Generic;
using Kaleidoscope;
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

        private class ToolRegistration
        {
            public string Id = string.Empty;
            public string Label = string.Empty;
            public string? Description;
            public Func<Vector2, ToolComponent> Factory = (_) => throw new InvalidOperationException();
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

        private readonly List<ToolEntry> _tools = new List<ToolEntry>();
        
        // Layout callbacks (host can set these to persist/load named layouts)
        public Action<string, List<ToolLayoutState>>? OnSaveLayout;
        public Action<string>? OnLoadLayout;
        public Func<List<string>>? GetAvailableLayoutNames;
        private bool _saveLayoutPopupOpen = false;
        private bool _loadLayoutPopupOpen = false;
        private string _layoutNameBuffer = string.Empty;
        private bool _layoutDirty = false;

        // Callback invoked when the layout changes. Host should persist the provided tool layout.
        public Action<List<ToolLayoutState>>? OnLayoutChanged;

        // Mark the layout as dirty (changed) so hosts can persist it.
        private void MarkLayoutDirty()
        {
            _layoutDirty = true;
            try
            {
                OnLayoutChanged?.Invoke(ExportLayout());
            }
            catch { }
        }

        // Attempt to consume the dirty flag. Returns true if it was set.
        public bool TryConsumeLayoutDirty()
        {
            if (!_layoutDirty) return false;
            _layoutDirty = false;
            return true;
        }

        public WindowContentContainer(Func<float>? getCellWidthPercent = null, Func<float>? getCellHeightPercent = null, Func<int>? getSubdivisions = null)
        {
            _getCellWidthPercent = getCellWidthPercent ?? (() => 25f);
            _getCellHeightPercent = getCellHeightPercent ?? (() => 25f);
            _getSubdivisions = getSubdivisions ?? (() => 4);
        }

        // Allows the host (e.g. MainWindow) to supply a factory to create tools
        public void SetToolFactory(Action<string, Vector2> factory)
        {
            _toolFactory = factory;
        }

        // Register a tool for the "Add tool" menu. The factory receives the click-relative
        // position and should return a configured ToolComponent (position may be adjusted by
        // the container snapping logic afterwards).
        public void RegisterTool(string id, string label, Func<Vector2, ToolComponent> factory, string? description = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id");
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _toolRegistry.Add(new ToolRegistration { Id = id, Label = label ?? id, Description = description, Factory = factory });
        }

        public void UnregisterTool(string id)
        {
            var idx = _toolRegistry.FindIndex(x => x.Id == id);
            if (idx >= 0) _toolRegistry.RemoveAt(idx);
        }

        public void AddTool(ToolComponent tool)
        {
            _tools.Add(new ToolEntry(tool));
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
                });
            }
            return ret;
        }

        public void ApplyLayout(List<ToolLayoutState>? layout)
        {
            if (layout == null) return;
            foreach (var entry in layout)
            {
                // Try to match by Id first, then by Title, then by Type
                var match = _tools.Find(x => x.Tool.Id == entry.Id)?.Tool
                    ?? _tools.Find(x => x.Tool.Title == entry.Title)?.Tool
                    ?? _tools.Find(x => x.Tool.GetType().FullName == entry.Type)?.Tool;
                if (match != null)
                {
                    match.Position = entry.Position;
                    match.Size = entry.Size;
                    match.Visible = entry.Visible;
                    try { match.BackgroundEnabled = entry.BackgroundEnabled; match.BackgroundColor = entry.BackgroundColor; } catch { }
                }
            }
        }

        public void Draw(bool editMode)
        {
            var dl = ImGui.GetWindowDrawList();

            // Compute content origin and available region once
            var windowPos = ImGui.GetWindowPos();
            var contentOrigin = windowPos + new Vector2(0, ImGui.GetFrameHeight());
            var availRegion = ImGui.GetContentRegionAvail();
            var contentMin = contentOrigin;
            var contentMax = contentOrigin + availRegion;

            // Compute grid cell size once
            var cellW = MathF.Max(8f, availRegion.X * MathF.Max(0.01f, _getCellWidthPercent() / 100f));
            var cellH = MathF.Max(8f, availRegion.Y * MathF.Max(0.01f, _getCellHeightPercent() / 100f));

            // If in edit mode, draw a grid overlay to help alignment
            if (editMode)
            {
                try
                {
                    var subdivisions = Math.Max(1, _getSubdivisions());
                    // minor (subdivision) lines color (very faint)
                    var minorColor = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.03f));
                    // major (cell) lines color (slightly stronger)
                    var majorColor = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.08f));

                    var subW = cellW / subdivisions;
                    var subH = cellH / subdivisions;

                    // To avoid heavy rendering, cap the number of lines drawn
                    const int MaxLines = 512; // total per axis

                    // Vertical lines
                    var totalV = (int)MathF.Ceiling((contentMax.X - contentMin.X) / subW) + 1;
                    var vStep = 1;
                    if (totalV > MaxLines) vStep = (int)MathF.Ceiling((float)totalV / MaxLines);
                    var vx = contentMin.X;
                    var countV = 0;
                    for (var iV = 0; iV < totalV; iV++, vx += subW)
                    {
                        if (iV % vStep != 0) continue;
                        var isMajor = (iV % (subdivisions) == 0);
                        dl.AddLine(new Vector2(vx, contentMin.Y), new Vector2(vx, contentMax.Y), isMajor ? majorColor : minorColor, 1f);
                        countV++;
                    }

                    // Horizontal lines
                    var totalH = (int)MathF.Ceiling((contentMax.Y - contentMin.Y) / subH) + 1;
                    var hStep = 1;
                    if (totalH > MaxLines) hStep = (int)MathF.Ceiling((float)totalH / MaxLines);
                    var hy = contentMin.Y;
                    var countH = 0;
                    for (var iH = 0; iH < totalH; iH++, hy += subH)
                    {
                        if (iH % hStep != 0) continue;
                        var isMajor = (iH % (subdivisions) == 0);
                        dl.AddLine(new Vector2(contentMin.X, hy), new Vector2(contentMax.X, hy), isMajor ? majorColor : minorColor, 1f);
                        countH++;
                    }
                }
                catch { }

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
                        catch { }

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
                                // Enumerate registered tools
                                foreach (var reg in _toolRegistry)
                                {
                                    if (ImGui.MenuItem(reg.Label))
                                    {
                                        try
                                        {
                                            var tool = reg.Factory(_lastContextClickRel);
                                            if (tool != null)
                                            {
                                                // Snap to sub-grid on placement if possible
                                                try
                                                {
                                                    var subdivisions = Math.Max(1, _getSubdivisions());
                                                    var subW = cellW / subdivisions;
                                                    var subH = cellH / subdivisions;
                                                    tool.Position = new Vector2(
                                                        MathF.Round(tool.Position.X / subW) * subW,
                                                        MathF.Round(tool.Position.Y / subH) * subH
                                                    );
                                                }
                                                catch { }
                                                AddTool(tool);
                                            }
                                        }
                                        catch { }
                                    }
                                }

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
                                            try { OnLoadLayout?.Invoke(n); } catch { }
                                            ImGui.CloseCurrentPopup();
                                        }
                                    }
                                }
                                catch { }

                                ImGui.EndMenu();
                            }
                        }
                        catch { }

                        ImGui.EndPopup();
                    }
                    // Save layout modal
                    if (ImGui.BeginPopupModal("save_layout_popup", ref _saveLayoutPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
                    {
                        try
                        {
                            ImGui.TextUnformatted("Enter a name for this layout:");
                            ImGui.InputText("##layoutname", ref _layoutNameBuffer, 128);
                            if (ImGui.Button("Save"))
                            {
                                if (!string.IsNullOrWhiteSpace(_layoutNameBuffer))
                                {
                                    try { OnSaveLayout?.Invoke(_layoutNameBuffer, ExportLayout()); } catch { }
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
                        catch { }
                        ImGui.EndPopup();
                    }
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
                catch { }
                ImGui.BeginChild(id, t.Size, true);
                // Title bar inside the child
                ImGui.TextUnformatted(t.Title);
                ImGui.Separator();
                t.DrawContent();
                ImGui.EndChild();

                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();

                if (editMode)
                {
                    // draw border
                    dl.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border));

                    // Dragging via mouse drag when hovering the child (title area)
                    var io = ImGui.GetIO();
                    var mouse = io.MousePos;
                    var titleHeight = MathF.Min(24f, t.Size.Y);
                    var titleMin = min;
                    var titleMax = new Vector2(max.X, min.Y + titleHeight);
                    var isMouseOverTitle = mouse.X >= titleMin.X && mouse.X <= titleMax.X && mouse.Y >= titleMin.Y && mouse.Y <= titleMax.Y;

                    // Start drag only when clicking the title, but continue dragging while mouse is down
                    if ((isMouseOverTitle || te.Dragging) && io.MouseDown[0])
                    {
                        if (!te.Dragging)
                        {
                            te.Dragging = true;
                            te.OrigPos = t.Position;
                            te.DragMouseStart = io.MousePos;
                        }
                        // Use mouse-start based delta and clamp large jumps to avoid UI lockups
                        var rawDelta = io.MousePos - te.DragMouseStart;
                        const float MaxDelta = 2000f;
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
                                var subdivisions = Math.Max(1, _getSubdivisions());
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
                            }
                            catch { }
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
                    if ((isMouseOverHandle || te.Resizing) && io.MouseDown[0])
                    {
                        if (!te.Resizing)
                        {
                            te.Resizing = true;
                            te.OrigSize = t.Size;
                            te.ResizeMouseStart = io.MousePos;
                        }
                        // Use mouse-start based delta and clamp large jumps
                        var rawDelta = io.MousePos - te.ResizeMouseStart;
                        const float MaxDelta = 2000f;
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
                                var subdivisions = Math.Max(1, _getSubdivisions());
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
                            }
                            catch { }
                            // mark layout changed on resize end so host can persist
                            MarkLayoutDirty();
                        }

                        te.Resizing = false;
                    }
                }

                ImGui.PopID();
            }
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
                            var co = wp + new Vector2(0, ImGui.GetFrameHeight());
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
                        catch { }
                    }

                    if (_contextToolIndex >= 0 && _contextToolIndex < _tools.Count)
                    {
                        var t = _tools[_contextToolIndex].Tool;
                        ImGui.TextUnformatted(t.Title ?? "Tool");
                        ImGui.Separator();
                        var bg = t.BackgroundEnabled;
                        if (ImGui.Checkbox("Show background", ref bg)) t.BackgroundEnabled = bg;
                        var col = t.BackgroundColor;
                        if (ImGui.ColorEdit4("Background color", ref col, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs)) t.BackgroundColor = col;
                        ImGui.Separator();
                        if (ImGui.MenuItem("Remove component"))
                        {
                            try
                            {
                                _tools.RemoveAt(_contextToolIndex);
                                MarkLayoutDirty();
                            }
                            catch { }
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.Separator();
                        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
                    }
                }
                catch { }
                ImGui.EndPopup();
                _contextToolIndex = -1;
            }
        }
    }
}
