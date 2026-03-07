using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Table;

/// <summary>
/// A generic, reusable table widget with customizable columns, sorting, styling,
/// and a built-in header right-click context menu for column resizing.
/// Cell content rendering is delegated to the caller via delegates.
/// </summary>
/// <typeparam name="TRow">The type of data for each row.</typeparam>
public class TableWidget<TRow>
{
    /// <summary>
    /// Shared default settings instance to avoid allocating per frame when no settings are bound.
    /// </summary>
    private static readonly MTTableSettings DefaultSettings = new();
    
    private readonly string _tableId;
    private readonly string _noDataText;
    
    // Settings binding
    private ITableSettings? _boundSettings;
    private Action? _onSettingsChanged;
    private string _settingsName = "Table Settings";
    
    // Optional content width measurer for "resize to data width"
    private Func<TRow, int, float>? _contentWidthMeasurer;
    
    // Sort state tracking
    private bool _sortInitialized = false;
    
    // Cached sort results to avoid per-frame ToList() allocation
    private List<TRow>? _cachedSortedRows;
    private IReadOnlyList<TRow>? _lastInputRows;
    private int _lastSortColumn = -1;
    private bool _lastSortAscending = true;
    private bool _sortDirty = true;
    
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
    
    /// <summary>
    /// Raised when column merge groups change (merge or unmerge action).
    /// Subscribers can use this to refresh display column lists.
    /// </summary>
    public event Action? OnMergeChanged;
    
    /// <summary>
    /// Gets whether this widget has bound settings.
    /// </summary>
    public bool HasSettings => _boundSettings != null;
    
    /// <summary>
    /// Gets the display name for settings.
    /// </summary>
    public string SettingsName => _settingsName;
    
    /// <summary>
    /// Delegate for rendering a cell's content.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="context">The cell render context with row/column indices.</param>
    public delegate void CellRenderer(TRow row, CellRenderContext context);
    
    /// <summary>
    /// Delegate for getting a sortable value from a row for a specific column.
    /// Return IComparable (string, int, float, DateTime, etc.) for sorting.
    /// </summary>
    /// <param name="row">The row data.</param>
    /// <param name="columnIndex">The column index.</param>
    /// <returns>A comparable value for sorting, or null if not sortable.</returns>
    public delegate IComparable? SortKeySelector(TRow row, int columnIndex);
    
    /// <summary>
    /// Creates a new TableWidget.
    /// </summary>
    /// <param name="tableId">Unique ID for ImGui table identification.</param>
    /// <param name="noDataText">Text to display when there is no data.</param>
    public TableWidget(string tableId, string noDataText = "No data available.")
    {
        _tableId = tableId;
        _noDataText = noDataText;
    }
    
    /// <summary>
    /// Binds this widget to a settings object for automatic synchronization.
    /// </summary>
    /// <param name="settings">The settings object implementing ITableSettings.</param>
    /// <param name="onSettingsChanged">Callback when settings are changed (e.g., to trigger config save).</param>
    /// <param name="settingsName">Display name for the settings section.</param>
    public void BindSettings(
        ITableSettings settings,
        Action? onSettingsChanged = null,
        string settingsName = "Table Settings")
    {
        _boundSettings = settings;
        _onSettingsChanged = onSettingsChanged;
        _settingsName = settingsName;
    }
    
    /// <summary>
    /// Sets a delegate that measures the rendered content width for a given row and column.
    /// This enables the "Resize to data width" context menu option.
    /// The delegate should return the pixel width of the cell content (text, icons, etc.).
    /// If not set, the "Resize to data width" options will not appear.
    /// </summary>
    /// <param name="measurer">Function taking (row, columnIndex) and returning content width in pixels.</param>
    /// <returns>This widget for fluent chaining.</returns>
    public TableWidget<TRow> WithContentWidthMeasurer(Func<TRow, int, float> measurer)
    {
        _contentWidthMeasurer = measurer;
        return this;
    }
    
