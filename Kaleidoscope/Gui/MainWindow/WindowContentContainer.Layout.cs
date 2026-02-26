using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

public sealed partial class WindowContentContainer
{
        /// <summary>
        /// Applies layout state properties from a <see cref="ToolLayoutState"/> to a <see cref="ToolComponent"/>.
        /// Optionally sets the tool's Id and Title from the entry.
        /// </summary>
        private static void ApplyLayoutState(ToolComponent tool, ToolLayoutState entry, bool setId = false, bool setTitle = false)
        {
            if (setId && !string.IsNullOrWhiteSpace(entry.Id))
                tool.Id = entry.Id;
            tool.Position = entry.Position;
            tool.Size = entry.Size;
            tool.Visible = entry.Visible;
            tool.BackgroundEnabled = entry.BackgroundEnabled;
            tool.HeaderVisible = entry.HeaderVisible;
            tool.OutlineEnabled = entry.OutlineEnabled;
            tool.BackgroundColor = entry.BackgroundColor;
            tool.CustomTitle = entry.CustomTitle;
            tool.GridCol = entry.GridCol;
            tool.GridRow = entry.GridRow;
            tool.GridColSpan = entry.GridColSpan;
            tool.GridRowSpan = entry.GridRowSpan;
            tool.HasGridCoords = entry.HasGridCoords;
            if (tool.HasGridCoords) ClampGridCoords(tool);
            if (setTitle && !string.IsNullOrWhiteSpace(entry.Title))
                tool.Title = entry.Title;
            if (entry.ToolSettings?.Count > 0)
                tool.ImportToolSettings(entry.ToolSettings);
        }
        /// <summary>
        /// Clamps grid coordinates to valid ranges: position >= 0, span > 0.
        /// Uses a tiny minimum span to prevent zero/negative values without
        /// artificially inflating tools on low-resolution grids.
        /// </summary>
        private static void ClampGridCoords(ToolComponent tool)
        {
            const float minSpan = 0.01f;
            tool.GridCol = MathF.Max(0f, tool.GridCol);
            tool.GridRow = MathF.Max(0f, tool.GridRow);
            tool.GridColSpan = MathF.Max(minSpan, tool.GridColSpan);
            tool.GridRowSpan = MathF.Max(minSpan, tool.GridRowSpan);
        }

        public List<ToolLayoutState> ExportLayout()
        {
            var ret = new List<ToolLayoutState>();
            foreach (var te in _tools)
            {
                if (te?.Tool is not { } t) continue;
                var state = new ToolLayoutState
                {
                    Id = t.Id,
                    Type = t.GetType().FullName ?? t.GetType().Name,
                    Title = t.Title,
                    CustomTitle = t.CustomTitle,
                    Position = t.Position,
                    Size = t.Size,
                    Visible = t.Visible,
                    BackgroundEnabled = t.BackgroundEnabled,
                    BackgroundColor = t.BackgroundColor,
                    HeaderVisible = t.HeaderVisible,
                    OutlineEnabled = t.OutlineEnabled,
                    // Include grid coordinates
                    GridCol = t.GridCol,
                    GridRow = t.GridRow,
                    GridColSpan = t.GridColSpan,
                    GridRowSpan = t.GridRowSpan,
                    HasGridCoords = t.HasGridCoords,
                };
                
                // Export tool-specific settings
                var toolSettings = t.ExportToolSettings();
                if (toolSettings != null && toolSettings.Count > 0)
                {
                    state.ToolSettings = toolSettings;
                }
                
                ret.Add(state);
            }
            LogService.Debug(LogCategory.UI, $"ExportLayout: exported {ret.Count} tools");
            return ret;
        }

        public void ApplyLayout(List<ToolLayoutState>? layout)
        {
            if (layout == null) return;
            
            // Suppress dirty marking during layout application since we're restoring
            // persisted state, not making user changes
            _suppressDirtyMarking = true;
            try
            {
                ApplyLayoutInternal(layout);
            }
            finally
            {
                _suppressDirtyMarking = false;
            }
        }

        private void ApplyLayoutInternal(List<ToolLayoutState> layout)
        {
            LogService.Debug(LogCategory.UI, $"ApplyLayout: applying {layout.Count} entries to {_tools.Count} existing tools");
            if (_toolRegistry.Count > 0)
            {
                LogService.Debug(LogCategory.UI, $"ApplyLayout: registered tool factories ({_toolRegistry.Count})");
            }
            
            // Track the original tool count before adding new tools
            var originalToolCount = _tools.Count;
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
                        // Ensure the Id is set from the layout entry for future lookups
                        if (!string.IsNullOrWhiteSpace(entry.Id))
                        {
                            match.Id = entry.Id;
                        }
                        ApplyLayoutState(match, entry);
                        if (matchIdx >= 0) matchedIndices.Add(matchIdx);
                        LogService.Debug(LogCategory.UI, $"ApplyLayout: matched existing tool for entry '{entry.Id}' (type={entry.Type}, title={entry.Title})");
                        continue;
                    }

