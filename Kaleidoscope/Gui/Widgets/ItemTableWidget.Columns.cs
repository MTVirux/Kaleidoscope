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
    
    private class DisplayColumn
    {
        public bool IsMerged { get; init; }
        public string Header { get; init; } = string.Empty;
        public float Width { get; init; }
        public Vector4? Color { get; init; }
        public List<int> SourceColumnIndices { get; init; } = new();
        public MergedColumnGroup? MergedGroup { get; init; }
    }
    
    /// <summary>
    /// Builds the list of display columns, combining individual columns and merged groups.
    /// </summary>
    private List<DisplayColumn> BuildDisplayColumns(IReadOnlyList<ItemColumnConfig> columns, IItemTableWidgetSettings settings, float autoWidth)
    {
        var displayColumns = new List<DisplayColumn>();
        var mergedIndices = new HashSet<int>();
        
        // First, collect all indices that are part of a merged group
        foreach (var group in settings.MergedColumnGroups)
        {
            foreach (var idx in group.ColumnIndices)
            {
                mergedIndices.Add(idx);
            }
        }
        
        // Track which merged groups we've already added (by their first column index)
        var addedMergedGroups = new HashSet<int>();
        
        // Iterate through all columns in order
        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            
            // Skip columns not visible in table view
            if (!column.ShowInTable)
                continue;
            
            if (mergedIndices.Contains(i))
            {
                // This column is part of a merged group - find which one
                var group = settings.MergedColumnGroups.FirstOrDefault(g => g.ColumnIndices.Contains(i));
                if (group != null)
                {
                    // Skip if the merged group itself is hidden
                    if (!group.ShowInTable)
                        continue;
                    
                    // Only add the merged group once (at the position of its first column that's visible)
                    // Get the first visible column index in this group
                    var firstVisibleIdx = group.ColumnIndices
                        .Where(idx => idx >= 0 && idx < columns.Count && columns[idx].ShowInTable)
                        .DefaultIfEmpty(-1)
                        .Min();
                    
                    if (firstVisibleIdx >= 0 && i == firstVisibleIdx && !addedMergedGroups.Contains(group.ColumnIndices.Min()))
                    {
                        addedMergedGroups.Add(group.ColumnIndices.Min());
                        // Only include visible columns in the merged group
                        var visibleIndices = group.ColumnIndices
                            .Where(idx => idx >= 0 && idx < columns.Count && columns[idx].ShowInTable)
                            .ToList();
                        
                        if (visibleIndices.Count > 0)
                        {
                            displayColumns.Add(new DisplayColumn
                            {
                                IsMerged = true,
                                Header = group.Name,
                                Width = settings.AutoSizeEqualColumns ? autoWidth : group.Width,
                                Color = group.Color,
                                SourceColumnIndices = visibleIndices,
                                MergedGroup = group
                            });
                        }
                    }
                    // Skip other columns in the same merged group
                }
            }
            else
            {
                // Regular column (not merged)
                displayColumns.Add(new DisplayColumn
                {
                    IsMerged = false,
                    Header = GetColumnHeader(column),
                    Width = settings.AutoSizeEqualColumns ? autoWidth : column.Width,
                    Color = column.Color,
                    SourceColumnIndices = new List<int> { i },
                    MergedGroup = null
                });
            }
        }
        
        return displayColumns;
    }
    
    /// <summary>
    /// Calculates the summed value for a display column from a character row.
    /// </summary>
    private static long GetDisplayColumnValue(DisplayColumn displayCol, ItemTableCharacterRow row, IReadOnlyList<ItemColumnConfig> columns)
    {
        long sum = 0;
        foreach (var idx in displayCol.SourceColumnIndices)
        {
            if (idx >= 0 && idx < columns.Count)
            {
                var colId = columns[idx].Id;
                if (row.ItemCounts.TryGetValue(colId, out var count))
                {
                    sum += count;
                }
            }
        }
        return sum;
    }
    
    /// <summary>
    /// Represents a row to display, either a single character or a merged group.
    /// </summary>
    private class DisplayRow
    {
        public bool IsMerged { get; init; }
        public string Name { get; init; } = string.Empty;
        public Vector4? Color { get; init; }
        /// <summary>Character IDs that this display row represents.</summary>
        public List<ulong> SourceCharacterIds { get; init; } = new();
        /// <summary>The merged group reference (if IsMerged is true).</summary>
        public MergedRowGroup? MergedGroup { get; init; }
        /// <summary>Aggregated item counts from all source rows.</summary>
        public Dictionary<uint, long> ItemCounts { get; init; } = new();
        /// <summary>Player-only item counts (for retainer breakdown display).</summary>
        public Dictionary<uint, long>? PlayerItemCounts { get; init; }
        /// <summary>Retainer breakdown data from the source character row.</summary>
        public Dictionary<(ulong RetainerId, string Name), Dictionary<uint, long>>? RetainerBreakdown { get; init; }
        /// <summary>Whether this row has retainer breakdown data available.</summary>
        public bool HasRetainerData => RetainerBreakdown != null && RetainerBreakdown.Count > 0;
    }
    
    /// <summary>
    /// Builds the list of display rows, combining individual rows and merged groups.
    /// Supports both Character-mode (CharacterIds) and grouped-mode (GroupKeys) merging.
    /// </summary>
    private List<DisplayRow> BuildDisplayRows(IReadOnlyList<ItemTableCharacterRow> rows, IItemTableWidgetSettings settings, IReadOnlyList<ItemColumnConfig> columns)
    {
        var displayRows = new List<DisplayRow>();
        var groupingMode = settings.GroupingMode;
        var isCharacterMode = groupingMode == TableGroupingMode.Character;
        
        if (isCharacterMode)
        {
            // Character mode: merge by CharacterIds
            var mergedCharacterIds = new HashSet<ulong>();
            
            // First, collect all character IDs that are part of a merged group (only Character-mode groups)
            foreach (var group in settings.MergedRowGroups.Where(g => g.GroupingMode == TableGroupingMode.Character))
            {
                foreach (var cid in group.CharacterIds)
                {
                    mergedCharacterIds.Add(cid);
                }
            }
            
            // Track which merged groups we've already added
            var addedMergedGroups = new HashSet<MergedRowGroup>();
            
            // Iterate through all rows in order
            foreach (var row in rows)
            {
                if (mergedCharacterIds.Contains(row.CharacterId))
                {
                    // This row is part of a merged group - find which one
                    var group = settings.MergedRowGroups.FirstOrDefault(g => 
                        g.GroupingMode == TableGroupingMode.Character && g.CharacterIds.Contains(row.CharacterId));
                    if (group != null && !addedMergedGroups.Contains(group))
                    {
                        addedMergedGroups.Add(group);
                        
                        // Aggregate item counts from all characters in this merged group
                        var aggregatedCounts = new Dictionary<uint, long>();
                        foreach (var cid in group.CharacterIds)
                        {
                            var sourceRow = rows.FirstOrDefault(r => r.CharacterId == cid);
                            if (sourceRow != null)
                            {
                                foreach (var kvp in sourceRow.ItemCounts)
                                {
                                    if (aggregatedCounts.TryGetValue(kvp.Key, out var existing))
                                        aggregatedCounts[kvp.Key] = existing + kvp.Value;
                                    else
                                        aggregatedCounts[kvp.Key] = kvp.Value;
                                }
                            }
                        }
                        
                        displayRows.Add(new DisplayRow
                        {
                            IsMerged = true,
                            Name = group.Name,
                            Color = group.Color,
                            SourceCharacterIds = group.CharacterIds.ToList(),
                            MergedGroup = group,
                            ItemCounts = aggregatedCounts
                        });
                    }
                    // Skip other rows in the same merged group
                }
                else
                {
                    // Regular row (not merged)
                    displayRows.Add(new DisplayRow
                    {
                        IsMerged = false,
                        Name = row.Name,
                        Color = null,
                        SourceCharacterIds = new List<ulong> { row.CharacterId },
                        MergedGroup = null,
                        ItemCounts = row.ItemCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                        PlayerItemCounts = row.PlayerItemCounts?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                        RetainerBreakdown = row.RetainerBreakdown
                    });
                }
            }
        }
        else
        {
            // Grouped mode (World/DC/Region/All): merge by GroupKeys (row.Name)
            var mergedGroupKeys = new HashSet<string>();
            
            // Collect all group keys that are part of a merged group (matching current mode)
            foreach (var group in settings.MergedRowGroups.Where(g => g.GroupingMode == groupingMode))
            {
                foreach (var key in group.GroupKeys)
                {
                    mergedGroupKeys.Add(key);
                }
            }
            
            // Track which merged groups we've already added
            var addedMergedGroups = new HashSet<MergedRowGroup>();
            
            // Iterate through all rows in order
            foreach (var row in rows)
            {
                if (mergedGroupKeys.Contains(row.Name))
                {
                    // This row is part of a merged group - find which one
                    var group = settings.MergedRowGroups.FirstOrDefault(g => 
                        g.GroupingMode == groupingMode && g.GroupKeys.Contains(row.Name));
                    if (group != null && !addedMergedGroups.Contains(group))
                    {
                        addedMergedGroups.Add(group);
                        
                        // Aggregate item counts from all group keys in this merged group
                        var aggregatedCounts = new Dictionary<uint, long>();
                        foreach (var key in group.GroupKeys)
                        {
                            var sourceRow = rows.FirstOrDefault(r => r.Name == key);
                            if (sourceRow != null)
                            {
                                foreach (var kvp in sourceRow.ItemCounts)
                                {
                                    if (aggregatedCounts.TryGetValue(kvp.Key, out var existing))
                                        aggregatedCounts[kvp.Key] = existing + kvp.Value;
                                    else
                                        aggregatedCounts[kvp.Key] = kvp.Value;
                                }
                            }
                        }
                        
                        displayRows.Add(new DisplayRow
                        {
                            IsMerged = true,
                            Name = group.Name,
                            Color = group.Color,
                            SourceCharacterIds = new List<ulong>(), // No individual char IDs in grouped mode
                            MergedGroup = group,
                            ItemCounts = aggregatedCounts
                        });
                    }
                    // Skip other rows in the same merged group
                }
                else
                {
                    // Regular row (not merged)
                    displayRows.Add(new DisplayRow
                    {
                        IsMerged = false,
                        Name = row.Name,
                        Color = null,
                        SourceCharacterIds = new List<ulong>(),
                        MergedGroup = null,
                        ItemCounts = row.ItemCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                        PlayerItemCounts = row.PlayerItemCounts?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                        RetainerBreakdown = row.RetainerBreakdown
                    });
                }
            }
        }
        
        return displayRows;
    }
    
    /// <summary>
    /// Calculates the summed value for a display column from a display row.
    /// </summary>
    private static long GetDisplayValue(DisplayColumn displayCol, DisplayRow displayRow, IReadOnlyList<ItemColumnConfig> columns)
    {
        long sum = 0;
        foreach (var idx in displayCol.SourceColumnIndices)
        {
            if (idx >= 0 && idx < columns.Count)
            {
                var colId = columns[idx].Id;
                if (displayRow.ItemCounts.TryGetValue(colId, out var count))
                {
                    sum += count;
                }
            }
        }
        return sum;
    }
    
    /// <summary>
    /// Calculates the summed value for a display column from a raw item counts dictionary.
    /// Used for retainer breakdown sub-rows.
    /// </summary>
    private static long GetDisplayValueFromCounts(DisplayColumn displayCol, Dictionary<uint, long> itemCounts, IReadOnlyList<ItemColumnConfig> columns)
    {
        long sum = 0;
        foreach (var idx in displayCol.SourceColumnIndices)
        {
            if (idx >= 0 && idx < columns.Count)
            {
                var colId = columns[idx].Id;
                if (itemCounts.TryGetValue(colId, out var count))
                {
                    sum += count;
                }
            }
        }
        return sum;
    }
    
    /// <summary>
    /// Gets the set of currently selected column indices (data columns only, 0-indexed).
    /// </summary>
    public IReadOnlySet<int> SelectedColumnIndices => _selectedColumnIndices;
    
    /// <summary>
    /// Gets the set of currently selected row character IDs.
    /// </summary>
    public IReadOnlySet<ulong> SelectedRowIds => _selectedRowIds;
    
    /// <summary>
    /// Gets the effective color for a column based on the TextColorMode setting.
    /// </summary>
    /// <param name="column">The column config (may be null for merged columns).</param>
    /// <param name="displayCol">The display column.</param>
    /// <param name="settings">The table settings.</param>
    /// <param name="columns">All column configurations.</param>
    /// <returns>The effective color to use, or null if no color should be applied.</returns>
    private Vector4? GetEffectiveColumnColor(ItemColumnConfig? column, DisplayColumn displayCol, IItemTableWidgetSettings settings, IReadOnlyList<ItemColumnConfig> columns)
    {
        // If text color mode is DontUse, just return the custom column color
        if (settings.TextColorMode == TableTextColorMode.DontUse)
            return displayCol.Color;
        
        // PreferredItemColors mode - use item colors from configuration
        if (settings.TextColorMode == TableTextColorMode.PreferredItemColors && _configuration != null)
        {
            // For merged columns, use the first source column's item/currency
            var sourceIdx = displayCol.SourceColumnIndices.FirstOrDefault(-1);
            if (sourceIdx >= 0 && sourceIdx < columns.Count)
            {
                var sourceCol = columns[sourceIdx];
                if (sourceCol.IsCurrency)
                {
                    // Check ItemColors (TrackedDataType -> uint)
                    var dataType = (Models.TrackedDataType)sourceCol.Id;
                    if (_configuration.ItemColors.TryGetValue(dataType, out var colorUint))
                        return ColorUtils.UintToVector4(colorUint);
                }
                else
                {
                    // Check GameItemColors (item ID -> uint)
                    if (_configuration.GameItemColors.TryGetValue(sourceCol.Id, out var colorUint))
                        return ColorUtils.UintToVector4(colorUint);
                }
            }
        }
        
        // Fallback to custom column color if preferred color not found
        return displayCol.Color;
    }
    
    /// <summary>
    /// Gets the effective color for a character/row based on the TextColorMode setting.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="settings">The table settings.</param>
    /// <param name="fallbackColor">The fallback color to use (e.g., row color or character column color).</param>
    /// <returns>The effective color to use, or null if no color should be applied.</returns>
    private Vector4? GetEffectiveCharacterColor(ulong characterId, IItemTableWidgetSettings settings, Vector4? fallbackColor)
    {
        // If text color mode is PreferredCharacterColors, use character colors from cache
        if (settings.TextColorMode == TableTextColorMode.PreferredCharacterColors && _cacheService != null)
        {
            var charColor = _cacheService.GetCharacterTimeSeriesColor(characterId);
            if (charColor.HasValue)
                return ColorUtils.UintToVector4(charColor.Value);
        }
        
        return fallbackColor;
    }
    
}