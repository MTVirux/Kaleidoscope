using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;
using MTGui.Common;
using MTGui.Table;
using MTGui.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

public partial class ItemTableWidget
{
    
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
        
        // Handle selection state based on SHIFT key
        var isShiftHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        
        // Check if any popup is currently open (to avoid clearing selection when clicking menu items)
        var isPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
        
        // Skip click processing if we just handled a merge action
        if (_skipNextClick)
        {
            _skipNextClick = false;
        }
        // Clear selection when clicking without SHIFT (but not when a popup is open)
        // We keep selection when SHIFT is released so user can right-click to merge
        else if (!isShiftHeld && !isPopupOpen && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectedColumnIndices.Clear();
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
        
        // NoSavedSettings prevents ImGui from trying to use its internal ini persistence
        // (Dalamud doesn't support ImGui ini files for plugins - we save column widths ourselves)
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;
        
        if (!ImGui.BeginTable(_config.TableId, displayColumnCount, flags))
            return;
        
        try
        {
            // Setup columns - apply DefaultSort flag to the saved sort column
            // PreferSortDescending makes first click sort descending (more useful for quantities)
            // For the default sort column, only use PreferSortDescending if saved state is descending
            // This ensures the arrow direction matches the actual sort order on reload
            var sortColIdx = settings.SortColumnIndex;
            var savedIsDescending = !settings.SortAscending;
            
            var charFlags = ImGuiTableColumnFlags.PreferSortDescending;
            if (sortColIdx == 0)
            {
                charFlags = savedIsDescending 
                    ? ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending
                    : ImGuiTableColumnFlags.DefaultSort;
            }
            // When auto-sizing is enabled, use WidthFixed for character column so it only shrinks as a last resort
            if (settings.AutoSizeEqualColumns)
            {
                charFlags |= ImGuiTableColumnFlags.WidthFixed;
            }
            
            // Only setup character column if not hidden
            if (!hideCharColumn)
            {
                ImGui.TableSetupColumn("Character", charFlags, charColumnWidth);
            }
            
            // Setup display columns (includes merged columns)
            for (int i = 0; i < displayColumns.Count; i++)
            {
                var displayCol = displayColumns[i];
                var colFlags = ImGuiTableColumnFlags.PreferSortDescending;
                if (sortColIdx == i + 1)
                {
                    colFlags = savedIsDescending
                        ? ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending
                        : ImGuiTableColumnFlags.DefaultSort;
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
                    settings.Sortable);
            }
            
            // Data column headers (using display columns which include merged columns)
            for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
            {
                ImGui.TableNextColumn();
                var displayCol = displayColumns[dispIdx];
                
                // Handle header selection with SHIFT+click/drag
                // Check if this display column is selected
                var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                if (isShiftHeld && !isPopupOpen) // Allow selecting both merged and non-merged columns
                {
                    // Get the header bounds for hover/click detection
                    var cellMin = ImGui.GetCursorScreenPos();
                    var cellMax = new Vector2(cellMin.X + ImGui.GetContentRegionAvail().X, cellMin.Y + ImGui.GetTextLineHeightWithSpacing());
                    var isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                    
                    // Start selection on click
                    if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _isSelectingColumns = true;
                        _selectionStartDisplayColumn = dispIdx;
                        _selectedDisplayColumnIndices.Clear();
                        _selectedDisplayColumnIndices.Add(dispIdx);
                    }
                    
                    // Extend selection while dragging
                    if (_isSelectingColumns && ImGui.IsMouseDown(ImGuiMouseButton.Left) && isHovered)
                    {
                        var minCol = Math.Min(_selectionStartDisplayColumn, dispIdx);
                        var maxCol = Math.Max(_selectionStartDisplayColumn, dispIdx);
                        _selectedDisplayColumnIndices.Clear();
                        for (int col = minCol; col <= maxCol; col++)
                        {
                            _selectedDisplayColumnIndices.Add(col);
                        }
                    }
                    
                    // End selection on mouse release
                    if (_isSelectingColumns && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        _isSelectingColumns = false;
                    }
                    
                    isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                }
                
                // Apply highlight background for selected headers
                if (isColumnSelected)
                {
                    var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f);
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(highlightColor));
                }
                
