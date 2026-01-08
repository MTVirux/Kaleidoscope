using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Container that manages tool layout and rendering within the main window.
/// Supports drag-and-drop, grid snapping, and layout persistence.
/// </summary>
public partial class WindowContentContainer
{
    
    private readonly Func<float> _getCellWidthPercent;
    private readonly Func<float> _getCellHeightPercent;
    private readonly Func<int> _getSubdivisions;
    private Action<string, Vector2>? _toolFactory;
    private Vector2 _lastContextClickRel;
    // Index of the tool that was right-clicked to open the tool-specific context menu
    private int _contextToolIndex = -1;
    // Pending popup to open next frame (prevents z-order issues by delaying one frame)
    private string? _pendingPopup = null;
    private Vector2 _pendingPopupPos = Vector2.Zero;
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

        // Callback to check if fullscreen mode is active
        // Used to ensure tool settings windows stay on top
        public Func<bool>? IsFullscreenMode;

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

        private void SetDraggingState(bool dragging)
        {
            if (_anyDragging == dragging) return;
            _anyDragging = dragging;
            try { OnDraggingChanged?.Invoke(dragging); }
            catch (Exception ex) { LogService.Debug(LogCategory.UI, $"OnDraggingChanged error: {ex.Message}"); }
        }

        // Update the global resizing state and notify if changed
        private void SetResizingState(bool resizing)
        {
            if (_anyResizing == resizing) return;
            _anyResizing = resizing;
            try { OnResizingChanged?.Invoke(resizing); }
            catch (Exception ex) { LogService.Debug(LogCategory.UI, $"OnResizingChanged error: {ex.Message}"); }
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
                LogService.Error(LogCategory.UI, "Error while invoking OnLayoutChanged", ex);
            }
        }

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
                LogService.Debug(LogCategory.UI, $"DuplicateTool: no registration found for tool id='{source.Id}'");
                return;
            }

            // Create a new instance via the factory
            var offset = new Vector2(20, 20); // Offset so the duplicate doesn't overlap exactly
            var newTool = registration.Factory(source.Position + offset);
            if (newTool == null)
            {
                LogService.Debug(LogCategory.UI, $"DuplicateTool: factory returned null for tool id='{source.Id}'");
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
            LogService.Debug(LogCategory.UI, $"DuplicateTool: exported {toolSettings?.Count ?? 0} settings from source tool");
            if (toolSettings?.Count > 0)
            {
                newTool.ImportToolSettings(toolSettings);
                LogService.Debug(LogCategory.UI, $"DuplicateTool: imported settings to new tool");
            }

            AddToolInstance(newTool);
            LogService.Debug(LogCategory.UI, $"DuplicateTool: duplicated tool id='{source.Id}'");
        }

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
            LogService.Debug(LogCategory.UI, $"AddToolInstance: added tool '{tool.Title ?? tool.Id ?? "<unknown>"}' total={_tools.Count}");
            
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
                LogService.Debug(LogCategory.UI, $"AddToolInstanceWithoutDirty: added tool '{tool.Title ?? tool.Id ?? "<unknown>"}' total={_tools.Count}");
                
                // Subscribe to tool settings changes to trigger layout saves
                tool.OnToolSettingsChanged += () => MarkLayoutDirty();
            }
            finally
            {
                _suppressDirtyMarking = false;
            }
        }
}