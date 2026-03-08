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

/// <summary>
/// Represents a display column in the item table, which may be a single column or a merged group.
/// </summary>
internal sealed class DisplayColumn
{
    public bool IsMerged { get; init; }
    public string Header { get; init; } = string.Empty;
    public float Width { get; init; }
    public Vector4? Color { get; init; }
    public List<int> SourceColumnIndices { get; init; } = new();
    public MergedColumnGroup? MergedGroup { get; init; }
}

/// <summary>
/// Represents a display row in the item table, either a single character or a merged group.
/// </summary>
internal sealed class DisplayRow
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

public sealed partial class ItemTableWidget
{
    
    /// <summary>
    /// Builds the list of display columns, combining individual columns and merged groups.
    /// Items are sorted by display order to allow interleaving merged groups with individual columns.
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
        
        // Build a unified list with display orders
        var orderedItems = new List<(int displayOrder, DisplayColumn column)>();
        
        // Add merged groups with their display orders
        foreach (var group in settings.MergedColumnGroups)
        {
            // Skip if the merged group is hidden
            if (!group.ShowInTable)
                continue;
            
            // Only include visible columns in the merged group
            var visibleIndices = group.ColumnIndices
                .Where(idx => idx >= 0 && idx < columns.Count && columns[idx].ShowInTable)
                .ToList();
            
            if (visibleIndices.Count > 0)
            {
                // Use DisplayOrder if set (-1 is sentinel for "auto"), otherwise use minimum column index * 10
                var displayOrder = group.DisplayOrder != -1 
                    ? group.DisplayOrder 
                    : group.ColumnIndices.Min() * 10;
                
                orderedItems.Add((displayOrder, new DisplayColumn
                {
                    IsMerged = true,
                    Header = group.Name,
                    Width = settings.AutoSizeEqualColumns ? autoWidth : group.Width,
                    Color = group.Color,
                    SourceColumnIndices = visibleIndices,
                    MergedGroup = group
                }));
            }
        }
        
        // Add individual (non-merged) columns with their display orders
        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            
            // Skip columns not visible in table view
            if (!column.ShowInTable)
                continue;
            
            // Skip columns that are part of a merged group
            if (mergedIndices.Contains(i))
                continue;
            
