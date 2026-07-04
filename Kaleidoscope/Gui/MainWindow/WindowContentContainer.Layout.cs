using Kaleidoscope.Services;

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

        /// <summary>
        /// Finds an existing, not-yet-matched tool for a layout entry, preferring Id, then Title,
        /// then .NET type name. Returns null (with <paramref name="matchIdx"/> = -1) when nothing matches.
        /// </summary>
        private ToolComponent? FindMatchingTool(ToolLayoutState entry, HashSet<int> matchedIndices, out int matchIdx)
        {
            for (var i = 0; i < Tools.Count; i++)
            {
                if (matchedIndices.Contains(i)) continue;
                if (Tools[i].Tool.Id == entry.Id) { matchIdx = i; return Tools[i].Tool; }
            }
            for (var i = 0; i < Tools.Count; i++)
            {
                if (matchedIndices.Contains(i)) continue;
                if (Tools[i].Tool.Title == entry.Title) { matchIdx = i; return Tools[i].Tool; }
            }
            for (var i = 0; i < Tools.Count; i++)
            {
                if (matchedIndices.Contains(i)) continue;
                if (Tools[i].Tool.GetType().FullName == entry.Type) { matchIdx = i; return Tools[i].Tool; }
            }
            matchIdx = -1;
            return null;
        }

        public List<ToolLayoutState> ExportLayout()
        {
            var ret = new List<ToolLayoutState>();
            foreach (var te in Tools)
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
            LogService.Debug(LogCategory.UI, $"ApplyLayout: applying {layout.Count} entries to {Tools.Count} existing tools");
            if (ToolRegistry.Count > 0)
            {
                LogService.Debug(LogCategory.UI, $"ApplyLayout: registered tool factories ({ToolRegistry.Count})");
            }
            
            // Track the original tool count before adding new tools
            var originalToolCount = Tools.Count;
            var matchedIndices = new System.Collections.Generic.HashSet<int>();
            for (var li = 0; li < layout.Count; li++)
            {
                var entry = layout[li];
                try
                {
                    // Match an existing, not-yet-matched tool by Id, then Title, then .NET type name.
                    var match = FindMatchingTool(entry, matchedIndices, out var matchIdx);

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
                    var reg = ToolRegistry.Find(r => string.Equals(r.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
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
                                matchedIndices.Add(Tools.Count - 1);
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
                        // If not found by id, use ToolFactory to look up the definition by .NET type name.
                        // Every registered factory instance's type is known to ToolFactory (ToolRegistry is
                        // populated from ToolFactory's available definitions), so this type-name lookup covers
                        // everything the old brute-force "create-every-factory-and-match-by-type" probe did,
                        // without instantiating and disposing throwaway tools.
                        if (Factory != null && !string.IsNullOrWhiteSpace(entry.Type))
                        {
                            var def = Factory.FindDefinitionByTypeName(entry.Type);
                            if (def != null)
                            {
                                LogService.Debug(LogCategory.UI, $"ApplyLayout: found definition '{def.Id}' by type '{entry.Type}' for entry '{entry.Id}'");
                                try
                                {
                                    var created = Factory.Create(def.Id, entry.Position);
                                    if (created != null)
                                    {
                                        created.Id = def.Id;
                                        ApplyLayoutState(created, entry, setTitle: true);
                                        AddToolInstance(created);
                                        matchedIndices.Add(Tools.Count - 1);
                                        LogService.Debug(LogCategory.UI, $"ApplyLayout: created tool via ToolFactory for definition '{def.Id}' (type={entry.Type})");
                                        createdAny = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogService.Debug(LogCategory.UI, $"ApplyLayout: ToolFactory.Create failed for '{def.Id}': {ex.Message}");
                                }
                            }
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
                        var tool = Tools[i].Tool;
                        LogService.Debug(LogCategory.UI, $"ApplyLayout: removing unmatched tool '{tool.Title}' (id={tool.Id}, type={tool.GetType().FullName})");
                        tool.Dispose();
                        Tools.RemoveAt(i);
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