using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets.Common;
using Kaleidoscope.Gui.Widgets.Table;
using Kaleidoscope.Gui.Widgets.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

public sealed partial class ItemTableWidget
{
    
    /// <summary>
    /// Shows a brief info notification to the user.
    /// </summary>
    private void ShowInfoNotification(string content)
    {
        _notificationManager?.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
        {
            Content = content,
            Type = NotificationType.Info,
            Minimized = true,
            InitialDuration = TimeSpan.FromSeconds(2),
        });
    }
    
    /// <summary>
    /// Draws the item table.
    /// </summary>
    /// <param name="data">The prepared table data to display.</param>
    /// <param name="settings">Optional settings override. If null, uses bound settings.</param>
    public void Draw(PreparedItemTableData? data, IItemTableWidgetSettings? settings = null)
    {
        settings ??= _boundSettings;
        if (settings == null || data == null)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }
        
        // Handle selection state based on modifier keys
        var isShiftHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        var isCtrlHeld = ImGui.GetIO().KeyCtrl;
        
        // Check if any popup is currently open (to avoid clearing selection when clicking menu items)
        var isPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
        
        // Skip click processing if we just handled a merge action
        if (_core.SkipNextClick)
        {
            _core.SkipNextClick = false;
        }
        // Clear selection when clicking without SHIFT (but not when a popup is open)
        // We keep selection when SHIFT is released so user can right-click to merge
        else if (!isShiftHeld && !isPopupOpen && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectedDisplayColumnIndices.Clear();
            _isSelectingColumns = false;
            _selectionStartDisplayColumn = -1;
            
            _selectedRowIds.Clear();
            _selectedDisplayRowIndices.Clear();
            _isSelectingRows = false;
            _selectionStartDisplayRow = -1;
        }
        
        // Cache rows for character name lookups in settings
        _cachedRows = data.Rows;
        
        var columns = data.Columns;
        if (columns.Count == 0)
        {
            ImGui.TextUnformatted("No columns configured. Add items or currencies in settings.");
            return;
        }
        
        var rows = data.Rows;
        if (rows.Count == 0)
        {
            ImGui.TextUnformatted(_config.NoDataText);
            return;
        }
        
        // Determine if we should hide the character column
        var hideCharColumn = settings.GroupingMode == TableGroupingMode.All && settings.HideCharacterColumnInAllMode;
        
        // Calculate character column width based on longest name if UseFullNameWidth is enabled
        var charColumnWidth = settings.CharacterColumnWidth;
        if (settings.UseFullNameWidth && rows.Count > 0)
        {
            var maxNameWidth = 0f;
            foreach (var row in rows)
            {
                var nameWidth = ImGui.CalcTextSize(row.Name).X;
                if (nameWidth > maxNameWidth)
                    maxNameWidth = nameWidth;
            }
            // Add padding for cell margins, borders, and extra safety margin
            maxNameWidth += ImGui.GetStyle().CellPadding.X;
            // Use the larger of calculated width or configured minimum
            charColumnWidth = Math.Max(charColumnWidth, maxNameWidth);
        }
        
        // Handle pending column resize actions from context menu
        HandlePendingColumnResize(columns, rows, settings, hideCharColumn, charColumnWidth);
        
        // Calculate equal width for data columns if AutoSizeEqualColumns is enabled
        float dataColumnWidth = 0f;
        if (settings.AutoSizeEqualColumns && columns.Count > 0)
        {
            // Get available width after accounting for character column and borders
            var availableWidth = ImGui.GetContentRegionAvail().X;
            // Subtract character column width and some margin for borders/scrollbar
            var remainingWidth = availableWidth - charColumnWidth - 20f;
            dataColumnWidth = Math.Max(50f, remainingWidth / columns.Count);
        }
        
        // Build display columns (handles merged columns)
        var displayColumns = BuildDisplayColumns(columns, settings, dataColumnWidth);
        _cachedDisplayColumns = displayColumns; // Cache for merge operations
        var displayColumnCount = hideCharColumn ? displayColumns.Count : 1 + displayColumns.Count;
        
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti | ImGuiTableFlags.SortTristate;
        
        // Use core's effective table ID (includes suffix for forced resets)
        var tableId = _core.GetEffectiveTableId();
        if (!ImGui.BeginTable(tableId, displayColumnCount, flags))
            return;
        
        try
        {
            // Setup columns - apply DefaultSort flag to saved sort columns
            // Build a lookup of sort column indices → ascending for multi-sort
            var sortColumnSet = new Dictionary<int, bool>(); // colIndex → ascending
            if (settings.SortColumns.Count > 0)
            {
                foreach (var sc in settings.SortColumns)
                    sortColumnSet[sc.ColumnIndex] = sc.Ascending;
            }
            else
            {
                sortColumnSet[settings.SortColumnIndex] = settings.SortAscending;
            }
            
            var charFlags = ImGuiTableColumnFlags.PreferSortDescending | ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize;
            if (sortColumnSet.TryGetValue(0, out var charAscending))
            {
                charFlags = ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize
                    | (!charAscending 
                        ? ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending
                        : ImGuiTableColumnFlags.DefaultSort);
            }
            
            // Only setup character column if not hidden
            if (!hideCharColumn)
            {
                ImGui.TableSetupColumn("Character", charFlags, charColumnWidth);
            }
            
            // During first 3 frames after table recreation, apply NoResize to prevent
            // ImGui's auto-fit queue from overwriting our init_widths.
            var isInitializing = _core.IsInitializing;
            for (int i = 0; i < displayColumns.Count; i++)
            {
                var displayCol = displayColumns[i];
                var colIdx = hideCharColumn ? i : i + 1;
                var colFlags = ImGuiTableColumnFlags.PreferSortDescending | ImGuiTableColumnFlags.WidthFixed;
                if (isInitializing)
                    colFlags |= ImGuiTableColumnFlags.NoResize;
                if (sortColumnSet.TryGetValue(colIdx, out var colAscending))
                {
                    colFlags = ImGuiTableColumnFlags.WidthFixed
                        | (!colAscending
                            ? ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending
                            : ImGuiTableColumnFlags.DefaultSort);
                    if (isInitializing)
                        colFlags |= ImGuiTableColumnFlags.NoResize;
                }
                ImGui.TableSetupColumn(displayCol.Header, colFlags, displayCol.Width);
            }
            ImGui.TableSetupScrollFreeze(0, 1);
            
            // Apply header color if set
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, settings.HeaderColor.Value);
            }
            
            // Draw custom header row with alignment support
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            
            // Character column header (only if not hidden)
            if (!hideCharColumn)
            {
                ImGui.TableNextColumn();
                
                DrawAlignedHeaderCell(
                    "Character",
                    settings.HeaderHorizontalAlignment,
                    settings.HeaderVerticalAlignment,
                    0,
                    settings.Sortable,
                    out var charRightClicked);
                
                // Override the built-in table context menu when right-clicking the character header.
                // The rightClicked flag covers the full header interaction area including resize grips.
                if (charRightClicked)
                    ImGui.OpenPopup($"CharHdrCtx_{_config.TableId}");
                if (ImGui.BeginPopup($"CharHdrCtx_{_config.TableId}"))
                    ImGui.EndPopup();
            }
            
            // Suppress ImGui's built-in header context menu that auto-sizes all columns
            if (ImGui.BeginPopup("##TableContextMenu"))
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            
            // Data column headers (using display columns which include merged columns)
            var headerPopupId = $"DataColCtx_{_config.TableId}";
            for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
            {
                ImGui.TableNextColumn();
                var displayCol = displayColumns[dispIdx];
                
                // Handle header selection with SHIFT+click/drag
                var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                if (isShiftHeld && !isPopupOpen)
                {
                    isColumnSelected = TableCore.HandleShiftSelection(dispIdx, _selectedDisplayColumnIndices, ref _isSelectingColumns, ref _selectionStartDisplayColumn);
                }
                
                // Apply highlight background for selected headers
                if (isColumnSelected)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(UiColors.SelectionHighlight));
                }
                
                DrawAlignedHeaderCell(
                    displayCol.Header,
                    settings.HeaderHorizontalAlignment,
                    settings.HeaderVerticalAlignment,
                    hideCharColumn ? dispIdx : dispIdx + 1,
                    settings.Sortable,
                    out var colRightClicked);
                
                // Override the built-in table context menu with our custom one.
                // The rightClicked flag covers the full header interaction area including resize grips.
                if (colRightClicked)
                {
                    _core.OpenContextMenu(dispIdx, headerPopupId);
                }
            }
            
            // Render the data column context menu (shared popup, content based on _core.ContextMenuColumn)
            DrawHeaderContextMenu(headerPopupId, displayColumns, settings);
            
            // Right-click context menu for row merging (when display rows are selected)
            DrawRowMergeContextMenu(settings);
            
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PopStyleColor();
            }
            
            // Handle sorting
            var sortedRows = GetSortedRows(rows, columns, displayColumns, hideCharColumn, settings);
            var numberFormat = settings.NumberFormat;
            
            // Filter out hidden characters (show them dimmed when CTRL is held)
            var revealedHiddenCids = new HashSet<ulong>();
            List<ItemTableCharacterRow> visibleRows;
            if (isCtrlHeld && settings.HiddenCharacters.Count > 0)
            {
                visibleRows = sortedRows.ToList();
                foreach (var cid in settings.HiddenCharacters)
                    revealedHiddenCids.Add(cid);
            }
            else
            {
                visibleRows = sortedRows.Where(r => !settings.HiddenCharacters.Contains(r.CharacterId)).ToList();
            }
            
            // Apply grouping if not in Character mode
            var groupedRows = ApplyGrouping(visibleRows, columns, settings.GroupingMode);
            
            // Build display rows (handles merged rows)
            var finalDisplayRows = BuildDisplayRows(groupedRows, settings, columns);
            
            // Filter out rows where all column values are zero if HideZeroRows is enabled
            if (settings.HideZeroRows)
            {
                finalDisplayRows = finalDisplayRows
                    .Where(r => r.ItemCounts.Values.Any(v => v != 0))
                    .ToList();
            }
            
            _cachedDisplayRows = finalDisplayRows; // Cache for merge operations
            
            // Track row order for range selection
            _currentRowOrder = finalDisplayRows
                .Where(r => !r.IsMerged)
                .SelectMany(r => r.SourceCharacterIds)
                .ToList();
            
            // Determine if we should show character context menu (only in Character mode)
            var showCharContextMenu = settings.GroupingMode == TableGroupingMode.Character;
            
            // Draw data rows
            int rowIndex = 0;
            for (int dispRowIdx = 0; dispRowIdx < finalDisplayRows.Count; dispRowIdx++)
            {
                var dispRow = finalDisplayRows[dispRowIdx];
                ImGui.TableNextRow();
                
                // Check if this display row is selected
                var isRowSelected = _selectedDisplayRowIndices.Contains(dispRowIdx);
                
                // Apply row background color based on even/odd or selection
                var isEven = rowIndex % 2 == 0;
                if (isRowSelected)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(UiColors.SelectionHighlight));
                }
                else if (isEven && settings.EvenRowColor.HasValue)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.EvenRowColor.Value));
                }
                else if (!isEven && settings.OddRowColor.HasValue)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(settings.OddRowColor.Value));
                }
                rowIndex++;
                
                // Character name with selection and context menu (only if not hidden)
                if (!hideCharColumn)
                {
                    ImGui.TableNextColumn();
                    var primaryCid = dispRow.SourceCharacterIds.FirstOrDefault();
                    var isRevealedHidden = revealedHiddenCids.Contains(primaryCid);
                    if (isRevealedHidden)
                        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.4f);
                    ImGui.PushID((int)primaryCid);
                    
                    // Handle row selection with SHIFT+click/drag on character column
                    if (isShiftHeld && !isPopupOpen)
                    {
                        isRowSelected = TableCore.HandleShiftSelection(dispRowIdx, _selectedDisplayRowIndices, ref _isSelectingRows, ref _selectionStartDisplayRow);
                    }
                    
                    // Determine text color - use preferred character colors if enabled
                    Vector4? nameColor = GetEffectiveCharacterColor(primaryCid, settings, dispRow.Color ?? settings.CharacterColumnColor);
                    if (isRowSelected)
                    {
                        var baseColor = nameColor ?? _config.DefaultTextColor;
                        nameColor = new Vector4(1f - baseColor.X, 1f - baseColor.Y, 1f - baseColor.Z, baseColor.W);
                    }
                    
                    // Check if this row has retainer breakdown data and if we should show it
                    var hasRetainerBreakdown = settings.ShowRetainerBreakdown && dispRow.HasRetainerData && !dispRow.IsMerged;
                    
                    if (hasRetainerBreakdown)
                    {
                        // Draw expandable tree node for characters with retainers
                        // Use character ID for Character mode, row name for grouped modes
                        var isCharacterMode = settings.GroupingMode == TableGroupingMode.Character;
                        var isExpanded = isCharacterMode 
                            ? _expandedCharacterIds.Contains(primaryCid)
                            : _expandedGroupNames.Contains(dispRow.Name);
                        
                        // Apply color if set
                        if (nameColor.HasValue)
                            ImGui.PushStyleColor(ImGuiCol.Text, nameColor.Value);
                        
                        // Use a simple arrow + text approach for better table compatibility
                        var arrowText = isExpanded ? "▼ " : "▶ ";
                        var clicked = ImGui.Selectable($"{arrowText}{dispRow.Name}", false, ImGuiSelectableFlags.SpanAllColumns);
                        
                        if (nameColor.HasValue)
                            ImGui.PopStyleColor();
                        
                        if (clicked)
                        {
                            if (isCharacterMode)
                            {
                                if (isExpanded)
                                    _expandedCharacterIds.Remove(primaryCid);
                                else
                                    _expandedCharacterIds.Add(primaryCid);
                            }
                            else
                            {
                                if (isExpanded)
                                    _expandedGroupNames.Remove(dispRow.Name);
                                else
                                    _expandedGroupNames.Add(dispRow.Name);
                            }
                        }
                    }
                    else
                    {
                        DrawAlignedCellText(
                            dispRow.Name, 
                            nameColor, 
                            settings.CharacterColumnHorizontalAlignment, 
                            settings.CharacterColumnVerticalAlignment);
                    }
                    
                    // Right-click context menu on character name (only in Character mode for non-merged rows)
                    if (isRevealedHidden)
                        ImGui.PopStyleVar();
                    DrawCharacterContextMenu(dispRow, primaryCid, isRevealedHidden, showCharContextMenu, settings);
                    
                    ImGui.PopID();
                }
                
                // Data columns (using display columns which include merged columns)
                // Check if this row is expanded to show retainer breakdown - if so, show player inventory only in main row
                var isExpandedForBreakdown = settings.ShowRetainerBreakdown && !dispRow.IsMerged && dispRow.HasRetainerData 
                    && (settings.GroupingMode == TableGroupingMode.Character 
                        ? _expandedCharacterIds.Contains(dispRow.SourceCharacterIds.FirstOrDefault())
                        : _expandedGroupNames.Contains(dispRow.Name));
                
                for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
                {
                    ImGui.TableNextColumn();
                    var displayCol = displayColumns[dispIdx];
                    
                    // When expanded with retainer breakdown, show player inventory in main row
                    // Otherwise show the combined total
                    var value = (isExpandedForBreakdown && dispRow.PlayerItemCounts != null)
                        ? GetDisplayValueFromCounts(displayCol, dispRow.PlayerItemCounts, columns)
                        : GetDisplayValue(displayCol, dispRow, columns);
                    
                    // Handle column selection with SHIFT+click/drag
                    var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                    if (isShiftHeld && !isPopupOpen)
                    {
                        isColumnSelected = TableCore.HandleShiftSelection(dispIdx, _selectedDisplayColumnIndices, ref _isSelectingColumns, ref _selectionStartDisplayColumn);
                    }
                    
                    // Apply inverted background color for selected columns
                    if (isColumnSelected)
                    {
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(UiColors.SelectionHighlight));
                    }
                    
                    // Determine text color - use preferred item colors if enabled, then invert if selected
                    var sourceColIdx = displayCol.SourceColumnIndices.FirstOrDefault(-1);
                    var sourceCol = sourceColIdx >= 0 && sourceColIdx < columns.Count ? columns[sourceColIdx] : null;
                    Vector4? textColor = GetEffectiveColumnColor(sourceCol, displayCol, settings, columns);
                    if (isColumnSelected)
                    {
                        // Invert the color for selected columns
                        var baseColor = textColor ?? _config.DefaultTextColor;
                        textColor = new Vector4(1f - baseColor.X, 1f - baseColor.Y, 1f - baseColor.Z, baseColor.W);
                    }
                    
                    DrawAlignedCellText(
                        FormatNumber(value, numberFormat), 
                        textColor, 
                        settings.HorizontalAlignment, 
                        settings.VerticalAlignment);
                }
                
                // Draw retainer sub-rows if this character is expanded
                DrawRetainerSubRows(dispRow, displayColumns, columns, settings, hideCharColumn, numberFormat, ref rowIndex);
            }
            
            // Total row
            DrawTotalRow(finalDisplayRows, displayColumns, columns, settings, hideCharColumn, numberFormat);
            
            // Capture column widths
            CaptureDisplayColumnWidths(displayColumns, columns, settings, hideCharColumn);
        }
        finally
        {
            ImGui.EndTable();
        }
    }
    
    #region Extracted Draw Helpers
    
    /// <summary>
    /// Handles pending column resize actions queued from the header context menu.
    /// </summary>
    private void HandlePendingColumnResize(
        IReadOnlyList<ItemColumnConfig> columns,
        IReadOnlyList<ItemTableCharacterRow> rows,
        IItemTableWidgetSettings settings,
        bool hideCharColumn,
        float charColumnWidth)
    {
        if (!_core.HasPendingResize || columns.Count == 0)
            return;
        
        _core.ConsumePendingResize(out var action, out var targetDispCol);
        
        var preDisplayColumns = BuildDisplayColumns(columns, settings, 0f);
        var cellPadding = ImGui.GetStyle().CellPadding.X * 2;
        
        if (targetDispCol >= 0 && targetDispCol < preDisplayColumns.Count)
        {
            var dispCol = preDisplayColumns[targetDispCol];
            float newWidth = action switch
            {
                MTColumnResizeAction.HeaderWidth => ImGui.CalcTextSize(dispCol.Header).X + cellPadding + 4f,
                MTColumnResizeAction.DataWidth => CalculateMaxDataWidth(dispCol, rows, columns, settings) + cellPadding,
                MTColumnResizeAction.FillSpace => CalculateFillWidth(preDisplayColumns, targetDispCol, hideCharColumn, charColumnWidth),
                _ => 0f
            };
            if (newWidth > 0f)
            {
                ApplyColumnWidth(dispCol, columns, Math.Max(30f, newWidth));
                _onSettingsChanged?.Invoke();
            }
        }
        else if (targetDispCol == -1)
        {
            foreach (var dispCol in preDisplayColumns)
            {
                float newWidth = action switch
                {
                    MTColumnResizeAction.HeaderWidth => ImGui.CalcTextSize(dispCol.Header).X + cellPadding + 4f,
                    MTColumnResizeAction.DataWidth => CalculateMaxDataWidth(dispCol, rows, columns, settings) + cellPadding,
                    _ => 0f
                };
                if (newWidth > 0f)
                    ApplyColumnWidth(dispCol, columns, Math.Max(30f, newWidth));
            }
            if (action == MTColumnResizeAction.FillSpace)
            {
                var effectiveCharWidth = hideCharColumn ? 0f : charColumnWidth;
                var totalCols = hideCharColumn ? preDisplayColumns.Count : preDisplayColumns.Count + 1;
                var fillWidth = TableHelpers.CalculateFillWidthEqual(totalCols, preDisplayColumns.Count, effectiveCharWidth);
                foreach (var dispCol in preDisplayColumns)
                    ApplyColumnWidth(dispCol, columns, fillWidth);
            }
            _onSettingsChanged?.Invoke();
        }
        
        _core.ResetColumnWidthState();
    }
    
    /// <summary>
    /// Draws the header column context menu (resize options + merge columns).
    /// </summary>
    private void DrawHeaderContextMenu(
        string headerPopupId,
        List<DisplayColumn> displayColumns,
        IItemTableWidgetSettings settings)
    {
        if (!ImGui.BeginPopup(headerPopupId))
            return;
        
        var ctxDispIdx = _core.ContextMenuColumn;
        var ctxHeader = ctxDispIdx >= 0 && ctxDispIdx < displayColumns.Count
            ? displayColumns[ctxDispIdx].Header
            : "Column";
        
        ImGui.TextDisabled(ctxHeader);
        ImGui.Separator();
        
        if (ImGui.MenuItem("Resize to header width"))
            _core.QueueResize(MTColumnResizeAction.HeaderWidth, ctxDispIdx);
        if (ImGui.MenuItem("Resize to data width"))
            _core.QueueResize(MTColumnResizeAction.DataWidth, ctxDispIdx);
        if (ImGui.MenuItem("Resize to fill space"))
            _core.QueueResize(MTColumnResizeAction.FillSpace, ctxDispIdx);
        
        ImGui.Spacing();
        ImGui.Separator();
        
        if (ImGui.MenuItem("Resize all data columns to header width"))
            _core.QueueResize(MTColumnResizeAction.HeaderWidth, -1);
        if (ImGui.MenuItem("Resize all data columns to data width"))
            _core.QueueResize(MTColumnResizeAction.DataWidth, -1);
        if (ImGui.MenuItem("Resize all data columns to fill space"))
            _core.QueueResize(MTColumnResizeAction.FillSpace, -1);
        
        if (_selectedDisplayColumnIndices.Count >= 2)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled($"{_selectedDisplayColumnIndices.Count} columns selected");
            
            if (ImGui.MenuItem("Merge Selected Columns"))
            {
                var allSourceIndices = new HashSet<int>();
                var mergedGroupsToRemove = new List<MergedColumnGroup>();
                
                foreach (var selIdx in _selectedDisplayColumnIndices)
                {
                    if (selIdx >= 0 && selIdx < displayColumns.Count)
                    {
                        var selCol = displayColumns[selIdx];
                        foreach (var srcIdx in selCol.SourceColumnIndices)
                            allSourceIndices.Add(srcIdx);
                        if (selCol.IsMerged && selCol.MergedGroup != null)
                            mergedGroupsToRemove.Add(selCol.MergedGroup);
                    }
                }
                
                foreach (var oldGroup in mergedGroupsToRemove)
                    settings.MergedColumnGroups.Remove(oldGroup);
                
                settings.MergedColumnGroups.Add(new MergedColumnGroup
                {
                    Name = "Merged",
                    ColumnIndices = allSourceIndices.OrderBy(x => x).ToList(),
                    Width = 80f
                });
                _selectedDisplayColumnIndices.Clear();
                _core.SkipNextClick = true;
                _onSettingsChanged?.Invoke();
            }
        }
        
        ImGui.EndPopup();
    }
    
    /// <summary>
    /// Draws the row merge context menu when multiple display rows are selected.
    /// </summary>
    private void DrawRowMergeContextMenu(IItemTableWidgetSettings settings)
    {
        if (_selectedDisplayRowIndices.Count < 2)
            return;
        
        var rowPopupId = $"MergeRowsPopup_{_config.TableId}";
        
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            ImGui.OpenPopup(rowPopupId);
        
        if (!ImGui.BeginPopup(rowPopupId))
            return;
        
        ImGui.TextDisabled($"{_selectedDisplayRowIndices.Count} rows selected");
        ImGui.Separator();
        
        if (ImGui.MenuItem("Merge Selected Rows"))
        {
            var allCharacterIds = new HashSet<ulong>();
            var mergedGroupsToRemove = new List<MergedRowGroup>();
            
            foreach (var dispRowIdx in _selectedDisplayRowIndices)
            {
                if (dispRowIdx >= 0 && dispRowIdx < _cachedDisplayRows.Count)
                {
                    var displayRow = _cachedDisplayRows[dispRowIdx];
                    foreach (var cid in displayRow.SourceCharacterIds)
                        allCharacterIds.Add(cid);
                    if (displayRow.IsMerged && displayRow.MergedGroup != null)
                        mergedGroupsToRemove.Add(displayRow.MergedGroup);
                }
            }
            
            foreach (var oldGroup in mergedGroupsToRemove)
                settings.MergedRowGroups.Remove(oldGroup);
            
            settings.MergedRowGroups.Add(new MergedRowGroup
            {
                Name = "Merged",
                CharacterIds = allCharacterIds.OrderBy(x => x).ToList()
            });
            _selectedDisplayRowIndices.Clear();
            _selectedRowIds.Clear();
            _core.SkipNextClick = true;
            _onSettingsChanged?.Invoke();
        }
        
        ImGui.EndPopup();
    }
    
    /// <summary>
    /// Draws the right-click context menu for a character name cell.
    /// </summary>
    private void DrawCharacterContextMenu(
        DisplayRow dispRow,
        ulong primaryCid,
        bool isRevealedHidden,
        bool showCharContextMenu,
        IItemTableWidgetSettings settings)
    {
        if (!showCharContextMenu || dispRow.IsMerged)
            return;
        if (!ImGui.BeginPopupContextItem($"CharContext_{primaryCid}"))
            return;
        
        if (ImGui.Selectable(dispRow.Name))
        {
            ImGui.SetClipboardText(dispRow.Name);
            ShowInfoNotification($"Copied \"{dispRow.Name}\" to clipboard.");
        }
        ImGui.Separator();
        
        // Relog to character via Lifestream
        if (_lifestreamService != null && _lifestreamService.IsAvailable)
        {
            var currentCid = GameStateService.PlayerContentId;
            if (primaryCid != 0 && primaryCid != currentCid)
            {
                var worldName = _cachedRows?
                    .FirstOrDefault(r => r.CharacterId == primaryCid)?.WorldName;
                
                if (!string.IsNullOrEmpty(worldName))
                {
                    var gameName = _cachedRows?
                        .FirstOrDefault(r => r.CharacterId == primaryCid)?.GameName;
                    var nameForRelog = !string.IsNullOrEmpty(gameName) ? gameName : dispRow.Name;
                    
                    if (ImGui.MenuItem("Relog to Character"))
                    {
                        _lifestreamService.ChangeCharacter(nameForRelog, worldName);
                        ShowInfoNotification($"Relogging to {nameForRelog} ({worldName})...");
                    }
                }
            }
        }
        
        if (isRevealedHidden)
        {
            if (ImGui.MenuItem("Unhide Character"))
            {
                settings.HiddenCharacters.Remove(primaryCid);
                _onSettingsChanged?.Invoke();
                ShowInfoNotification($"\"{dispRow.Name}\" is no longer hidden.");
            }
        }
        else
        {
            if (ImGui.MenuItem("Hide Character"))
            {
                settings.HiddenCharacters.Add(primaryCid);
                _onSettingsChanged?.Invoke();
                ShowInfoNotification($"\"{dispRow.Name}\" is now hidden.");
            }
        }
        
        ImGui.EndPopup();
    }
    
    /// <summary>
    /// Draws retainer sub-rows for an expanded character row.
    /// </summary>
    private void DrawRetainerSubRows(
        DisplayRow dispRow,
        List<DisplayColumn> displayColumns,
        IReadOnlyList<ItemColumnConfig> columns,
        IItemTableWidgetSettings settings,
        bool hideCharColumn,
        NumberFormatConfig numberFormat,
        ref int rowIndex)
    {
        if (!settings.ShowRetainerBreakdown || dispRow.IsMerged || !dispRow.HasRetainerData)
            return;
        
        var primaryCidForExpand = dispRow.SourceCharacterIds.FirstOrDefault();
        var isExpanded = settings.GroupingMode == TableGroupingMode.Character
            ? _expandedCharacterIds.Contains(primaryCidForExpand)
            : _expandedGroupNames.Contains(dispRow.Name);
        if (!isExpanded)
            return;
        
        var retainerList = dispRow.RetainerBreakdown!.ToList();
        for (int retIdx = 0; retIdx < retainerList.Count; retIdx++)
        {
            var (retainerKey, retainerCounts) = retainerList[retIdx];
            var isLastRetainer = retIdx == retainerList.Count - 1;
            rowIndex++;
            
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(UiColors.SubRowBackground));
            
            if (!hideCharColumn)
            {
                ImGui.TableNextColumn();
                ImGui.Indent(16f);
                var prefix = isLastRetainer ? "└ " : "├ ";
                DrawAlignedCellText(
                    $"{prefix}{retainerKey.Name}",
                    UiColors.Info,
                    settings.CharacterColumnHorizontalAlignment,
                    settings.CharacterColumnVerticalAlignment);
                ImGui.Unindent(16f);
            }
            
            for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
            {
                ImGui.TableNextColumn();
                var displayCol = displayColumns[dispIdx];
                var subValue = GetDisplayValueFromCounts(displayCol, retainerCounts, columns);
                
                var subSourceColIdx = displayCol.SourceColumnIndices.FirstOrDefault(-1);
                var subSourceCol = subSourceColIdx >= 0 && subSourceColIdx < columns.Count ? columns[subSourceColIdx] : null;
                Vector4? subTextColor = GetEffectiveColumnColor(subSourceCol, displayCol, settings, columns);
                
                DrawAlignedCellText(
                    FormatNumber(subValue, numberFormat),
                    subTextColor,
                    settings.HorizontalAlignment,
                    settings.VerticalAlignment);
            }
        }
    }
    
    /// <summary>
    /// Draws the total row at the bottom of the table.
    /// </summary>
    private void DrawTotalRow(
        List<DisplayRow> finalDisplayRows,
        List<DisplayColumn> displayColumns,
        IReadOnlyList<ItemColumnConfig> columns,
        IItemTableWidgetSettings settings,
        bool hideCharColumn,
        NumberFormatConfig numberFormat)
    {
        if (!settings.ShowTotalRow || finalDisplayRows.Count <= 1 || settings.GroupingMode == TableGroupingMode.All)
            return;
        
        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(_config.TotalRowColor));
        
        if (!hideCharColumn)
        {
            ImGui.TableNextColumn();
            DrawAlignedCellText(
                "TOTAL",
                null,
                settings.CharacterColumnHorizontalAlignment,
                settings.CharacterColumnVerticalAlignment);
        }
        
        for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
        {
            ImGui.TableNextColumn();
            var displayCol = displayColumns[dispIdx];
            var sum = finalDisplayRows.Sum(r => GetDisplayValue(displayCol, r, columns));
            
            var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
            if (isColumnSelected)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(UiColors.SelectionHighlight));
            
            var totalSourceColIdx = displayCol.SourceColumnIndices.FirstOrDefault(-1);
            var totalSourceCol = totalSourceColIdx >= 0 && totalSourceColIdx < columns.Count ? columns[totalSourceColIdx] : null;
            Vector4? textColor = GetEffectiveColumnColor(totalSourceCol, displayCol, settings, columns);
            if (isColumnSelected)
            {
                var baseColor = textColor ?? _config.DefaultTextColor;
                textColor = new Vector4(1f - baseColor.X, 1f - baseColor.Y, 1f - baseColor.Z, baseColor.W);
            }
            
            DrawAlignedCellText(
                FormatNumber(sum, numberFormat),
                textColor,
                settings.HorizontalAlignment,
                settings.VerticalAlignment);
        }
    }
    
    /// <summary>
    /// Captures actual column widths from ImGui after the init settling period.
    /// </summary>
    private void CaptureDisplayColumnWidths(
        List<DisplayColumn> displayColumns,
        IReadOnlyList<ItemColumnConfig> columns,
        IItemTableWidgetSettings settings,
        bool hideCharColumn)
    {
        _core.TickInitFrame();
        if (_core.IsInitializing)
            return;
        
        var widthsChanged = false;
        
        if (!hideCharColumn)
        {
            ImGui.TableSetColumnIndex(0);
            var currentCharWidth = ImGui.GetContentRegionAvail().X;
            if (Math.Abs(currentCharWidth - settings.CharacterColumnWidth) > 1f)
            {
                settings.CharacterColumnWidth = currentCharWidth;
                widthsChanged = true;
            }
        }
        
        var dataColOffset = hideCharColumn ? 0 : 1;
        for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
        {
            ImGui.TableSetColumnIndex(dispIdx + dataColOffset);
            var currentWidth = ImGui.GetContentRegionAvail().X;
            
            var displayCol = displayColumns[dispIdx];
            if (displayCol.IsMerged && displayCol.MergedGroup != null)
            {
                if (Math.Abs(currentWidth - displayCol.MergedGroup.Width) > 1f)
                {
                    displayCol.MergedGroup.Width = currentWidth;
                    widthsChanged = true;
                }
            }
            else if (!displayCol.IsMerged && displayCol.SourceColumnIndices.Count == 1)
            {
                var colIdx = displayCol.SourceColumnIndices[0];
                if (colIdx >= 0 && colIdx < columns.Count && Math.Abs(currentWidth - columns[colIdx].Width) > 1f)
                {
                    columns[colIdx].Width = currentWidth;
                    widthsChanged = true;
                }
            }
        }
        
        if (widthsChanged)
            _onSettingsChanged?.Invoke();
    }
    
    #endregion
    
}