                DrawAlignedHeaderCell(
                    displayCol.Header,
                    settings.HeaderHorizontalAlignment,
                    settings.HeaderVerticalAlignment,
                    hideCharColumn ? dispIdx : dispIdx + 1,
                    settings.Sortable);
            }
            
            // Right-click context menu for column merging (when display columns are selected)
            if (_selectedDisplayColumnIndices.Count >= 2)
            {
                // Create a unique popup ID for this widget instance
                var popupId = $"MergeColumnsPopup_{_config.TableId}";
                
                // Check if right-click happened anywhere in the table area
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                {
                    ImGui.OpenPopup(popupId);
                }
                
                if (ImGui.BeginPopup(popupId))
                {
                    ImGui.TextDisabled($"{_selectedDisplayColumnIndices.Count} columns selected");
                    ImGui.Separator();
                    
                    if (ImGui.MenuItem("Merge Selected Columns"))
                    {
                        // Collect all source column indices from selected display columns
                        // This flattens any existing merged groups
                        var allSourceIndices = new HashSet<int>();
                        var mergedGroupsToRemove = new List<MergedColumnGroup>();
                        
                        foreach (var dispIdx in _selectedDisplayColumnIndices)
                        {
                            if (dispIdx >= 0 && dispIdx < displayColumns.Count)
                            {
                                var displayCol = displayColumns[dispIdx];
                                foreach (var srcIdx in displayCol.SourceColumnIndices)
                                {
                                    allSourceIndices.Add(srcIdx);
                                }
                                
                                // Track merged groups that need to be removed
                                if (displayCol.IsMerged && displayCol.MergedGroup != null)
                                {
                                    mergedGroupsToRemove.Add(displayCol.MergedGroup);
                                }
                            }
                        }
                        
                        // Remove old merged groups that were consumed
                        foreach (var oldGroup in mergedGroupsToRemove)
                        {
                            settings.MergedColumnGroups.Remove(oldGroup);
                        }
                        
                        // Create a new merged column group with all source indices
                        var mergedGroup = new MergedColumnGroup
                        {
                            Name = "Merged",
                            ColumnIndices = allSourceIndices.OrderBy(x => x).ToList(),
                            Width = 80f
                        };
                        
                        settings.MergedColumnGroups.Add(mergedGroup);
                        _selectedDisplayColumnIndices.Clear();
                        _selectedColumnIndices.Clear();
                        _skipNextClick = true; // Prevent click from selecting underneath
                        _onSettingsChanged?.Invoke();
                    }
                    
                    ImGui.EndPopup();
                }
            }
            
            // Right-click context menu for row merging (when display rows are selected)
            if (_selectedDisplayRowIndices.Count >= 2)
            {
                var rowPopupId = $"MergeRowsPopup_{_config.TableId}";
                
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
                {
                    ImGui.OpenPopup(rowPopupId);
                }
                
                if (ImGui.BeginPopup(rowPopupId))
                {
                    ImGui.TextDisabled($"{_selectedDisplayRowIndices.Count} rows selected");
                    ImGui.Separator();
                    
                    if (ImGui.MenuItem("Merge Selected Rows"))
                    {
                        // Collect all source character IDs from selected display rows
                        // This flattens any existing merged groups
                        var allCharacterIds = new HashSet<ulong>();
                        var mergedGroupsToRemove = new List<MergedRowGroup>();
                        
                        foreach (var dispRowIdx in _selectedDisplayRowIndices)
                        {
                            if (dispRowIdx >= 0 && dispRowIdx < _cachedDisplayRows.Count)
                            {
                                var displayRow = _cachedDisplayRows[dispRowIdx];
                                foreach (var cid in displayRow.SourceCharacterIds)
                                {
                                    allCharacterIds.Add(cid);
                                }
                                
                                // Track merged groups that need to be removed
                                if (displayRow.IsMerged && displayRow.MergedGroup != null)
                                {
                                    mergedGroupsToRemove.Add(displayRow.MergedGroup);
                                }
                            }
                        }
                        
                        // Remove old merged groups that were consumed
                        foreach (var oldGroup in mergedGroupsToRemove)
                        {
                            settings.MergedRowGroups.Remove(oldGroup);
                        }
                        
                        // Create a new merged row group with all character IDs
                        var mergedGroup = new MergedRowGroup
                        {
                            Name = "Merged",
                            CharacterIds = allCharacterIds.OrderBy(x => x).ToList()
                        };
                        
                        settings.MergedRowGroups.Add(mergedGroup);
                        _selectedDisplayRowIndices.Clear();
                        _selectedRowIds.Clear();
                        _skipNextClick = true; // Prevent click from selecting row underneath
                        _onSettingsChanged?.Invoke();
                    }
                    
                    ImGui.EndPopup();
                }
            }
            
            if (settings.HeaderColor.HasValue)
            {
                ImGui.PopStyleColor();
            }
            
            // Handle sorting
            var sortedRows = GetSortedRows(rows, columns, settings);
            var numberFormat = settings.NumberFormat;
            
            // Filter out hidden characters
            var visibleRows = sortedRows.Where(r => !settings.HiddenCharacters.Contains(r.CharacterId)).ToList();
            
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
                    var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f);
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(highlightColor));
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
                    ImGui.PushID((int)primaryCid);
                    
                    // Handle row selection with SHIFT+click/drag on character column
                    // Allow selecting both merged and non-merged rows
                    if (isShiftHeld && !isPopupOpen)
                    {
                        var cellMin = ImGui.GetCursorScreenPos();
                        var cellMax = new Vector2(cellMin.X + ImGui.GetContentRegionAvail().X, cellMin.Y + ImGui.GetTextLineHeightWithSpacing());
                        var isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                        
                        // Start selection on click
                        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            _isSelectingRows = true;
                            _selectionStartDisplayRow = dispRowIdx;
                            _selectedDisplayRowIndices.Clear();
                            _selectedDisplayRowIndices.Add(dispRowIdx);
                        }
                        
                        // Extend selection while dragging
                        if (_isSelectingRows && ImGui.IsMouseDown(ImGuiMouseButton.Left) && isHovered)
                        {
                            var minIdx = Math.Min(_selectionStartDisplayRow, dispRowIdx);
                            var maxIdx = Math.Max(_selectionStartDisplayRow, dispRowIdx);
                            _selectedDisplayRowIndices.Clear();
                            for (int idx = minIdx; idx <= maxIdx; idx++)
                            {
                                _selectedDisplayRowIndices.Add(idx);
                            }
                        }
                        
                        // End selection on mouse release
                        if (_isSelectingRows && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            _isSelectingRows = false;
                        }
                        
                        isRowSelected = _selectedDisplayRowIndices.Contains(dispRowIdx);
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
                    if (showCharContextMenu && !dispRow.IsMerged && ImGui.BeginPopupContextItem($"CharContext_{primaryCid}"))
                    {
                        ImGui.TextDisabled(dispRow.Name);
                        ImGui.Separator();
                        
                        if (ImGui.MenuItem("Hide Character"))
                        {
                            settings.HiddenCharacters.Add(primaryCid);
                            _onSettingsChanged?.Invoke();
                        }
                        
                        ImGui.EndPopup();
                    }
                    
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
                    // Check if this display column is selected
                    var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                    if (isShiftHeld && !isPopupOpen) // Allow selecting both merged and non-merged columns
                    {
                        // Get the column bounds for hover/click detection
                        var cellMin = ImGui.GetCursorScreenPos();
                        var cellMax = new Vector2(cellMin.X + ImGui.GetContentRegionAvail().X, cellMin.Y + ImGui.GetTextLineHeightWithSpacing());
                        var isHovered = ImGui.IsMouseHoveringRect(cellMin, cellMax);
                        
                        // Start selection on click
                        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        {
                            _isSelectingColumns = true;
                            _selectionStartDisplayColumn = dispIdx;
                            _selectedDisplayColumnIndices.Clear();
                            _selectedDisplayColumnIndices.Add(dispIdx);
                        }
                        
                        // Extend selection while dragging
                        if (_isSelectingColumns && ImGui.IsMouseDown(ImGuiMouseButton.Left) && isHovered)
                        {
                            var minCol = Math.Min(_selectionStartDisplayColumn, dispIdx);
                            var maxCol = Math.Max(_selectionStartDisplayColumn, dispIdx);
                            _selectedDisplayColumnIndices.Clear();
                            for (int col = minCol; col <= maxCol; col++)
                            {
                                _selectedDisplayColumnIndices.Add(col);
                            }
                        }
                        
                        // End selection on mouse release
                        if (_isSelectingColumns && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            _isSelectingColumns = false;
                        }
                        
                        // Update selection status after potential changes
                        isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                    }
                    
                    // Apply inverted background color for selected columns
                    if (isColumnSelected)
                    {
                        // Use inverted/highlight color for selected cell background
                        var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f); // Yellow-ish highlight
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(highlightColor));
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
                if (settings.ShowRetainerBreakdown && !dispRow.IsMerged && dispRow.HasRetainerData)
                {
                    var primaryCidForExpand = dispRow.SourceCharacterIds.FirstOrDefault();
                    var isExpandedForSubRows = settings.GroupingMode == TableGroupingMode.Character
                        ? _expandedCharacterIds.Contains(primaryCidForExpand)
                        : _expandedGroupNames.Contains(dispRow.Name);
                    if (isExpandedForSubRows)
                    {
                        // Draw retainer rows (player inventory is shown in the main row when expanded)
                        var retainerList = dispRow.RetainerBreakdown!.ToList();
                        for (int retIdx = 0; retIdx < retainerList.Count; retIdx++)
                        {
                            var (retainerKey, retainerCounts) = retainerList[retIdx];
                            var isLastRetainer = retIdx == retainerList.Count - 1;
                            rowIndex++;
                            
                            ImGui.TableNextRow();
                            
                            // Slightly darker background for sub-rows
                            var subRowBgColor = new Vector4(0.15f, 0.15f, 0.2f, 0.5f);
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(subRowBgColor));
                            
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
                            
                            // Data columns for retainer
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
                }
            }
            
            // Total row (only show if there are multiple display rows and not in All mode)
            // In All mode, the single row already shows the total
            if (settings.ShowTotalRow && finalDisplayRows.Count > 1 && settings.GroupingMode != TableGroupingMode.All)
            {
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
                    
                    // Apply selection styling to total row as well
                    var isColumnSelected = _selectedDisplayColumnIndices.Contains(dispIdx);
                    if (isColumnSelected)
                    {
                        var highlightColor = new Vector4(0.8f, 0.8f, 0.2f, 0.5f);
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(highlightColor));
                    }
                    
                    // Use preferred item colors for total row as well
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
            
            // Capture and save column widths if user has resized them
            // Skip the first frame to avoid overwriting saved widths with defaults
            if (_columnWidthsInitialized)
            {
                var widthsChanged = false;
                
                // Get column widths by navigating to each column and reading content region
                // This works because we're still inside the table context
                
                // Check character column width (column 0) - only if not hidden
                if (!hideCharColumn)
                {
                    ImGui.TableSetColumnIndex(0);
                    var currentCharWidth = ImGui.GetContentRegionAvail().X;
                    // Add cell padding back to get the actual column width
                    currentCharWidth += ImGui.GetStyle().CellPadding.X * 2;
                    if (Math.Abs(currentCharWidth - settings.CharacterColumnWidth) > 1f)
                    {
                        settings.CharacterColumnWidth = currentCharWidth;
                        widthsChanged = true;
                    }
                }
                
                // Check display column widths (includes merged columns)
                var dataColOffset = hideCharColumn ? 0 : 1;
                for (int dispIdx = 0; dispIdx < displayColumns.Count; dispIdx++)
                {
                    ImGui.TableSetColumnIndex(dispIdx + dataColOffset);
                    var currentWidth = ImGui.GetContentRegionAvail().X;
                    currentWidth += ImGui.GetStyle().CellPadding.X * 2;
                    
                    var displayCol = displayColumns[dispIdx];
                    if (displayCol.IsMerged && displayCol.MergedGroup != null)
                    {
                        // Update merged group width
                        if (Math.Abs(currentWidth - displayCol.MergedGroup.Width) > 1f)
                        {
                            displayCol.MergedGroup.Width = currentWidth;
                            widthsChanged = true;
                        }
                    }
                    else if (!displayCol.IsMerged && displayCol.SourceColumnIndices.Count == 1)
                    {
                        // Update regular column width
                        var colIdx = displayCol.SourceColumnIndices[0];
                        if (colIdx >= 0 && colIdx < columns.Count && Math.Abs(currentWidth - columns[colIdx].Width) > 1f)
                        {
                            columns[colIdx].Width = currentWidth;
                            widthsChanged = true;
                        }
                    }
                }
                
                if (widthsChanged)
                {
                    _onSettingsChanged?.Invoke();
                }
            }
            else
            {
                _columnWidthsInitialized = true;
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }
    
}