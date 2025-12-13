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

        public WindowContentContainer(Func<float>? getCellWidthPercent = null, Func<float>? getCellHeightPercent = null, Func<int>? getSubdivisions = null)
        {
            _getCellWidthPercent = getCellWidthPercent ?? (() => 25f);
            _getCellHeightPercent = getCellHeightPercent ?? (() => 25f);
            _getSubdivisions = getSubdivisions ?? (() => 4);
        }

        public void AddTool(ToolComponent tool)
        {
            _tools.Add(new ToolEntry(tool));
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
                        }

                        te.Resizing = false;
                    }
                }

                ImGui.PopID();
            }
        }
    }
}
