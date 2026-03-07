using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Table;

/// <summary>
/// Shared table infrastructure used by both <see cref="TableWidget{TRow}"/> and ItemTableWidget.
/// Owns the mechanical plumbing: column resize state machine, column width capture,
/// column setup, header rendering with SHIFT+click/drag selection, context menus,
/// and sort specs reading. Consumers compose this (has-a) rather than inherit.
/// </summary>
public sealed class TableCore
{
    private readonly string _tableId;
    
    // Column resize state
    private MTColumnResizeAction _pendingResizeAction = MTColumnResizeAction.None;
    private int _resizeTargetColumn = -1; // -1 means "all columns"
    private int _contextMenuColumn = -1;  // Which column the context menu was opened for
    private int _tableIdSuffix;           // Incremented to force ImGui table state reset after resize
    private int _columnWidthsInitFrames;  // Counts frames after table recreation for init settling
    
    // Columns that should not get resize context menu (e.g. fixed/label columns)
    private readonly HashSet<int> _noResizeColumns = new();
    
    // Column selection state (for SHIFT+click/drag merge selection)
    private readonly HashSet<int> _selectedColumnIndices = new();
    private bool _isSelectingColumns = false;
    private int _selectionStartColumn = -1;
    private bool _skipNextClick = false;
    
    // Sort state tracking
    private bool _sortInitialized = false;
    
    /// <summary>
    /// Raised when column merge groups change (merge or unmerge action).
    /// </summary>
    public event Action? OnMergeChanged;
    
    /// <summary>
    /// Gets the set of currently selected column indices (for external highlight rendering).
    /// </summary>
    public IReadOnlySet<int> SelectedColumnIndices => _selectedColumnIndices;
    
    /// <summary>
    /// Gets the current table ID suffix (for appending to table IDs to force ImGui state reset).
    /// </summary>
    public int TableIdSuffix => _tableIdSuffix;
    
    /// <summary>
    /// Gets whether the table is in its initial settling period (first 3 frames after creation/reset).
    /// During init, columns should have NoResize to prevent ImGui's auto-fit from overwriting widths.
    /// </summary>
    public bool IsInitializing => _columnWidthsInitFrames <= 3;
    
    /// <summary>
    /// Increments the init frame counter. Call once per frame before checking <see cref="IsInitializing"/>.
    /// Used by widgets that implement their own column width capture logic.
    /// </summary>
    public void TickInitFrame() => _columnWidthsInitFrames++;
    
    /// <summary>
    /// Gets or sets whether to skip the next click for selection clearing.
    /// Set to true after merge/unmerge actions to prevent the click from clearing the selection.
    /// </summary>
    public bool SkipNextClick
    {
        get => _skipNextClick;
        set => _skipNextClick = value;
    }
    
    /// <summary>
    /// Gets the column index of the column the context menu was opened for.
    /// </summary>
    public int ContextMenuColumn => _contextMenuColumn;
    
    /// <summary>
    /// Whether a resize action is pending.
    /// </summary>
    public bool HasPendingResize => _pendingResizeAction != MTColumnResizeAction.None;
    
    /// <summary>
    /// Creates a new TableCore instance.
    /// </summary>
    /// <param name="tableId">Unique ID for ImGui table identification.</param>
    public TableCore(string tableId)
    {
        _tableId = tableId;
    }
    
    /// <summary>
    /// Marks a column as non-resizable. Non-resizable columns won't show the resize context menu
    /// on right-click and their width is excluded from fill calculations.
    /// </summary>
    /// <param name="columnIndex">The 0-based column index to mark as non-resizable.</param>
    public void AddNoResizeColumn(int columnIndex)
    {
        _noResizeColumns.Add(columnIndex);
    }
    
    /// <summary>
    /// Gets whether a column is marked as non-resizable.
    /// </summary>
    public bool IsNoResizeColumn(int columnIndex) => _noResizeColumns.Contains(columnIndex);
    
