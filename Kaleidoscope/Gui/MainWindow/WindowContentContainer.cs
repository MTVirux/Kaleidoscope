using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Container that manages tool layout and rendering within the main window.
/// Supports drag-and-drop, grid snapping, and layout persistence.
/// </summary>
public class WindowContentContainer
{
    #region Fields
    
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

    // Rename modal state
    private int _renameToolIndex = -1;
    private bool _renamePopupOpen = false;
    private string _renameBuffer = string.Empty;

    // Grid resolution modal state
    private bool _gridResolutionPopupOpen = false;
    private LayoutGridSettings _editingGridSettings = new LayoutGridSettings();
    private int _previousColumns = 0;
    private int _previousRows = 0;

    // Current layout grid settings
    private LayoutGridSettings _currentGridSettings = new LayoutGridSettings();

    // Last known content region size for detecting window resize
    private Vector2 _lastContentSize = Vector2.Zero;

    // Flag to suppress dirty marking during layout application (restoring from persistence)
    private bool _suppressDirtyMarking = false;

    private class ToolRegistration
    {
        public string Id = string.Empty;
        public string Label = string.Empty;
        public string? Description;
        // Category path for nested menus, components separated by '>' (e.g. "Gil>Graph")
        public string? CategoryPath;
        public Func<Vector2, ToolComponent?> Factory = (_) => null;
    }

    private readonly List<ToolRegistration> _toolRegistry = new();

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
    private bool _newLayoutPopupOpen = false;
        private string _newLayoutNameBuffer = string.Empty;

        // Callback invoked when the layout changes. Host should mark the layout as dirty (not auto-save).
        public Action<List<ToolLayoutState>>? OnLayoutChanged;
        
        // Callback invoked when the user explicitly saves the layout.
        public Action? OnSaveLayoutExplicit;
        
        // Callback invoked when the user discards unsaved changes.
        public Action? OnDiscardChanges;
        
        // Callback to check if the layout has unsaved changes.
        public Func<bool>? GetIsDirty;
        
        // Callback to get the current layout name for display.
        public Func<string>? GetCurrentLayoutName;

        // Callback invoked to open the layouts management UI (config window layouts tab).
        public Action? OnManageLayouts;
        
        // Callback to show the unsaved changes dialog via LayoutEditingService.
        // Returns true if action can proceed (not dirty), false if blocked for dialog.
        public Func<string, Action, bool>? TryPerformDestructiveAction;
        
        // Callbacks for unsaved changes dialog state from LayoutEditingService
        public Func<bool>? GetShowUnsavedChangesDialog;
        public Func<string>? GetPendingActionDescription;
        public Action<UnsavedChangesChoice>? HandleUnsavedChangesChoice;

        // Callback invoked when the user saves a tool as a preset.
        // Parameters: tool type ID, preset name, serialized settings
        public Action<string, string, Dictionary<string, object?>>? OnSavePreset;

        // Save as preset state
        private int _savePresetToolIndex = -1;
        private bool _savePresetPopupOpen = false;
        private string _savePresetName = string.Empty;
        private string _savePresetDescription = string.Empty;

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

        /// <summary>
        /// Gets the minimum tool width (constant).
        /// </summary>
        private static float MinToolWidth => ConfigStatic.MinToolWidth;

        /// <summary>
        /// Gets the minimum tool height based on current text line height.
        /// This allows tools to be resized down to a single text line.
        /// </summary>
        private static float MinToolHeight => MathF.Max(16f, ImGui.GetFrameHeight());

        #endregion

        #region Interaction State

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

        // Notify host that layout has changed. Dirty state is managed by LayoutEditingService.
        // Suppressed during layout application to avoid marking restored layouts as dirty.
        private void MarkLayoutDirty()
        {
            if (_suppressDirtyMarking)
                return;
            
            try
            {
                OnLayoutChanged?.Invoke(ExportLayout());
            }
            catch (Exception ex)
            {
                LogService.Error("Error while invoking OnLayoutChanged", ex);
            }
        }

        #endregion

        #region Tool Duplication