            // Regular column - display order is index * 10
            orderedItems.Add((i * 10, new DisplayColumn
            {
                IsMerged = false,
                Header = GetColumnHeader(column),
                Width = settings.AutoSizeEqualColumns ? autoWidth : column.Width,
                Color = column.Color,
                SourceColumnIndices = new List<int> { i },
                MergedGroup = null
            }));
        }
        
        // Sort by display order and extract columns
        orderedItems.Sort((a, b) => a.displayOrder.CompareTo(b.displayOrder));
        displayColumns = orderedItems.Select(x => x.column).ToList();
        
        return displayColumns;
    }
    
    /// <summary>
    /// Calculates the summed value for a display column from a character row.
    /// </summary>
    private static long GetDisplayColumnValue(DisplayColumn displayCol, ItemTableCharacterRow row, IReadOnlyList<ItemColumnConfig> columns)
        => GetDisplayValueFromCounts(displayCol, row.ItemCounts, columns);
    
    /// <summary>
    /// Builds the list of display rows, combining individual rows and merged groups.
    /// Supports both Character-mode (CharacterIds) and grouped-mode (GroupKeys) merging.
    /// </summary>
    private List<DisplayRow> BuildDisplayRows(IReadOnlyList<ItemTableCharacterRow> rows, IItemTableWidgetSettings settings, IReadOnlyList<ItemColumnConfig> columns)
    {
        var groupingMode = settings.GroupingMode;
        var isCharacterMode = groupingMode == TableGroupingMode.Character;
        
        // Abstraction delegates — same algorithm, different key types
        // Character mode: key = CharacterId (ulong), merge items = group.CharacterIds
        // Grouped mode:   key = Name (string),       merge items = group.GroupKeys
        if (isCharacterMode)
        {
            return BuildDisplayRowsCore(
                rows, settings.MergedRowGroups, groupingMode,
                row => row.CharacterId,
                group => group.CharacterIds.Cast<object>(),
                (row, key) => row.CharacterId.Equals(key),
                group => group.CharacterIds.Select(c => c).ToList(),
                isCharacterMode: true);
        }
        else
        {
            return BuildDisplayRowsCore(
                rows, settings.MergedRowGroups, groupingMode,
                row => row.Name,
                group => group.GroupKeys.Cast<object>(),
                (row, key) => row.Name == (string)key,
                _ => new List<ulong>(),
                isCharacterMode: false);
        }
    }
    
    /// <summary>
    /// Core implementation for building display rows with merge support.
    /// Abstracts over Character-mode (ulong keys) and Grouped-mode (string keys).
    /// </summary>
    private List<DisplayRow> BuildDisplayRowsCore<TKey>(
        IReadOnlyList<ItemTableCharacterRow> rows,
        List<MergedRowGroup> mergedRowGroups,
        TableGroupingMode groupingMode,
        Func<ItemTableCharacterRow, TKey> getRowKey,
        Func<MergedRowGroup, IEnumerable<object>> getMergeKeys,
        Func<ItemTableCharacterRow, object, bool> rowMatchesKey,
        Func<MergedRowGroup, List<ulong>> getSourceCharacterIds,
        bool isCharacterMode)
        where TKey : notnull
    {
        var displayRows = new List<DisplayRow>();
        
        // Build set of merged keys
        var mergedKeys = new HashSet<TKey>();
        foreach (var group in mergedRowGroups.Where(g => g.GroupingMode == groupingMode))
            foreach (var key in getMergeKeys(group))
                mergedKeys.Add((TKey)key);
        
        var addedMergedGroups = new HashSet<MergedRowGroup>();
        
        foreach (var row in rows)
        {
            var rowKey = getRowKey(row);
            if (mergedKeys.Contains(rowKey))
            {
                // Find which merged group this row belongs to
                var group = mergedRowGroups.FirstOrDefault(g =>
                    g.GroupingMode == groupingMode && getMergeKeys(g).Any(k => k.Equals(rowKey)));
                if (group != null && !addedMergedGroups.Contains(group))
                {
                    addedMergedGroups.Add(group);
                    
                    // Aggregate item counts from all members of this merged group
                    var aggregatedCounts = new Dictionary<uint, long>();
                    foreach (var key in getMergeKeys(group))
                    {
                        var sourceRow = rows.FirstOrDefault(r => rowMatchesKey(r, key));
                        if (sourceRow != null)
                            MergeDictionaryAdditive(aggregatedCounts, sourceRow.ItemCounts);
                    }
                    
                    displayRows.Add(new DisplayRow
                    {
                        IsMerged = true,
                        Name = group.Name,
                        Color = group.Color,
                        SourceCharacterIds = getSourceCharacterIds(group),
                        MergedGroup = group,
                        ItemCounts = aggregatedCounts
                    });
                }
            }
            else
            {
                displayRows.Add(new DisplayRow
                {
                    IsMerged = false,
                    Name = row.Name,
                    Color = null,
                    SourceCharacterIds = isCharacterMode ? new List<ulong> { row.CharacterId } : new List<ulong>(),
                    MergedGroup = null,
                    ItemCounts = row.ItemCounts,
                    PlayerItemCounts = row.PlayerItemCounts,
                    RetainerBreakdown = row.RetainerBreakdown
                });
            }
        }
        
        return displayRows;
    }
    
    /// <summary>
    /// Calculates the summed value for a display column from a display row.
    /// </summary>
    private static long GetDisplayValue(DisplayColumn displayCol, DisplayRow displayRow, IReadOnlyList<ItemColumnConfig> columns)
        => GetDisplayValueFromCounts(displayCol, displayRow.ItemCounts, columns);
    
    /// <summary>
    /// Calculates the summed value for a display column from a raw item counts dictionary.
    /// This is the single canonical implementation — all other display value methods delegate here.
    /// </summary>
    private static long GetDisplayValueFromCounts(DisplayColumn displayCol, IReadOnlyDictionary<uint, long> itemCounts, IReadOnlyList<ItemColumnConfig> columns)
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
    public IReadOnlySet<int> SelectedColumnIndices => _selectedDisplayColumnIndices;
    
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