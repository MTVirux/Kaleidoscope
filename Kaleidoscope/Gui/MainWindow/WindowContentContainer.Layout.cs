using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

public partial class WindowContentContainer
{

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
                        match.Position = entry.Position;
                        match.Size = entry.Size;
                        match.Visible = entry.Visible;
                        match.BackgroundEnabled = entry.BackgroundEnabled;
                        match.HeaderVisible = entry.HeaderVisible;
                        match.OutlineEnabled = entry.OutlineEnabled;
                        match.CustomTitle = entry.CustomTitle;
                        // Apply grid coordinates
                        match.GridCol = entry.GridCol;
                        match.GridRow = entry.GridRow;
                        match.GridColSpan = entry.GridColSpan;
                        match.GridRowSpan = entry.GridRowSpan;
                        match.HasGridCoords = entry.HasGridCoords;
                        // Apply tool-specific settings
                        if (entry.ToolSettings?.Count > 0)
                        {
                            match.ImportToolSettings(entry.ToolSettings);
                        }
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
                                created.Position = entry.Position;
                                created.Size = entry.Size;
                                created.Visible = entry.Visible;
                                created.BackgroundEnabled = entry.BackgroundEnabled;
                                created.HeaderVisible = entry.HeaderVisible;
                                created.OutlineEnabled = entry.OutlineEnabled;
                                created.BackgroundColor = entry.BackgroundColor;
                                // Apply grid coordinates
                                created.GridCol = entry.GridCol;
                                created.GridRow = entry.GridRow;
                                created.GridColSpan = entry.GridColSpan;
                                created.GridRowSpan = entry.GridRowSpan;
                                created.HasGridCoords = entry.HasGridCoords;
                                if (!string.IsNullOrWhiteSpace(entry.Title)) created.Title = entry.Title;
                                created.CustomTitle = entry.CustomTitle;
                                // Apply tool-specific settings
                                if (entry.ToolSettings?.Count > 0)
                                {
                                    created.ImportToolSettings(entry.ToolSettings);
                                }
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
                                    cand.Position = entry.Position;
                                    cand.Size = entry.Size;
                                    cand.Visible = entry.Visible;
                                    cand.BackgroundEnabled = entry.BackgroundEnabled;
                                    cand.HeaderVisible = entry.HeaderVisible;
                                    cand.OutlineEnabled = entry.OutlineEnabled;
                                    cand.BackgroundColor = entry.BackgroundColor;
                                    // Apply grid coordinates
                                    cand.GridCol = entry.GridCol;
                                    cand.GridRow = entry.GridRow;
                                    cand.GridColSpan = entry.GridColSpan;
                                    cand.GridRowSpan = entry.GridRowSpan;
                                    cand.HasGridCoords = entry.HasGridCoords;
                                    if (!string.IsNullOrWhiteSpace(entry.Title)) cand.Title = entry.Title;
                                    cand.CustomTitle = entry.CustomTitle;
                                    // Apply tool-specific settings
                                    if (entry.ToolSettings?.Count > 0)
                                    {
                                        cand.ImportToolSettings(entry.ToolSettings);
                                    }
                                    AddToolInstance(cand);
                                    // Mark newly added tool as matched so it won't be reused for another entry
                                    matchedIndices.Add(_tools.Count - 1);
                                    LogService.Debug(LogCategory.UI, $"ApplyLayout: created tool via factory '{candReg.Id}' matched by type for entry '{entry.Id}'");
                                    createdAny = true;
                                    break;
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
                                        inst.Id = entry.Id;
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
                                        inst.CustomTitle = entry.CustomTitle;
                                        // Apply tool-specific settings
                                        if (entry.ToolSettings?.Count > 0)
                                        {
                                            inst.ImportToolSettings(entry.ToolSettings);
                                        }
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