        /// <summary>
        /// Duplicates a tool by creating a new instance with the same settings.
        /// </summary>
        /// <param name="source">The tool to duplicate.</param>
        private void DuplicateTool(ToolComponent source)
        {
            // Find the registration for this tool
            var registration = _toolRegistry.FirstOrDefault(r => r.Id == source.Id);
            if (registration == null)
            {
                LogService.Debug($"DuplicateTool: no registration found for tool id='{source.Id}'");
                return;
            }

            // Create a new instance via the factory
            var offset = new Vector2(20, 20); // Offset so the duplicate doesn't overlap exactly
            var newTool = registration.Factory(source.Position + offset);
            if (newTool == null)
            {
                LogService.Debug($"DuplicateTool: factory returned null for tool id='{source.Id}'");
                return;
            }

            // Set the new tool's Id to match the registration
            newTool.Id = registration.Id;

            // Copy visual properties
            newTool.Size = source.Size;
            newTool.Visible = source.Visible;
            newTool.BackgroundEnabled = source.BackgroundEnabled;
            newTool.HeaderVisible = source.HeaderVisible;
            newTool.OutlineEnabled = source.OutlineEnabled;
            newTool.BackgroundColor = source.BackgroundColor;

            // Copy grid coordinates (offset by position already)
            newTool.GridCol = source.GridCol + (offset.X / (source.Size.X / source.GridColSpan));
            newTool.GridRow = source.GridRow + (offset.Y / (source.Size.Y / source.GridRowSpan));
            newTool.GridColSpan = source.GridColSpan;
            newTool.GridRowSpan = source.GridRowSpan;
            newTool.HasGridCoords = source.HasGridCoords;

            // Copy custom title (with " (Copy)" suffix if set)
            if (!string.IsNullOrWhiteSpace(source.CustomTitle))
            {
                newTool.CustomTitle = source.CustomTitle + " (Copy)";
            }

            // Copy tool-specific settings
            var toolSettings = source.ExportToolSettings();
            LogService.Debug($"DuplicateTool: exported {toolSettings?.Count ?? 0} settings from source tool");
            if (toolSettings?.Count > 0)
            {
                newTool.ImportToolSettings(toolSettings);
                LogService.Debug($"DuplicateTool: imported settings to new tool");
            }

            AddToolInstance(newTool);
            LogService.Debug($"DuplicateTool: duplicated tool id='{source.Id}'");
        }

        #endregion

        #region Constructor and Grid Settings

        public WindowContentContainer(Func<float>? getCellWidthPercent = null, Func<float>? getCellHeightPercent = null, Func<int>? getSubdivisions = null)
        {
            _getCellWidthPercent = getCellWidthPercent ?? (() => 25f);
            _getCellHeightPercent = getCellHeightPercent ?? (() => 25f);
            _getSubdivisions = getSubdivisions ?? (() => 4);
        }

        /// <summary>
        /// Optional callback to get the current tool internal padding from an external source.
        /// If set and returns a non-negative value, it overrides the _currentGridSettings value.
        /// This allows real-time updates from the config window.
        /// </summary>
        public Func<int>? GetExternalToolInternalPadding { get; set; }

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
                        MathF.Max(MinToolWidth, t.GridColSpan * newCellW),
                        MathF.Max(MinToolHeight, t.GridRowSpan * newCellH)
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
        /// Updates the tool internal padding from an external source (e.g., config window).
        /// </summary>
        public void UpdateToolInternalPadding(int paddingPx)
        {
            _currentGridSettings.ToolInternalPaddingPx = paddingPx;
        }

        /// <summary>
        /// Callback invoked when grid settings change. Host should persist the settings.
        /// </summary>
        public Action<LayoutGridSettings>? OnGridSettingsChanged;

        #endregion

        #region Tool Registration

        public void SetToolFactory(Action<string, Vector2> factory)
        {
            _toolFactory = factory;
        }

        // Register a tool for the "Add tool" menu. The factory receives the click-relative
        // position and should return a configured ToolComponent (position may be adjusted by
        // the container snapping logic afterwards). Factory may return null if tool creation fails.
        public void DefineToolType(string id, string label, Func<Vector2, ToolComponent?> factory, string? description = null, string? categoryPath = null)
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