    /// <summary>
    /// Forces a table state reset, causing ImGui to re-read column init widths.
    /// Call this after programmatically changing column widths outside of the context menu.
    /// </summary>
    public void ResetColumnWidthState()
    {
        _columnWidthsInitFrames = 0;
        _tableIdSuffix++;
    }
    
    /// <summary>
    /// Gets the effective table ID, including the suffix for forced resets.
    /// </summary>
    public string GetEffectiveTableId()
    {
        return _tableIdSuffix > 0 ? $"{_tableId}_{_tableIdSuffix}" : _tableId;
    }
    
    #region Selection State Management
    
    /// <summary>
    /// Handles clearing selection state when the user clicks without SHIFT held.
    /// Call this at the start of each frame before drawing headers.
    /// </summary>
    /// <param name="isShiftHeld">Whether the SHIFT key is held.</param>
    /// <param name="isPopupOpen">Whether any popup is currently open.</param>
    public void HandleSelectionClearing(bool isShiftHeld, bool isPopupOpen)
    {
        if (_skipNextClick)
        {
            _skipNextClick = false;
        }
        else if (!isShiftHeld && !isPopupOpen && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectedColumnIndices.Clear();
            _isSelectingColumns = false;
            _selectionStartColumn = -1;
        }
    }
    
    /// <summary>
    /// Clears all column selection state.
    /// </summary>
    public void ClearColumnSelection()
    {
        _selectedColumnIndices.Clear();
        _isSelectingColumns = false;
        _selectionStartColumn = -1;
    }
    
    #endregion
    
    #region Column Setup
    