                    // No existing tool matched — attempt to create a new instance from the registered tool factories.
                    // First, try to find a registration by factory id (common case when Id contains a factory name).
                    var createdAny = false;
                    var reg = _toolRegistry.Find(r => string.Equals(r.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                    if (reg != null && reg.Factory != null)
                    {
                        LogService.Debug(LogCategory.UI, $"ApplyLayout: attempting registry factory by id='{reg.Id}' for entry '{entry.Id}'");
                        try
                        {
                            var created = reg.Factory(entry.Position);
                            if (created != null)
                            {
                                created.Id = reg.Id;
                                ApplyLayoutState(created, entry, setTitle: true);
                                AddToolInstance(created);
                                // Mark newly added tool as matched so it won't be reused for another entry
                                matchedIndices.Add(_tools.Count - 1);
                                LogService.Debug(LogCategory.UI, $"ApplyLayout: created tool via registry id='{reg.Id}' for entry '{entry.Id}' (type={entry.Type})");
                                createdAny = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug(LogCategory.UI, $"ApplyLayout: registry factory '{reg.Id}' threw: {ex.Message}");
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
                                    cand.Id = candReg.Id;
                                    ApplyLayoutState(cand, entry, setTitle: true);
                                    AddToolInstance(cand);
                                    // Mark newly added tool as matched so it won't be reused for another entry
                                    matchedIndices.Add(_tools.Count - 1);
                                    LogService.Debug(LogCategory.UI, $"ApplyLayout: created tool via factory '{candReg.Id}' matched by type for entry '{entry.Id}'");
                                    createdAny = true;
                                    break;
                                }
                                else
                                {
                                    // Type didn't match — dispose the probed instance to avoid resource leaks
                                    cand.Dispose();
                                }
                            }
                            catch (Exception ex)
                            {
                                LogService.Debug(LogCategory.UI, $"Factory invocation failed for registry entry '{candReg.Id}': {ex.Message}");
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
                            catch (Exception ex)
                            {
                                LogService.Debug(LogCategory.UI, $"[WindowContentContainer] Type.GetType failed for '{entry.Type}': {ex.Message}");
                                found = null;
                            }
                            if (found == null)
                            {
                                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                                {
                                    try
                                    {
                                        var t = asm.GetType(entry.Type);
                                        if (t != null) { found = t; break; }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Debug(LogCategory.UI, $"[WindowContentContainer] Assembly type resolution failed for '{entry.Type}' in {asm.GetName().Name}: {ex.Message}");
                                    }
                                }
                            }

                            if (found != null && typeof(ToolComponent).IsAssignableFrom(found))
                            {
                                try
                                {
                                    var inst = Activator.CreateInstance(found) as ToolComponent;
                                    if (inst != null)
                                    {
                                        ApplyLayoutState(inst, entry, setId: true, setTitle: true);
                                        AddToolInstance(inst);
                                        // Mark newly added tool as matched so it won't be reused for another entry
                                        matchedIndices.Add(_tools.Count - 1);
                                        LogService.Debug(LogCategory.UI, $"ApplyLayout: created tool via reflection type='{entry.Type}' for entry '{entry.Id}'");
                                        createdAny = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogService.Debug(LogCategory.UI, $"Reflection creation failed for type '{entry.Type}': {ex.Message}");
                                }
                            }
                            else
                            {
                                LogService.Debug(LogCategory.UI, $"ApplyLayout: reflection could not find type '{entry.Type}'");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug(LogCategory.UI, $"ApplyLayout: reflection attempt failed for '{entry.Type}': {ex.Message}");
                        }
                    }

                    if (!createdAny)
                    {
                        LogService.Debug(LogCategory.UI, $"ApplyLayout: no existing tool matched and creation failed for '{entry.Id}' / '{entry.Type}'");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.UI, $"Failed to apply layout entry '{entry.Id}'", ex);
                }
            }
            
            // Remove tools that existed before ApplyLayout but were not matched to any layout entry.
            // Iterate in reverse to safely remove by index without shifting issues.
            for (var i = originalToolCount - 1; i >= 0; i--)
            {
                if (!matchedIndices.Contains(i))
                {
                    try
                    {
                        var tool = _tools[i].Tool;
                        LogService.Debug(LogCategory.UI, $"ApplyLayout: removing unmatched tool '{tool.Title}' (id={tool.Id}, type={tool.GetType().FullName})");
                        tool.Dispose();
                        _tools.RemoveAt(i);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(LogCategory.UI, $"Failed to remove unmatched tool at index {i}", ex);
                    }
                }
            }
            
            // Force grid-based position recalculation on the next frame.
            // This is essential when importing layouts from different window sizes (e.g., windowed to fullscreen).
            // Tools with HasGridCoords will have their Position/Size recalculated from grid coordinates.
            _lastContentSize = Vector2.Zero;
        }

}