        public void AddToolInstance(ToolComponent tool)
        {
            if (tool == null) return;
            
            _tools.Add(new ToolEntry(tool));
            LogService.Debug($"AddToolInstance: added tool '{tool.Title ?? tool.Id ?? "<unknown>"}' total={_tools.Count}");
            
            // Subscribe to tool settings changes to trigger layout saves
            tool.OnToolSettingsChanged += () => MarkLayoutDirty();
            
            MarkLayoutDirty();
        }

        /// <summary>
        /// Adds a tool instance without marking the layout as dirty.
        /// Use this for initial setup (e.g., adding default tools on first run).
        /// </summary>
        public void AddToolInstanceWithoutDirty(ToolComponent tool)
        {
            if (tool == null) return;
            
            _suppressDirtyMarking = true;
            try
            {
                _tools.Add(new ToolEntry(tool));
                LogService.Debug($"AddToolInstanceWithoutDirty: added tool '{tool.Title ?? tool.Id ?? "<unknown>"}' total={_tools.Count}");
                
                // Subscribe to tool settings changes to trigger layout saves
                tool.OnToolSettingsChanged += () => MarkLayoutDirty();
            }
            finally
            {
                _suppressDirtyMarking = false;
            }
        }

        #endregion

        #region Layout Export and Import

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
            LogService.Debug($"ExportLayout: exported {ret.Count} tools");
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
            LogService.Debug($"ApplyLayout: applying {layout.Count} entries to {_tools.Count} existing tools");
            if (_toolRegistry.Count > 0)
            {
                LogService.Debug($"ApplyLayout: registered tool factories ({_toolRegistry.Count})");
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
                            catch (Exception ex)
                            {
                                LogService.Debug($"[WindowContentContainer] Type.GetType failed for '{entry.Type}': {ex.Message}");
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
                                        LogService.Debug($"[WindowContentContainer] Assembly type resolution failed for '{entry.Type}' in {asm.GetName().Name}: {ex.Message}");
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
            
            // Remove tools that existed before ApplyLayout but were not matched to any layout entry.
            // Iterate in reverse to safely remove by index without shifting issues.
            for (var i = originalToolCount - 1; i >= 0; i--)
            {
                if (!matchedIndices.Contains(i))
                {
                    try
                    {
                        var tool = _tools[i].Tool;
                        LogService.Debug($"ApplyLayout: removing unmatched tool '{tool.Title}' (id={tool.Id}, type={tool.GetType().FullName})");
                        tool.Dispose();
                        _tools.RemoveAt(i);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error($"Failed to remove unmatched tool at index {i}", ex);
                    }
                }
            }
            
            // Force grid-based position recalculation on the next frame.
            // This is essential when importing layouts from different window sizes (e.g., windowed to fullscreen).
            // Tools with HasGridCoords will have their Position/Size recalculated from grid coordinates.
            _lastContentSize = Vector2.Zero;
        }

        #endregion

        #region Drawing

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
                            LogService.Debug($"Tool click detection error: {ex.Message}");
                        }