    /// <summary>
    /// Sets up ImGui columns with proper flags, including temporary NoResize during init
    /// and DefaultSort flags based on saved sort state.
    /// </summary>
    /// <param name="columns">Column definitions.</param>
    /// <param name="sortColumns">Current sort column entries.</param>
    /// <param name="fallbackSortIndex">Fallback sort column index if sortColumns is empty.</param>
    /// <param name="fallbackSortAscending">Fallback sort ascending if sortColumns is empty.</param>
    public void SetupColumns(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<SortColumnEntry> sortColumns,
        int fallbackSortIndex,
        bool fallbackSortAscending)
    {
        var sortColumnSet = BuildSortColumnSet(sortColumns, fallbackSortIndex, fallbackSortAscending);
        var isInitializing = IsInitializing;
        
        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var colFlags = column.Flags;
            var isNoResize = _noResizeColumns.Contains(i);
            
            // Apply default sort to saved sort columns
            if (sortColumnSet.TryGetValue(i, out var ascending))
            {
                colFlags |= ascending 
                    ? ImGuiTableColumnFlags.DefaultSort 
                    : ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending;
            }
            else if (column.PreferSortDescending)
            {
                colFlags |= ImGuiTableColumnFlags.PreferSortDescending;
            }
            
            // Apply width flags
            if (column.Stretch)
            {
                colFlags |= ImGuiTableColumnFlags.WidthStretch;
            }
            else
            {
                colFlags |= ImGuiTableColumnFlags.WidthFixed;
            }
            
            // Non-resizable columns always have NoResize
            if (isNoResize)
            {
                colFlags |= ImGuiTableColumnFlags.NoResize;
            }
            // During init, temporarily apply NoResize to prevent ImGui's auto-fit queue
            // from overwriting our init widths
            else if (isInitializing)
            {
                colFlags |= ImGuiTableColumnFlags.NoResize;
            }
            
            ImGui.TableSetupColumn(column.Header, colFlags, column.Width);
        }
    }
    
    /// <summary>
    /// Sets up a single ImGui column with proper flags.
    /// Useful when the consumer builds columns one at a time (e.g., mixing fixed and data columns).
    /// </summary>
    /// <param name="header">Column header text.</param>
    /// <param name="width">Column width.</param>
    /// <param name="columnIndex">The column's logical index (for sort flag lookup).</param>
    /// <param name="sortColumns">Current sort column entries.</param>
    /// <param name="fallbackSortIndex">Fallback sort column index if sortColumns is empty.</param>
    /// <param name="fallbackSortAscending">Fallback sort ascending if sortColumns is empty.</param>
    /// <param name="preferSortDescending">Whether this column prefers descending sort on first click.</param>
    /// <param name="noResize">Whether this column should never be resizable.</param>
    /// <param name="stretch">Whether this column should stretch.</param>
    public void SetupColumn(
        string header,
        float width,
        int columnIndex,
        IReadOnlyList<SortColumnEntry> sortColumns,
        int fallbackSortIndex,
        bool fallbackSortAscending,
        bool preferSortDescending = true,
        bool noResize = false,
        bool stretch = false)
    {
        var sortColumnSet = BuildSortColumnSet(sortColumns, fallbackSortIndex, fallbackSortAscending);
        var isInitializing = IsInitializing;
        
        var colFlags = ImGuiTableColumnFlags.None;
        
        // Apply default sort to saved sort columns
        if (sortColumnSet.TryGetValue(columnIndex, out var ascending))
        {
            colFlags |= ascending 
                ? ImGuiTableColumnFlags.DefaultSort 
                : ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending;
        }
        else if (preferSortDescending)
        {
            colFlags |= ImGuiTableColumnFlags.PreferSortDescending;
        }
        
        // Apply width flags
        colFlags |= stretch ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed;
        
        // Apply resize flags
        if (noResize || _noResizeColumns.Contains(columnIndex))
        {
            colFlags |= ImGuiTableColumnFlags.NoResize;
        }
        else if (isInitializing)
        {
            colFlags |= ImGuiTableColumnFlags.NoResize;
        }
        
        ImGui.TableSetupColumn(header, colFlags, width);
    }
    
    #endregion
    
    #region Header Rendering
    
    /// <summary>
    /// Draws the header row with right-click detection, SHIFT+click/drag selection, and the context menu.
    /// </summary>
    /// <param name="columns">Column definitions.</param>
    /// <param name="headerHAlign">Header horizontal alignment.</param>
    /// <param name="headerVAlign">Header vertical alignment.</param>
    /// <param name="sortable">Whether sorting is enabled.</param>
    /// <param name="isShiftHeld">Whether SHIFT key is held.</param>
    /// <param name="isPopupOpen">Whether any popup is open.</param>
    /// <param name="mergedColumnGroups">Merged column groups for merge/unmerge context menu items.</param>
    /// <param name="hasContentWidthMeasurer">Whether data width measurement is available.</param>
    /// <param name="onSettingsChanged">Callback when settings change.</param>
    public void DrawHeaderRow(
        IReadOnlyList<TableColumn> columns,
        TableHorizontalAlignment headerHAlign,
        TableVerticalAlignment headerVAlign,
        bool sortable,
        bool isShiftHeld,
        bool isPopupOpen,
        List<MergedColumnGroupBase>? mergedColumnGroups,
        bool hasContentWidthMeasurer,
        Action? onSettingsChanged)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        
        var popupId = $"MTColCtx_{_tableId}";
        
        for (int i = 0; i < columns.Count; i++)
        {
            ImGui.TableNextColumn();
            var isNoResize = _noResizeColumns.Contains(i);
            
            // Handle SHIFT+click/drag selection for non-fixed columns
            var isColumnSelected = _selectedColumnIndices.Contains(i);
            if (!isNoResize && isShiftHeld && !isPopupOpen)
            {
                isColumnSelected = HandleShiftSelection(i, _selectedColumnIndices, ref _isSelectingColumns, ref _selectionStartColumn);
            }
            
            // Apply highlight background for selected headers
            if (isColumnSelected)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.8f, 0.4f)));
            }
            
            TableHelpers.DrawAlignedHeaderCell(
                columns[i].Header,
                headerHAlign,
                headerVAlign,
                sortable,
                out var rightClicked,
                columns[i].HeaderColor);
            
            if (isNoResize)
            {
                // For non-resizable columns, suppress the right-click with an empty popup
                if (rightClicked)
                    ImGui.OpenPopup($"MTNoResizeCtx_{_tableId}_{i}");
                if (ImGui.BeginPopup($"MTNoResizeCtx_{_tableId}_{i}"))
                    ImGui.EndPopup();
            }
            else if (rightClicked)
            {
                _contextMenuColumn = i;
                ImGui.OpenPopup(popupId);
            }
        }
        
        // Suppress ImGui's built-in header context menu that auto-sizes all columns
        if (ImGui.BeginPopup("##TableContextMenu"))
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        
        // Render the shared context menu
        DrawResizeContextMenu(popupId, columns, mergedColumnGroups, hasContentWidthMeasurer, onSettingsChanged);
    }
    
    /// <summary>
    /// Draws a single header cell with SHIFT+click/drag selection support.
    /// Use when building headers one at a time instead of via <see cref="DrawHeaderRow"/>.
    /// </summary>
    /// <param name="label">Header label text.</param>
    /// <param name="displayIndex">The display index (for selection tracking).</param>
    /// <param name="headerHAlign">Horizontal alignment.</param>
    /// <param name="headerVAlign">Vertical alignment.</param>
    /// <param name="sortable">Whether sorting is enabled.</param>
    /// <param name="isShiftHeld">Whether SHIFT is held.</param>
    /// <param name="isPopupOpen">Whether any popup is open.</param>
    /// <param name="rightClicked">True if right-clicked this frame.</param>
    /// <param name="headerColor">Optional header text color.</param>
    /// <returns>True if this column is currently selected.</returns>
    public bool DrawHeaderCell(
        string label,
        int displayIndex,
        TableHorizontalAlignment headerHAlign,
        TableVerticalAlignment headerVAlign,
        bool sortable,
        bool isShiftHeld,
        bool isPopupOpen,
        out bool rightClicked,
        Vector4? headerColor = null)
    {
        var isNoResize = _noResizeColumns.Contains(displayIndex);
        var isColumnSelected = _selectedColumnIndices.Contains(displayIndex);
        
        if (!isNoResize && isShiftHeld && !isPopupOpen)
        {
            isColumnSelected = HandleShiftSelection(displayIndex, _selectedColumnIndices, ref _isSelectingColumns, ref _selectionStartColumn);
        }
        
        if (isColumnSelected)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.8f, 0.4f)));
        }
        
        TableHelpers.DrawAlignedHeaderCell(label, headerHAlign, headerVAlign, sortable, out rightClicked, headerColor);
        
        return isColumnSelected;
    }
    
    #endregion
    
    #region Context Menu
    
    /// <summary>
    /// Draws the right-click context menu with resize and merge/unmerge options.
    /// </summary>
    private void DrawResizeContextMenu(
        string popupId,
        IReadOnlyList<TableColumn> columns,
        List<MergedColumnGroupBase>? mergedColumnGroups,
        bool hasContentWidthMeasurer,
        Action? onSettingsChanged)
    {
        if (!ImGui.BeginPopup(popupId))
            return;
        
        var ctxIdx = _contextMenuColumn;
        var ctxHeader = ctxIdx >= 0 && ctxIdx < columns.Count ? columns[ctxIdx].Header : "Column";
        
        ImGui.TextDisabled(ctxHeader);
        ImGui.Separator();
        
        if (ImGui.MenuItem("Resize to header width"))
        {
            _pendingResizeAction = MTColumnResizeAction.HeaderWidth;
            _resizeTargetColumn = ctxIdx;
        }
        if (hasContentWidthMeasurer && ImGui.MenuItem("Resize to data width"))
        {
            _pendingResizeAction = MTColumnResizeAction.DataWidth;
            _resizeTargetColumn = ctxIdx;
        }
        if (ImGui.MenuItem("Resize to fill space"))
        {
            _pendingResizeAction = MTColumnResizeAction.FillSpace;
            _resizeTargetColumn = ctxIdx;
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        
        if (ImGui.MenuItem("Resize all columns to header width"))
        {
            _pendingResizeAction = MTColumnResizeAction.HeaderWidth;
            _resizeTargetColumn = -1;
        }
        if (hasContentWidthMeasurer && ImGui.MenuItem("Resize all columns to data width"))
        {
            _pendingResizeAction = MTColumnResizeAction.DataWidth;
            _resizeTargetColumn = -1;
        }
        if (ImGui.MenuItem("Resize all columns to fill space"))
        {
            _pendingResizeAction = MTColumnResizeAction.FillSpace;
            _resizeTargetColumn = -1;
        }
        
        // Merge: show when 2+ columns are SHIFT-selected
        if (_selectedColumnIndices.Count >= 2 && mergedColumnGroups != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled($"{_selectedColumnIndices.Count} columns selected");
            
            if (ImGui.MenuItem("Merge Selected Columns"))
            {
                // Collect all source column indices, expanding any existing merged groups
                var allSourceIndices = new HashSet<int>();
                var groupsToRemove = new List<MergedColumnGroupBase>();
                
                foreach (var selIdx in _selectedColumnIndices)
                {
                    var existingGroup = mergedColumnGroups.FirstOrDefault(g => g.ColumnIndices.Contains(selIdx));
                    if (existingGroup != null)
                    {
                        foreach (var idx in existingGroup.ColumnIndices)
                            allSourceIndices.Add(idx);
                        if (!groupsToRemove.Contains(existingGroup))
                            groupsToRemove.Add(existingGroup);
                    }
                    else
                    {
                        allSourceIndices.Add(selIdx);
                    }
                }
                
                // Remove consumed groups
                foreach (var oldGroup in groupsToRemove)
                    mergedColumnGroups.Remove(oldGroup);
                
                // Create new merged group
                mergedColumnGroups.Add(new MergedColumnGroupBase
                {
                    Name = "Merged",
                    ColumnIndices = allSourceIndices.OrderBy(x => x).ToList(),
                    Width = 80f
                });
                
                _selectedColumnIndices.Clear();
                _skipNextClick = true;
                onSettingsChanged?.Invoke();
                OnMergeChanged?.Invoke();
            }
        }
        
        // Unmerge: show when the right-clicked column belongs to a merged group
        if (ctxIdx >= 0 && mergedColumnGroups != null)
        {
            var mergedGroup = mergedColumnGroups.FirstOrDefault(g => g.ColumnIndices.Contains(ctxIdx));
            if (mergedGroup != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                
                if (ImGui.MenuItem($"Unmerge \"{mergedGroup.Name}\""))
                {
                    mergedColumnGroups.Remove(mergedGroup);
                    _selectedColumnIndices.Clear();
                    _skipNextClick = true;
                    onSettingsChanged?.Invoke();
                    OnMergeChanged?.Invoke();
                }
            }
        }
        
        ImGui.EndPopup();
    }
    
    /// <summary>
    /// Sets the context menu column and requests the given popup to open.
    /// Call this when you detect a right-click on a data column header.
    /// </summary>
    /// <param name="columnIndex">The column index that was right-clicked.</param>
    /// <param name="popupId">The popup ID to open.</param>
    public void OpenContextMenu(int columnIndex, string popupId)
    {
        _contextMenuColumn = columnIndex;
        ImGui.OpenPopup(popupId);
    }
    
    #endregion
    
    #region Resize System
    
    /// <summary>
    /// Processes any pending resize action. Call at the start of each frame before BeginTable.
    /// Returns the pending action and target, then clears them.
    /// </summary>
    /// <param name="action">The pending resize action.</param>
    /// <param name="targetColumn">The target column index (-1 = all columns).</param>
    /// <returns>True if there was a pending resize action.</returns>
    public bool ConsumePendingResize(out MTColumnResizeAction action, out int targetColumn)
    {
        if (_pendingResizeAction == MTColumnResizeAction.None)
        {
            action = MTColumnResizeAction.None;
            targetColumn = -1;
            return false;
        }
        
        action = _pendingResizeAction;
        targetColumn = _resizeTargetColumn;
        _pendingResizeAction = MTColumnResizeAction.None;
        _resizeTargetColumn = -1;
        return true;
    }
    
    /// <summary>
    /// Queues a resize action for processing on the next frame.
    /// </summary>
    /// <param name="action">The resize action to perform.</param>
    /// <param name="targetColumn">The target column (-1 for all).</param>
    public void QueueResize(MTColumnResizeAction action, int targetColumn)
    {
        _pendingResizeAction = action;
        _resizeTargetColumn = targetColumn;
    }
    
    /// <summary>
    /// Processes a pending resize for a list of TableColumn definitions.
    /// Handles both single column and all-column resize operations.
    /// </summary>
    /// <typeparam name="TRow">Row type for data width measurement.</typeparam>
    /// <param name="columns">The column definitions to resize.</param>
    /// <param name="rows">The row data (needed for data width measurement).</param>
    /// <param name="contentWidthMeasurer">Optional delegate to measure content width per row/column.</param>
    /// <param name="onSettingsChanged">Callback when widths change.</param>
    public void HandlePendingResize<TRow>(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<TRow> rows,
        Func<TRow, int, float>? contentWidthMeasurer,
        Action? onSettingsChanged)
    {
        if (!ConsumePendingResize(out var action, out var targetCol))
            return;
        
        if (columns.Count == 0)
            return;
        
        var cellPadding = ImGui.GetStyle().CellPadding.X * 2;
        
        // Build list of resizable column indices
        var resizableColumns = new List<int>();
        for (int i = 0; i < columns.Count; i++)
        {
            if (!_noResizeColumns.Contains(i))
                resizableColumns.Add(i);
        }
        
        if (targetCol >= 0 && targetCol < columns.Count)
        {
            // Single column resize
            var newWidth = CalculateNewWidth(action, targetCol, columns, rows, resizableColumns, cellPadding, contentWidthMeasurer);
            if (newWidth > 0f)
                columns[targetCol].Width = Math.Max(30f, newWidth);
        }
        else if (targetCol == -1)
        {
            // All resizable columns
            if (action == MTColumnResizeAction.FillSpace)
            {
                float fixedWidth = 0f;
                foreach (var idx in _noResizeColumns)
                {
                    if (idx >= 0 && idx < columns.Count)
                        fixedWidth += columns[idx].Width;
                }
                
                var fillWidth = TableHelpers.CalculateFillWidthEqual(columns.Count, resizableColumns.Count, fixedWidth);
                foreach (var idx in resizableColumns)
                    columns[idx].Width = fillWidth;
            }
            else
            {
                foreach (var idx in resizableColumns)
                {
                    var newWidth = CalculateNewWidth(action, idx, columns, rows, resizableColumns, cellPadding, contentWidthMeasurer);
                    if (newWidth > 0f)
                        columns[idx].Width = Math.Max(30f, newWidth);
                }
            }
        }
        
        // Force fresh table ID so ImGui picks up new init widths
        ResetColumnWidthState();
        onSettingsChanged?.Invoke();
    }
    
    /// <summary>
    /// Calculates the new width for a column based on the resize action.
    /// </summary>
    private float CalculateNewWidth<TRow>(
        MTColumnResizeAction action,
        int columnIndex,
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<TRow> rows,
        List<int> resizableColumns,
        float cellPadding,
        Func<TRow, int, float>? contentWidthMeasurer)
    {
        return action switch
        {
            MTColumnResizeAction.HeaderWidth => ImGui.CalcTextSize(columns[columnIndex].Header).X + cellPadding + 4f,
            MTColumnResizeAction.DataWidth => CalculateMaxDataWidth(columnIndex, rows, contentWidthMeasurer) + cellPadding,
            MTColumnResizeAction.FillSpace => CalculateSingleFillWidth(columnIndex, columns, resizableColumns),
            _ => 0f
        };
    }
    
    /// <summary>
    /// Calculates the maximum content width across all rows for a column.
    /// </summary>
    private float CalculateMaxDataWidth<TRow>(int columnIndex, IReadOnlyList<TRow> rows, Func<TRow, int, float>? contentWidthMeasurer)
    {
        if (contentWidthMeasurer == null) return 30f;
        
        float maxWidth = 30f;
        foreach (var row in rows)
        {
            var width = contentWidthMeasurer(row, columnIndex);
            if (width > maxWidth)
                maxWidth = width;
        }
        return maxWidth;
    }
    
    /// <summary>
    /// Calculates fill width for a single column given all other column widths.
    /// </summary>
    private float CalculateSingleFillWidth(int targetIndex, IReadOnlyList<TableColumn> columns, List<int> resizableColumns)
    {
        float fixedWidth = 0f;
        foreach (var idx in _noResizeColumns)
        {
            if (idx >= 0 && idx < columns.Count)
                fixedWidth += columns[idx].Width;
        }
        
        float otherDataWidth = 0f;
        foreach (var idx in resizableColumns)
        {
            if (idx != targetIndex)
                otherDataWidth += columns[idx].Width;
        }
        
        return TableHelpers.CalculateFillWidthSingle(columns.Count, fixedWidth, otherDataWidth);
    }
    
    #endregion
    
    #region Column Width Capture
    
    /// <summary>
    /// Captures actual column widths from ImGui after the auto-fit queue settles (3 frames).
    /// </summary>
    /// <param name="columns">Column definitions whose Width property will be updated.</param>
    /// <param name="onSettingsChanged">Callback when widths change.</param>
    public void CaptureColumnWidths(IReadOnlyList<TableColumn> columns, Action? onSettingsChanged)
    {
        _columnWidthsInitFrames++;
        if (_columnWidthsInitFrames <= 3)
            return;
        
        var widthsChanged = false;
        
        for (int i = 0; i < columns.Count; i++)
        {
            ImGui.TableSetColumnIndex(i);
            var currentWidth = ImGui.GetContentRegionAvail().X;
            
            if (Math.Abs(currentWidth - columns[i].Width) > 1f)
            {
                columns[i].Width = currentWidth;
                widthsChanged = true;
            }
        }
        
        if (widthsChanged)
        {
            onSettingsChanged?.Invoke();
        }
    }
    
    /// <summary>
    /// Captures actual column widths with a column index offset (e.g., when there's a fixed character column at index 0).
    /// Calls the provided callback for each column with its display index and current width.
    /// </summary>
    /// <param name="columnCount">Number of columns to capture.</param>
    /// <param name="columnOffset">ImGui column index offset (e.g., 1 if there's a fixed column at 0).</param>
    /// <param name="widthSetter">Callback invoked for each column: (displayIndex, currentWidth) → returns true if width changed.</param>
    /// <param name="onSettingsChanged">Callback when any width changes.</param>
    public void CaptureColumnWidthsCustom(
        int columnCount,
        int columnOffset,
        Func<int, float, bool> widthSetter,
        Action? onSettingsChanged)
    {
        _columnWidthsInitFrames++;
        if (_columnWidthsInitFrames <= 3)
            return;
        
        var widthsChanged = false;
        
        for (int i = 0; i < columnCount; i++)
        {
            ImGui.TableSetColumnIndex(i + columnOffset);
            var currentWidth = ImGui.GetContentRegionAvail().X;
            
            if (widthSetter(i, currentWidth))
                widthsChanged = true;
        }
        
        if (widthsChanged)
        {
            onSettingsChanged?.Invoke();
        }
    }
    
    #endregion
    
    #region Sort Specs
    
    /// <summary>
    /// Reads ImGui's sort specifications and updates the settings accordingly.
    /// Returns the effective sort columns list for use in sorting.
    /// </summary>
    /// <param name="sortable">Whether sorting is enabled.</param>
    /// <param name="sortColumns">Current sort columns (will be updated if user changes sort).</param>
    /// <param name="sortColumnIndex">Legacy single sort column index (updated for compat).</param>
    /// <param name="sortAscending">Legacy single sort ascending (updated for compat).</param>
    /// <param name="onSortChanged">Called when the user changes sort.</param>
    /// <returns>The effective sort columns list (may be from SortColumns or legacy fallback).</returns>
    public List<SortColumnEntry> ReadSortSpecs(
        bool sortable,
        ref List<SortColumnEntry> sortColumns,
        ref int sortColumnIndex,
        ref bool sortAscending,
        Action? onSortChanged)
    {
        if (!sortable)
            return sortColumns.Count > 0 ? sortColumns : new List<SortColumnEntry> 
                { new() { ColumnIndex = sortColumnIndex, Ascending = sortAscending } };
        
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            if (_sortInitialized && sortSpecs.SpecsCount > 0)
            {
                var newSortColumns = new List<SortColumnEntry>();
                for (int i = 0; i < sortSpecs.SpecsCount; i++)
                {
                    var spec = sortSpecs.Specs[i];
                    newSortColumns.Add(new SortColumnEntry
                    {
                        ColumnIndex = spec.ColumnIndex,
                        Ascending = spec.SortDirection == ImGuiSortDirection.Ascending
                    });
                }
                sortColumns = newSortColumns;
                
                // Keep legacy fields in sync with primary sort
                if (newSortColumns.Count > 0)
                {
                    sortColumnIndex = newSortColumns[0].ColumnIndex;
                    sortAscending = newSortColumns[0].Ascending;
                }
                onSortChanged?.Invoke();
            }
            else if (_sortInitialized && sortSpecs.SpecsCount == 0)
            {
                // User cleared all sort columns (SortTristate)
                sortColumns = new List<SortColumnEntry>();
                onSortChanged?.Invoke();
            }
            _sortInitialized = true;
            sortSpecs.SpecsDirty = false;
        }
        
        return sortColumns.Count > 0
            ? sortColumns
            : new List<SortColumnEntry> { new() { ColumnIndex = sortColumnIndex, Ascending = sortAscending } };
    }
    
    #endregion
    
    #region Static Helpers
    
    /// <summary>
    /// Handles SHIFT+click/drag range selection for column or row headers.
    /// Returns the updated selection state for the current index.
    /// </summary>
    public static bool HandleShiftSelection(
        int currentIdx,
        HashSet<int> selectedIndices,
        ref bool isSelecting,
        ref int selectionStart)
    {
        var cellMin = ImGui.GetCursorScreenPos();
        var cellMax = new Vector2(cellMin.X + ImGui.GetContentRegionAvail().X, cellMin.Y + ImGui.GetTextLineHeightWithSpacing());
        var isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
        
        // Start selection on click
        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            isSelecting = true;
            selectionStart = currentIdx;
            selectedIndices.Clear();
            selectedIndices.Add(currentIdx);
        }
        
        // Extend selection while dragging
        if (isSelecting && ImGui.IsMouseDown(ImGuiMouseButton.Left) && isHovered)
        {
            var min = Math.Min(selectionStart, currentIdx);
            var max = Math.Max(selectionStart, currentIdx);
            selectedIndices.Clear();
            for (int i = min; i <= max; i++)
            {
                selectedIndices.Add(i);
            }
        }
        
        // End selection on mouse release
        if (isSelecting && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            isSelecting = false;
        }
        
        return selectedIndices.Contains(currentIdx);
    }
    
    /// <summary>
    /// Builds a sort column lookup dictionary from the sort columns list.
    /// </summary>
    private static Dictionary<int, bool> BuildSortColumnSet(
        IReadOnlyList<SortColumnEntry> sortColumns,
        int fallbackSortIndex,
        bool fallbackSortAscending)
    {
        var sortColumnSet = new Dictionary<int, bool>();
        if (sortColumns.Count > 0)
        {
            foreach (var sc in sortColumns)
                sortColumnSet[sc.ColumnIndex] = sc.Ascending;
        }
        else
        {
            sortColumnSet[fallbackSortIndex] = fallbackSortAscending;
        }
        return sortColumnSet;
    }
    
    #endregion
}