    /// <summary>
    /// Marks a column as non-resizable. Non-resizable columns won't show the resize context menu
    /// on right-click and their width is excluded from fill calculations. Use this for fixed-width
    /// label columns (e.g., a "Character" column) that should not participate in resize operations.
    /// </summary>
    /// <param name="columnIndex">The 0-based column index to mark as non-resizable.</param>
    /// <returns>This widget for fluent chaining.</returns>
    public TableWidget<TRow> WithNoResizeColumn(int columnIndex)
    {
        _noResizeColumns.Add(columnIndex);
        return this;
    }
    
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
    /// Draws the table with built-in header context menu for column resizing.
    /// </summary>
    /// <param name="columns">Column definitions. Widths will be updated when user resizes.</param>
    /// <param name="rows">Row data.</param>
    /// <param name="cellRenderer">Delegate to render each cell's content.</param>
    /// <param name="sortKeySelector">Optional delegate to get sort keys. If null, sorting uses row order.</param>
    /// <param name="settings">Optional settings override. If null, uses bound settings.</param>
    /// <param name="height">Optional explicit height. If 0, uses available height.</param>
    public void Draw(
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<TRow> rows,
        CellRenderer cellRenderer,
        SortKeySelector? sortKeySelector = null,
        ITableSettings? settings = null,
        float height = 0f)
    {
        settings ??= _boundSettings ?? DefaultSettings;
        
        if (columns.Count == 0)
        {
            ImGui.TextUnformatted("No columns defined.");
            return;
        }
        
        if (rows.Count == 0)
        {
            ImGui.TextUnformatted(_noDataText);
            return;
        }
        
        // Handle pending column resize actions from the context menu (deferred to next frame)
        HandlePendingResize(columns, rows);
        
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;
        
        var tableHeight = height > 0 ? height : ImGui.GetContentRegionAvail().Y;
        
        // Append _tableIdSuffix to force ImGui to reset column state after resize actions
        var tableId = _tableIdSuffix > 0 ? $"{_tableId}_{_tableIdSuffix}" : _tableId;
        if (!ImGui.BeginTable(tableId, columns.Count, flags, new Vector2(0, tableHeight)))
            return;
        
        try
        {
            // Determine if we're in the init settling period (first 3 frames after table recreation)
            var isInitializing = _columnWidthsInitFrames <= 3;
            
            // Setup columns with WidthFixed + temporary NoResize during init
            SetupColumns(columns, settings, isInitializing);
            
            if (settings.FreezeHeader)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
            }
            
            // Apply header color if set
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, settings.HeaderColor.Value);
            }
            
            // Draw header row with right-click detection
            DrawHeaderRow(columns, settings);
            
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PopStyleColor();
            }
            
            // Handle sorting
            var sortedRows = GetSortedRows(rows, sortKeySelector, settings);
            
            // Draw data rows
            for (int rowIdx = 0; rowIdx < sortedRows.Count; rowIdx++)
            {
                var row = sortedRows[rowIdx];
                ImGui.TableNextRow();
                
                // Apply row background color based on even/odd
                var isEven = rowIdx % 2 == 0;
                if (settings.UseAlternatingRowColors)
                {
                    if (isEven && settings.EvenRowColor.HasValue)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.EvenRowColor.Value));
                    }
                    else if (!isEven && settings.OddRowColor.HasValue)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.OddRowColor.Value));
                    }
                }
                
                // Render each cell
                for (int colIdx = 0; colIdx < columns.Count; colIdx++)
                {
                    ImGui.TableNextColumn();
                    
                    // Highlight selected columns in data cells too
                    if (_selectedColumnIndices.Contains(colIdx))
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.8f, 0.4f)));
                    }
                    
                    var context = new CellRenderContext
                    {
                        RowIndex = rowIdx,
                        ColumnIndex = colIdx,
                        Settings = settings
                    };
                    
                    cellRenderer(row, context);
                }
            }
            
            // Capture column widths after ImGui's auto-fit queue settles (3 frames)
            CaptureColumnWidths(columns);
        }
        finally
        {
            ImGui.EndTable();
        }
    }
    
    #region Column Resize System
    
    /// <summary>
    /// Sets up ImGui columns with proper flags, including temporary NoResize during init.
    /// </summary>
    private void SetupColumns(IReadOnlyList<TableColumn> columns, ITableSettings settings, bool isInitializing)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var colFlags = column.Flags;
            var isNoResize = _noResizeColumns.Contains(i);
            
            // Apply default sort to saved column
            if (i == settings.SortColumnIndex)
            {
                colFlags |= settings.SortAscending 
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
    /// Draws the header row with right-click detection, SHIFT+click/drag selection, and the context menu.
    /// </summary>
    private void DrawHeaderRow(IReadOnlyList<TableColumn> columns, ITableSettings settings)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        
        var popupId = $"MTColCtx_{_tableId}";
        var isShiftHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        var isPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
        
        // Handle selection clearing — clear when clicking without SHIFT (but not when popup is open)
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
                settings.HeaderHorizontalAlignment,
                settings.HeaderVerticalAlignment,
                settings.Sortable,
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
        DrawResizeContextMenu(popupId, columns, settings);
    }
    
    /// <summary>
    /// Draws the right-click context menu with resize and merge/unmerge options.
    /// </summary>
    private void DrawResizeContextMenu(string popupId, IReadOnlyList<TableColumn> columns, ITableSettings settings)
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
        if (_contentWidthMeasurer != null && ImGui.MenuItem("Resize to data width"))
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
        if (_contentWidthMeasurer != null && ImGui.MenuItem("Resize all columns to data width"))
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
        if (_selectedColumnIndices.Count >= 2)
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
                    // Check if this column is already part of a merged group
                    var existingGroup = settings.MergedColumnGroups.FirstOrDefault(g => g.ColumnIndices.Contains(selIdx));
                    if (existingGroup != null)
                    {
                        // Absorb all indices from the existing group
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
                    settings.MergedColumnGroups.Remove(oldGroup);
                
                // Create new merged group
                settings.MergedColumnGroups.Add(new MergedColumnGroupBase
                {
                    Name = "Merged",
                    ColumnIndices = allSourceIndices.OrderBy(x => x).ToList(),
                    Width = 80f
                });
                
                _selectedColumnIndices.Clear();
                _skipNextClick = true;
                _onSettingsChanged?.Invoke();
                OnMergeChanged?.Invoke();
            }
        }
        
        // Unmerge: show when the right-clicked column belongs to a merged group
        if (ctxIdx >= 0)
        {
            var mergedGroup = settings.MergedColumnGroups.FirstOrDefault(g => g.ColumnIndices.Contains(ctxIdx));
            if (mergedGroup != null)
            {
                ImGui.Spacing();
                ImGui.Separator();
                
                if (ImGui.MenuItem($"Unmerge \"{mergedGroup.Name}\""))
                {
                    settings.MergedColumnGroups.Remove(mergedGroup);
                    _selectedColumnIndices.Clear();
                    _skipNextClick = true;
                    _onSettingsChanged?.Invoke();
                    OnMergeChanged?.Invoke();
                }
            }
        }
        
        ImGui.EndPopup();
    }
    
    /// <summary>
    /// Handles SHIFT+click/drag range selection for column or row headers.
    /// Returns the updated selection state for the current index.
    /// Can be called by external table implementations that need the same selection behavior.
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
    /// Processes any pending resize action from the context menu.
    /// </summary>
    private void HandlePendingResize(IReadOnlyList<TableColumn> columns, IReadOnlyList<TRow> rows)
    {
        if (_pendingResizeAction == MTColumnResizeAction.None || columns.Count == 0)
            return;
        
        var action = _pendingResizeAction;
        var targetCol = _resizeTargetColumn;
        _pendingResizeAction = MTColumnResizeAction.None;
        _resizeTargetColumn = -1;
        
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
            var newWidth = CalculateNewWidth(action, targetCol, columns, rows, resizableColumns, cellPadding);
            if (newWidth > 0f)
                columns[targetCol].Width = Math.Max(30f, newWidth);
        }
        else if (targetCol == -1)
        {
            // All resizable columns
            if (action == MTColumnResizeAction.FillSpace)
            {
                // Calculate fixed columns total width
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
                    var newWidth = CalculateNewWidth(action, idx, columns, rows, resizableColumns, cellPadding);
                    if (newWidth > 0f)
                        columns[idx].Width = Math.Max(30f, newWidth);
                }
            }
        }
        
        // Force fresh table ID so ImGui picks up new init widths
        _columnWidthsInitFrames = 0;
        _tableIdSuffix++;
        _onSettingsChanged?.Invoke();
    }
    
    /// <summary>
    /// Calculates the new width for a column based on the resize action.
    /// </summary>
    private float CalculateNewWidth(
        MTColumnResizeAction action,
        int columnIndex,
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<TRow> rows,
        List<int> resizableColumns,
        float cellPadding)
    {
        return action switch
        {
            MTColumnResizeAction.HeaderWidth => ImGui.CalcTextSize(columns[columnIndex].Header).X + cellPadding + 4f,
            MTColumnResizeAction.DataWidth => CalculateMaxDataWidth(columnIndex, rows) + cellPadding,
            MTColumnResizeAction.FillSpace => CalculateSingleFillWidth(columnIndex, columns, resizableColumns),
            _ => 0f
        };
    }
    
    /// <summary>
    /// Calculates the maximum content width across all rows for a column.
    /// </summary>
    private float CalculateMaxDataWidth(int columnIndex, IReadOnlyList<TRow> rows)
    {
        if (_contentWidthMeasurer == null) return 30f;
        
        float maxWidth = 30f;
        foreach (var row in rows)
        {
            var width = _contentWidthMeasurer(row, columnIndex);
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
    
    /// <summary>
    /// Captures actual column widths from ImGui after the auto-fit queue settles.
    /// </summary>
    private void CaptureColumnWidths(IReadOnlyList<TableColumn> columns)
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
            _onSettingsChanged?.Invoke();
        }
    }
    
    #endregion
    
    #region Sorting
    
    private List<TRow> GetSortedRows(
        IReadOnlyList<TRow> rows,
        SortKeySelector? sortKeySelector,
        ITableSettings settings)
    {
        // Detect if input data reference changed
        if (!ReferenceEquals(rows, _lastInputRows))
        {
            _lastInputRows = rows;
            _sortDirty = true;
        }
        
        if (!settings.Sortable || sortKeySelector == null)
        {
            if (_sortDirty || _cachedSortedRows == null)
            {
                _cachedSortedRows = rows.ToList();
                _sortDirty = false;
            }
            return _cachedSortedRows;
        }
        
        // Check for sort specs - update settings when user changes sort
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            if (_sortInitialized && sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                settings.SortColumnIndex = spec.ColumnIndex;
                settings.SortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
                _onSettingsChanged?.Invoke();
            }
            _sortInitialized = true;
            sortSpecs.SpecsDirty = false;
            _sortDirty = true;
        }
        
        var sortColumnIndex = settings.SortColumnIndex;
        var sortAscending = settings.SortAscending;
        
        // Check if sort parameters changed
        if (sortColumnIndex != _lastSortColumn || sortAscending != _lastSortAscending)
        {
            _lastSortColumn = sortColumnIndex;
            _lastSortAscending = sortAscending;
            _sortDirty = true;
        }
        
        // Return cached result if nothing changed
        if (!_sortDirty && _cachedSortedRows != null)
            return _cachedSortedRows;
        
        // Sort the rows using the sort key selector
        var sorted = rows.ToList();
        sorted.Sort((a, b) =>
        {
            var keyA = sortKeySelector(a, sortColumnIndex);
            var keyB = sortKeySelector(b, sortColumnIndex);
            
            if (keyA == null && keyB == null) return 0;
            if (keyA == null) return sortAscending ? -1 : 1;
            if (keyB == null) return sortAscending ? 1 : -1;
            
            var result = keyA.CompareTo(keyB);
            return sortAscending ? result : -result;
        });
        
        _cachedSortedRows = sorted;
        _sortDirty = false;
        return sorted;
    }
    
    #endregion
    
    #region Settings UI
    
    /// <summary>
    /// Draws the settings UI for this table widget.
    /// </summary>
    /// <returns>True if any setting was changed.</returns>
    public bool DrawSettings()
    {
        if (_boundSettings == null) return false;
        
        var changed = false;
        var settings = _boundSettings;
        
        // Table options
        var sortable = settings.Sortable;
        if (ImGui.Checkbox("Enable sorting", ref sortable))
        {
            settings.Sortable = sortable;
            changed = true;
        }
        
        var freezeHeader = settings.FreezeHeader;
        if (ImGui.Checkbox("Freeze header row", ref freezeHeader))
        {
            settings.FreezeHeader = freezeHeader;
            changed = true;
        }
        
        var useAlternatingColors = settings.UseAlternatingRowColors;
        if (ImGui.Checkbox("Use alternating row colors", ref useAlternatingColors))
        {
            settings.UseAlternatingRowColors = useAlternatingColors;
            changed = true;
        }
        
        ImGui.Spacing();
        if (TreeHelpers.DrawSection("Data Column Alignment", true))
        {
            // Data horizontal alignment
            var hAlign = (int)settings.DataHorizontalAlignment;
            if (ImGui.Combo("Data Horizontal", ref hAlign, "Left\0Center\0Right\0"))
            {
                settings.DataHorizontalAlignment = (TableHorizontalAlignment)hAlign;
                changed = true;
            }
        
            // Data vertical alignment
            var vAlign = (int)settings.DataVerticalAlignment;
            if (ImGui.Combo("Data Vertical", ref vAlign, "Top\0Center\0Bottom\0"))
            {
                settings.DataVerticalAlignment = (TableVerticalAlignment)vAlign;
                changed = true;
            }
            TreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (TreeHelpers.DrawSection("Header Row Alignment"))
        {
            // Header horizontal alignment
            var headerHAlign = (int)settings.HeaderHorizontalAlignment;
            if (ImGui.Combo("Header Horizontal", ref headerHAlign, "Left\0Center\0Right\0"))
            {
                settings.HeaderHorizontalAlignment = (TableHorizontalAlignment)headerHAlign;
                changed = true;
            }
        
            // Header vertical alignment
            var headerVAlign = (int)settings.HeaderVerticalAlignment;
            if (ImGui.Combo("Header Vertical", ref headerVAlign, "Top\0Center\0Bottom\0"))
            {
                settings.HeaderVerticalAlignment = (TableVerticalAlignment)headerVAlign;
                changed = true;
            }
            TreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (TreeHelpers.DrawSection("Row Colors"))
        {
            // Header color
            changed |= TableHelpers.DrawColorOption("Header", settings.HeaderColor, c => settings.HeaderColor = c);
        
            // Even row color
            changed |= TableHelpers.DrawColorOption("Even Rows", settings.EvenRowColor, c => settings.EvenRowColor = c);
        
            // Odd row color
            changed |= TableHelpers.DrawColorOption("Odd Rows", settings.OddRowColor, c => settings.OddRowColor = c);
            TreeHelpers.EndSection();
        }
        
        if (changed)
        {
            _onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    #endregion
    
    #region Helper Methods for Cell Rendering
    
    /// <summary>
    /// Helper method to draw text with alignment in a cell.
    /// Call this from your cell renderer delegate for aligned text.
    /// </summary>
    public static void DrawAlignedText(
        string text,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        Vector4? color = null)
    {
        TableHelpers.DrawAlignedCellText(text, hAlign, vAlign, color);
    }
    
    /// <summary>
    /// Helper method to draw text using settings alignment.
    /// </summary>
    public static void DrawAlignedText(string text, ITableSettings settings, Vector4? color = null)
    {
        TableHelpers.DrawAlignedCellText(text, settings.DataHorizontalAlignment, settings.DataVerticalAlignment, color);
    }
    
    #endregion
}