                        if (clickedTool >= 0)
                        {
                            // Tool context menu works without edit mode
                            _contextToolIndex = clickedTool;
                            _lastContextClickRel = mouse - contentOrigin;
                            ImGui.SetNextWindowPos(mouse);
                            ImGui.OpenPopup("tool_context_menu");
                        }
                        else if (editMode)
                        {
                            // Content context menu (add tools) only works in edit mode
                            _lastContextClickRel = mouse - contentOrigin;
                            ImGui.SetNextWindowPos(mouse);
                            ImGui.OpenPopup("content_context_menu");
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
                                                    LogService.Debug($"Tool snap error: {ex.Message}");
                                                }
                                                AddToolInstance(tool);
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
                                LogService.Error("Failed to save layout", ex);
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
                                    LogService.Error("Failed to discard changes", ex);
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
                                        LogService.Error($"Failed to create layout '{_newLayoutNameBuffer}'", ex);
                                    }
                                    try
                                    {
                                        // Switch to the newly created layout
                                        OnLoadLayout?.Invoke(_newLayoutNameBuffer);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Error($"Failed to load new layout '{_newLayoutNameBuffer}'", ex);
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
                            LogService.Error("Error in new layout popup", ex);
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
                    LogService.Debug($"Background draw error: {ex.Message}");
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
                    catch (Exception ex) { LogService.Debug($"[WindowContentContainer] IsMainWindowInteracting callback error: {ex.Message}"); }

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
                                LogService.Debug($"Drag snap error: {ex.Message}");
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
                                LogService.Error("Failed to duplicate tool", ex);
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

            // Tool settings window (renders when a tool has opened its settings)
            DrawToolSettingsWindow();
            
            // Tool rename modal
            DrawToolRenameModal();
            
            // Save as Preset modal
            DrawSavePresetModal();
        }

        #endregion

        #region Modal Dialogs

        /// <summary>
        /// Draws the tool rename modal if one is currently open.
        /// </summary>
        private void DrawToolRenameModal()
        {
            if (_renameToolIndex < 0 || _renameToolIndex >= _tools.Count)
                return;

            const string popupName = "tool_rename_popup";
            var toolToRename = _tools[_renameToolIndex].Tool;

            // The popup must be opened each frame until it appears
            if (_renamePopupOpen && !ImGui.IsPopupOpen(popupName))
            {
                ImGui.OpenPopup(popupName);
            }

            if (!ImGui.BeginPopupModal(popupName, ref _renamePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                // Modal not showing - if user closed it, reset state
                if (!_renamePopupOpen)
                {
                    _renameToolIndex = -1;
                }
                return;
            }

            try
            {
                ImGui.TextUnformatted("Rename Tool");
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextUnformatted("Enter a new name for this tool:");
                ImGui.InputText("##renameinput", ref _renameBuffer, ConfigStatic.TextInputBufferSize);
                
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"Original name: {toolToRename.Title}");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGuiHelpers.ButtonAutoWidth("OK"))
                {
                    var trimmed = _renameBuffer?.Trim();
                    // If the name is empty or matches the original title, clear the custom title
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed == toolToRename.Title)
                    {
                        toolToRename.CustomTitle = null;
                    }
                    else
                    {
                        toolToRename.CustomTitle = trimmed;
                    }
                    MarkLayoutDirty();
                    ImGui.CloseCurrentPopup();
                    _renamePopupOpen = false;
                }
                ImGui.SameLine();
                if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    _renamePopupOpen = false;
                }
                ImGui.SameLine();
                if (ImGuiHelpers.ButtonAutoWidth("Reset"))
                {
                    toolToRename.CustomTitle = null;
                    MarkLayoutDirty();
                    ImGui.CloseCurrentPopup();
                    _renamePopupOpen = false;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Reset to the original name");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Error in tool rename modal", ex);
            }

            ImGui.EndPopup();

            if (!_renamePopupOpen)
            {
                _renameToolIndex = -1;
            }
        }

        /// <summary>
        /// Draws the save as preset modal if one is currently open.
        /// </summary>
        private void DrawSavePresetModal()
        {
            if (_savePresetToolIndex < 0 || _savePresetToolIndex >= _tools.Count)
                return;

            const string popupName = "save_preset_popup";
            var toolToSave = _tools[_savePresetToolIndex].Tool;

            // The popup must be opened each frame until it appears
            if (_savePresetPopupOpen && !ImGui.IsPopupOpen(popupName))
            {
                ImGui.OpenPopup(popupName);
            }

            if (!ImGui.BeginPopupModal(popupName, ref _savePresetPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                // Modal not showing - if user closed it, reset state
                if (!_savePresetPopupOpen)
                {
                    _savePresetToolIndex = -1;
                }
                return;
            }

            try
            {
                ImGui.TextUnformatted("Save as Preset");
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextWrapped("Save the current tool configuration as a reusable preset.");
                ImGui.Spacing();

                ImGui.TextUnformatted("Preset Name:");
                ImGui.SetNextItemWidth(300f);
                ImGui.InputTextWithHint("##presetNameInput", "Enter preset name", ref _savePresetName, 256);

                ImGui.Spacing();
                ImGui.TextUnformatted("Description (optional):");
                ImGui.SetNextItemWidth(300f);
                ImGui.InputTextWithHint("##presetDescInput", "Enter description", ref _savePresetDescription, 512);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var canSave = !string.IsNullOrWhiteSpace(_savePresetName);
                if (!canSave)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGuiHelpers.ButtonAutoWidth("Save"))
                {
                    try
                    {
                        var settings = toolToSave.ExportToolSettings();
                        if (settings != null && OnSavePreset != null)
                        {
                            OnSavePreset.Invoke(toolToSave.Id, _savePresetName.Trim(), settings);
                            LogService.Debug($"Saved preset '{_savePresetName}' for tool type '{toolToSave.Id}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("Error saving preset", ex);
                    }
                    ImGui.CloseCurrentPopup();
                    _savePresetPopupOpen = false;
                }

                if (!canSave)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip("Enter a preset name to save");
                    }
                }

                ImGui.SameLine();
                if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    _savePresetPopupOpen = false;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Error in save preset modal", ex);
            }

            ImGui.EndPopup();

            if (!_savePresetPopupOpen)
            {
                _savePresetToolIndex = -1;
            }
        }

        /// <summary>
        /// Draws the tool settings window if one is currently open.
        /// </summary>
        private void DrawToolSettingsWindow()
        {
        if (_settingsToolIndex < 0 || _settingsToolIndex >= _tools.Count)
            return;

        var toolForSettings = _tools[_settingsToolIndex].Tool;
        var windowTitle = $"{toolForSettings.Title ?? "Tool"} Settings###ToolSettingsWindow";

        ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(windowTitle, ref _settingsPopupOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            if (!_settingsPopupOpen)
            {
                _settingsToolIndex = -1;
            }
            return;
        }

        try
        {
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
            if (ImGuiHelpers.ButtonAutoWidth("Close"))
            {
                _settingsPopupOpen = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Error in tool settings window", ex);
        }

        ImGui.End();

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
            
            // Tool internal padding
            ImGui.TextUnformatted("Tool Internal Padding (pixels):");
            var toolPadding = _editingGridSettings.ToolInternalPaddingPx;
            if (ImGui.SliderInt("##toolpadding", ref toolPadding, 0, 32))
            {
                _editingGridSettings.ToolInternalPaddingPx = toolPadding;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Padding in pixels inside each tool.\nHigher values create more space around tool content.\n0 = no padding.");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // OK / Cancel buttons
            if (ImGuiHelpers.ButtonAutoWidth("OK"))
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
            
            if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
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

    /// <summary>
    /// Draws the unsaved changes confirmation dialog using state from LayoutEditingService.
    /// </summary>
    private void DrawUnsavedChangesDialog()
    {
        // Check if the dialog should be shown via LayoutEditingService callback
        var shouldShow = GetShowUnsavedChangesDialog?.Invoke() ?? false;
        if (!shouldShow)
        {
            return;
        }
        
        const string popupName = "unsaved_changes_popup";
        
        // Open the popup if not already open
        if (!ImGui.IsPopupOpen(popupName))
        {
            ImGui.OpenPopup(popupName);
        }
        
        var open = true;
        if (!ImGui.BeginPopupModal(popupName, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }
        
        try
        {
            ImGui.TextUnformatted("Unsaved Layout Changes");
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.TextWrapped("You have unsaved changes to the current layout.");
            
            var description = GetPendingActionDescription?.Invoke();
            if (!string.IsNullOrWhiteSpace(description))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), $"Action: {description}");
            }
            ImGui.Spacing();
            ImGui.TextUnformatted("What would you like to do?");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Save button
            if (ImGuiHelpers.ButtonAutoWidth("Save"))
            {
                HandleUnsavedChangesChoice?.Invoke(UnsavedChangesChoice.Save);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Save your changes, then continue");
            }
            
            ImGui.SameLine();
            
            // Discard button
            if (ImGuiHelpers.ButtonAutoWidth("Discard"))
            {
                HandleUnsavedChangesChoice?.Invoke(UnsavedChangesChoice.Discard);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Discard your changes and revert to the last saved layout");
            }
            
            ImGui.SameLine();
            
            // Cancel button
            if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
            {
                HandleUnsavedChangesChoice?.Invoke(UnsavedChangesChoice.Cancel);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Cancel and return to editing");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Error in unsaved changes dialog", ex);
        }

        ImGui.EndPopup();
    }

    #endregion
}
