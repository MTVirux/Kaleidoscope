using System.Numerics;
using Dalamud.Bindings.ImGui;
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
    /// Merges values from a source dictionary into a target dictionary using additive semantics.
    /// If a key already exists in target, its value is summed with the source value.
    /// </summary>
    private static void MergeDictionaryAdditive<TKey>(Dictionary<TKey, long> target, IReadOnlyDictionary<TKey, long> source) where TKey : notnull
    {
        foreach (var kvp in source)
        {
            if (target.TryGetValue(kvp.Key, out var existing))
                target[kvp.Key] = existing + kvp.Value;
            else
                target[kvp.Key] = kvp.Value;
        }
    }
    
    /// <summary>
    /// Aggregates PlayerItemCounts and RetainerBreakdown from multiple source rows into the target row.
    /// </summary>
    private static void AggregateRowData(ItemTableCharacterRow target, IEnumerable<ItemTableCharacterRow> sourceRows)
    {
        var aggregatedPlayerCounts = new Dictionary<uint, long>();
        var aggregatedRetainerBreakdown = new Dictionary<(ulong RetainerId, string Name), Dictionary<uint, long>>();
        
        foreach (var sourceRow in sourceRows)
        {
            if (sourceRow.PlayerItemCounts != null)
                MergeDictionaryAdditive(aggregatedPlayerCounts, sourceRow.PlayerItemCounts);
            
            if (sourceRow.RetainerBreakdown != null)
            {
                foreach (var (retainerKey, counts) in sourceRow.RetainerBreakdown)
                {
                    if (!aggregatedRetainerBreakdown.TryGetValue(retainerKey, out var retainerCounts))
                    {
                        retainerCounts = new Dictionary<uint, long>();
                        aggregatedRetainerBreakdown[retainerKey] = retainerCounts;
                    }
                    MergeDictionaryAdditive(retainerCounts, counts);
                }
            }
        }
        
        if (aggregatedPlayerCounts.Count > 0)
            target.PlayerItemCounts = aggregatedPlayerCounts;
        if (aggregatedRetainerBreakdown.Count > 0)
            target.RetainerBreakdown = aggregatedRetainerBreakdown;
    }
    
    private List<ItemTableCharacterRow> GetSortedRows(
        IReadOnlyList<ItemTableCharacterRow> rows,
        IReadOnlyList<ItemColumnConfig> columns,
        IReadOnlyList<DisplayColumn> displayColumns,
        bool hideCharColumn,
        IItemTableWidgetSettings settings)
    {
        if (!settings.Sortable)
            return rows.ToList(); // Preserve order from caller (already sorted by config)
        
        // Check for sort specs - update settings when user clicks a column header
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            if (sortSpecs.SpecsCount > 0)
            {
                // Read all sort specs for multi-column sorting
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
                settings.SortColumns = newSortColumns;

                // Keep legacy fields in sync with primary sort
                if (newSortColumns.Count > 0)
                {
                    settings.SortColumnIndex = newSortColumns[0].ColumnIndex;
                    settings.SortAscending = newSortColumns[0].Ascending;
                }
                _onSettingsChanged?.Invoke();
            }
            else
            {
                // User cleared all sort columns (SortTristate)
                settings.SortColumns = new List<SortColumnEntry>();
                _onSettingsChanged?.Invoke();
            }
            sortSpecs.SpecsDirty = false;
        }
        
        // Build sort columns list (prefer SortColumns, fall back to legacy fields)
        var sortColumns = settings.SortColumns.Count > 0
            ? settings.SortColumns
            : new List<SortColumnEntry> { new() { ColumnIndex = settings.SortColumnIndex, Ascending = settings.SortAscending } };
        
        // If no sort columns at all, preserve caller order
        if (sortColumns.Count == 0)
            return rows.ToList();
        
        // Sort the rows with multi-column support (left-to-right priority)
        var sorted = rows.ToList();
        sorted.Sort((a, b) =>
        {
            foreach (var sc in sortColumns)
            {
                var sortColumnIndex = sc.ColumnIndex;
                var sortAscending = sc.Ascending;
                
                // When the character column is hidden, ImGui column 0 is the first data column.
                // When visible, ImGui column 0 is the character column and data columns start at 1.
                var isCharacterSort = hideCharColumn ? false : sortColumnIndex == 0;
                var displayColIdx = hideCharColumn ? sortColumnIndex : sortColumnIndex - 1;
                
                int result;
                if (isCharacterSort)
                {
                    // Sort by character name
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (!sortAscending) result = -result;
                }
                else if (displayColIdx >= 0 && displayColIdx < displayColumns.Count)
                {
                    var displayCol = displayColumns[displayColIdx];
                    var valA = GetDisplayColumnValue(displayCol, a, columns);
                    var valB = GetDisplayColumnValue(displayCol, b, columns);
                    result = valA.CompareTo(valB);
                    if (!sortAscending) result = -result;
                }
                else
                {
                    result = 0;
                }
                
                if (result != 0) return result;
            }
            return 0;
        });
        
        return sorted;
    }
    
    /// <summary>
    /// Applies grouping to the rows based on the selected grouping mode.
    /// </summary>
    private static List<ItemTableCharacterRow> ApplyGrouping(
        IReadOnlyList<ItemTableCharacterRow> rows,
        IReadOnlyList<ItemColumnConfig> columns,
        TableGroupingMode mode)
    {
        if (mode == TableGroupingMode.Character || rows.Count == 0)
        {
            // No grouping - return as-is
            return rows.ToList();
        }
        
        if (mode == TableGroupingMode.All)
        {
            // Combine all rows into a single aggregate row
            var aggregateRow = new ItemTableCharacterRow
            {
                CharacterId = 0,
                Name = "All Characters",
                WorldName = string.Empty,
                DataCenterName = string.Empty,
                RegionName = string.Empty,
                ItemCounts = new Dictionary<uint, long>()
            };
            
            foreach (var column in columns)
            {
                var sum = rows.Sum(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0);
                aggregateRow.ItemCounts[column.Id] = sum;
            }
            
            // Aggregate PlayerItemCounts and RetainerBreakdown from all source rows
            AggregateRowData(aggregateRow, rows);
            
            return new List<ItemTableCharacterRow> { aggregateRow };
        }
        
        // Group by the selected field
        Func<ItemTableCharacterRow, string> keySelector = mode switch
        {
            TableGroupingMode.World => r => string.IsNullOrEmpty(r.WorldName) ? "Unknown World" : r.WorldName,
            TableGroupingMode.DataCenter => r => string.IsNullOrEmpty(r.DataCenterName) ? "Unknown DC" : r.DataCenterName,
            TableGroupingMode.Region => r => string.IsNullOrEmpty(r.RegionName) ? "Unknown Region" : r.RegionName,
            _ => r => r.Name
        };
        
        var grouped = rows.GroupBy(keySelector);
        var result = new List<ItemTableCharacterRow>();
        
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            var aggregateRow = new ItemTableCharacterRow
            {
                // Use 0 as character ID for grouped rows (no single character)
                CharacterId = 0,
                Name = group.Key,
                WorldName = mode == TableGroupingMode.World ? group.Key : group.First().WorldName,
                DataCenterName = mode == TableGroupingMode.DataCenter ? group.Key : group.First().DataCenterName,
                RegionName = mode == TableGroupingMode.Region ? group.Key : group.First().RegionName,
                ItemCounts = new Dictionary<uint, long>()
            };
            
            foreach (var column in columns)
            {
                var sum = group.Sum(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0);
                aggregateRow.ItemCounts[column.Id] = sum;
            }
            
            // Aggregate PlayerItemCounts and RetainerBreakdown from all source rows in this group
            AggregateRowData(aggregateRow, group);
            
            result.Add(aggregateRow);
        }
        
        return result;
    }
    
    private static string FormatNumber(long value, NumberFormatConfig? config) => TableHelpers.FormatNumber(value, config);
    
    /// <summary>
    /// Draws text in a table cell with the specified alignment.
    /// </summary>
    private static void DrawAlignedCellText(
        string text, 
        Vector4? color, 
        TableHorizontalAlignment hAlign, 
        TableVerticalAlignment vAlign) => TableHelpers.DrawAlignedCellText(text, hAlign, vAlign, color);
    
    /// <summary>
    /// Draws a header cell with alignment and sorting support.
    /// </summary>
    private static void DrawAlignedHeaderCell(
        string label,
        TableHorizontalAlignment hAlign,
        TableVerticalAlignment vAlign,
        int columnIndex,
        bool sortable,
        out bool rightClicked) => TableHelpers.DrawAlignedHeaderCell(label, hAlign, vAlign, sortable, out rightClicked);
    
    /// <summary>
    /// Calculates the maximum data cell text width for a display column across all rows.
    /// </summary>
    private static float CalculateMaxDataWidth(
        DisplayColumn dispCol,
        IReadOnlyList<ItemTableCharacterRow> rows,
        IReadOnlyList<ItemColumnConfig> columns,
        IItemTableWidgetSettings settings)
    {
        var numberFormat = settings.NumberFormat;
        float maxWidth = 30f; // Minimum sensible width
        foreach (var row in rows)
        {
            var value = GetDisplayColumnValue(dispCol, row, columns);
            var text = FormatNumber(value, numberFormat);
            var textWidth = ImGui.CalcTextSize(text).X;
            if (textWidth > maxWidth)
                maxWidth = textWidth;
        }
        return maxWidth;
    }
    
    /// <summary>
    /// Calculates the width needed for a single column to fill the remaining table space.
    /// </summary>
    private static float CalculateFillWidth(
        List<DisplayColumn> displayColumns,
        int targetDispIdx,
        bool hideCharColumn,
        float charColumnWidth)
    {
        var effectiveCharWidth = hideCharColumn ? 0f : charColumnWidth;
        var totalCols = hideCharColumn ? displayColumns.Count : displayColumns.Count + 1;
        
        // Sum up widths of all other display columns
        float otherColumnsWidth = 0f;
        for (int i = 0; i < displayColumns.Count; i++)
        {
            if (i != targetDispIdx)
                otherColumnsWidth += displayColumns[i].Width;
        }
        
        return TableHelpers.CalculateFillWidthSingle(totalCols, effectiveCharWidth, otherColumnsWidth);
    }
    
    /// <summary>
    /// Applies a new width to a display column's underlying config (regular column or merged group).
    /// </summary>
    private static void ApplyColumnWidth(DisplayColumn dispCol, IReadOnlyList<ItemColumnConfig> columns, float newWidth)
    {
        if (dispCol.IsMerged && dispCol.MergedGroup != null)
        {
            dispCol.MergedGroup.Width = newWidth;
        }
        else if (!dispCol.IsMerged && dispCol.SourceColumnIndices.Count == 1)
        {
            var colIdx = dispCol.SourceColumnIndices[0];
            if (colIdx >= 0 && colIdx < columns.Count)
                columns[colIdx].Width = newWidth;
        }
    }
    
}