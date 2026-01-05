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
    
    private List<ItemTableCharacterRow> GetSortedRows(
        IReadOnlyList<ItemTableCharacterRow> rows,
        IReadOnlyList<ItemColumnConfig> columns,
        IItemTableWidgetSettings settings)
    {
        if (!settings.Sortable)
            return rows.ToList(); // Preserve order from caller (already sorted by config)
        
        // Check for sort specs - update settings when user changes sort
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            // On first frame, SpecsDirty is true but we should use saved settings
            // Only save if this isn't the initial sort setup
            if (_sortInitialized && sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                settings.SortColumnIndex = spec.ColumnIndex;
                settings.SortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
                _onSettingsChanged?.Invoke();
            }
            _sortInitialized = true;
            sortSpecs.SpecsDirty = false;
        }
        
        var sortColumnIndex = settings.SortColumnIndex;
        var sortAscending = settings.SortAscending;
        
        // Sort the rows
        IEnumerable<ItemTableCharacterRow> sorted;
        if (sortColumnIndex == 0)
        {
            // Sort by character name column - preserve the order from caller (already sorted by config)
            // If descending, reverse the pre-sorted order to maintain AR/alphabetical order in reverse
            sorted = sortAscending 
                ? rows // Preserve order from caller (already sorted by CharacterSortHelper)
                : rows.Reverse(); // Reverse the configured order (could be AR order, alphabetical, etc.)
        }
        else if (sortColumnIndex > 0 && sortColumnIndex <= columns.Count)
        {
            // Sort by data column
            var column = columns[sortColumnIndex - 1];
            sorted = sortAscending
                ? rows.OrderBy(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0)
                : rows.OrderByDescending(r => r.ItemCounts.TryGetValue(column.Id, out var c) ? c : 0);
        }
        else
        {
            sorted = rows; // Preserve order from caller
        }
        
        return sorted.ToList();
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
            
            // Aggregate PlayerItemCounts from all source rows
            var aggregatedPlayerCounts = new Dictionary<uint, long>();
            foreach (var sourceRow in rows)
            {
                if (sourceRow.PlayerItemCounts != null)
                {
                    foreach (var kvp in sourceRow.PlayerItemCounts)
                    {
                        if (aggregatedPlayerCounts.TryGetValue(kvp.Key, out var existing))
                            aggregatedPlayerCounts[kvp.Key] = existing + kvp.Value;
                        else
                            aggregatedPlayerCounts[kvp.Key] = kvp.Value;
                    }
                }
            }
            if (aggregatedPlayerCounts.Count > 0)
                aggregateRow.PlayerItemCounts = aggregatedPlayerCounts;
            
            // Aggregate RetainerBreakdown from all source rows
            var aggregatedRetainerBreakdown = new Dictionary<(ulong RetainerId, string Name), Dictionary<uint, long>>();
            foreach (var sourceRow in rows)
            {
                if (sourceRow.RetainerBreakdown != null)
                {
                    foreach (var (retainerKey, counts) in sourceRow.RetainerBreakdown)
                    {
                        if (!aggregatedRetainerBreakdown.TryGetValue(retainerKey, out var retainerCounts))
                        {
                            retainerCounts = new Dictionary<uint, long>();
                            aggregatedRetainerBreakdown[retainerKey] = retainerCounts;
                        }
                        foreach (var kvp in counts)
                        {
                            if (retainerCounts.TryGetValue(kvp.Key, out var existing))
                                retainerCounts[kvp.Key] = existing + kvp.Value;
                            else
                                retainerCounts[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            if (aggregatedRetainerBreakdown.Count > 0)
                aggregateRow.RetainerBreakdown = aggregatedRetainerBreakdown;
            
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
            
            // Aggregate PlayerItemCounts from all source rows in this group
            var aggregatedPlayerCounts = new Dictionary<uint, long>();
            foreach (var sourceRow in group)
            {
                if (sourceRow.PlayerItemCounts != null)
                {
                    foreach (var kvp in sourceRow.PlayerItemCounts)
                    {
                        if (aggregatedPlayerCounts.TryGetValue(kvp.Key, out var existing))
                            aggregatedPlayerCounts[kvp.Key] = existing + kvp.Value;
                        else
                            aggregatedPlayerCounts[kvp.Key] = kvp.Value;
                    }
                }
            }
            if (aggregatedPlayerCounts.Count > 0)
                aggregateRow.PlayerItemCounts = aggregatedPlayerCounts;
            
            // Aggregate RetainerBreakdown from all source rows in this group
            var aggregatedRetainerBreakdown = new Dictionary<(ulong RetainerId, string Name), Dictionary<uint, long>>();
            foreach (var sourceRow in group)
            {
                if (sourceRow.RetainerBreakdown != null)
                {
                    foreach (var (retainerKey, counts) in sourceRow.RetainerBreakdown)
                    {
                        if (!aggregatedRetainerBreakdown.TryGetValue(retainerKey, out var retainerCounts))
                        {
                            retainerCounts = new Dictionary<uint, long>();
                            aggregatedRetainerBreakdown[retainerKey] = retainerCounts;
                        }
                        foreach (var kvp in counts)
                        {
                            if (retainerCounts.TryGetValue(kvp.Key, out var existing))
                                retainerCounts[kvp.Key] = existing + kvp.Value;
                            else
                                retainerCounts[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            if (aggregatedRetainerBreakdown.Count > 0)
                aggregateRow.RetainerBreakdown = aggregatedRetainerBreakdown;
            
            result.Add(aggregateRow);
        }
        
        return result;
    }
    
    private static string FormatNumber(long value, NumberFormatConfig? config) => MTTableHelpers.FormatNumber(value, config);
    
    /// <summary>
    /// Draws text in a table cell with the specified alignment.
    /// </summary>
    private static void DrawAlignedCellText(
        string text, 
        Vector4? color, 
        MTTableHorizontalAlignment hAlign, 
        MTTableVerticalAlignment vAlign) => MTTableHelpers.DrawAlignedCellText(text, hAlign, vAlign, color);
    
    /// <summary>
    /// Draws a header cell with alignment and sorting support.
    /// </summary>
    private static void DrawAlignedHeaderCell(
        string label,
        MTTableHorizontalAlignment hAlign,
        MTTableVerticalAlignment vAlign,
        int columnIndex,
        bool sortable) => MTTableHelpers.DrawAlignedHeaderCell(label, hAlign, vAlign, sortable);
    
}