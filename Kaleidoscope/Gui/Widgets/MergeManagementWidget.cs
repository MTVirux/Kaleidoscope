using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Widget for managing source merging (characters/rows).
/// Item/currency merge is now integrated directly into ColumnManagementWidget.
/// </summary>
public static class MergeManagementWidget
{
    // Selection state for character merge operations (keyed by widget instance ID)
    private static readonly Dictionary<string, HashSet<ulong>> _selectedCharacterIds = new();
    
    // Selection state for group key merge operations (keyed by widget instance ID)
    private static readonly Dictionary<string, HashSet<string>> _selectedGroupKeys = new();
    
    /// <summary>
    /// Draws the merged sources management section.
    /// Now supports both Character-mode (character IDs) and grouped-mode (group keys).
    /// </summary>
    public static bool DrawMergedRows(
        List<MergedRowGroup> mergedRowGroups,
        TableGroupingMode groupingMode,
        Func<ulong, string> getCharacterName,
        IEnumerable<ulong>? availableCharacterIds = null,
        IEnumerable<string>? availableGroupKeys = null,
        Action? onSettingsChanged = null,
        Action? onRefreshNeeded = null,
        string widgetId = "default")
    {
        ImGui.Spacing();
        ImGui.Spacing();
        
        if (!TreeHelpers.DrawCollapsingSection("Source Merging", false))
            return false;
        
        var isCharacterMode = groupingMode == TableGroupingMode.Character;
        
        bool changed;
        if (isCharacterMode)
        {
            if (!_selectedCharacterIds.TryGetValue(widgetId, out var selectedIds))
            {
                selectedIds = new HashSet<ulong>();
                _selectedCharacterIds[widgetId] = selectedIds;
            }
            
            changed = DrawMergeMode(
                mergedRowGroups, TableGroupingMode.Character, availableCharacterIds,
                g => g.CharacterIds, (g, items) => g.CharacterIds = items,
                getCharacterName, id => (int)id, selectedIds, null, onRefreshNeeded);
        }
        else
        {
            var keyId = $"{widgetId}_{groupingMode}";
            if (!_selectedGroupKeys.TryGetValue(keyId, out var selectedKeys))
            {
                selectedKeys = new HashSet<string>();
                _selectedGroupKeys[keyId] = selectedKeys;
            }
            
            // Build mode-specific preamble and early exit
            var modeName = groupingMode switch
            {
                TableGroupingMode.World => "World",
                TableGroupingMode.DataCenter => "Data Center",
                TableGroupingMode.Region => "Region",
                TableGroupingMode.All => "All",
                _ => groupingMode.ToString()
            };
            
            Action preamble = () => ImGui.TextDisabled($"Grouping by: {modeName}");
            
            if (groupingMode == TableGroupingMode.All)
            {
                preamble();
                ImGui.TextDisabled("Cannot merge when grouped to 'All' (single row).");
                return false;
            }
            
            changed = DrawMergeMode(
                mergedRowGroups, groupingMode, availableGroupKeys,
                g => g.GroupKeys, (g, items) => g.GroupKeys = items,
                k => k, k => k.GetHashCode(), selectedKeys, preamble, onRefreshNeeded);
        }
        
        if (changed)
        {
            onSettingsChanged?.Invoke();
            onRefreshNeeded?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Generic merge mode UI — handles both character IDs and group keys.
    /// </summary>
    private static bool DrawMergeMode<TId>(
        List<MergedRowGroup> mergedRowGroups,
        TableGroupingMode groupingMode,
        IEnumerable<TId>? availableItems,
        Func<MergedRowGroup, IReadOnlyList<TId>> getGroupItems,
        Action<MergedRowGroup, List<TId>> setGroupItems,
        Func<TId, string> getDisplayName,
        Func<TId, int> getImGuiId,
        HashSet<TId> selectedItems,
        Action? preamble,
        Action? onRefreshNeeded)
        where TId : notnull
    {
        var changed = false;
        
        // Build set of already-merged items (only from matching mode groups)
        var mergedItems = new HashSet<TId>();
        foreach (var group in mergedRowGroups.Where(g => g.GroupingMode == groupingMode))
            foreach (var item in getGroupItems(group))
                mergedItems.Add(item);
        
        var allItems = availableItems?.ToList() ?? new List<TId>();
        
        // Clean up selection — remove items that are now merged or no longer available
        selectedItems.RemoveWhere(id => mergedItems.Contains(id) || !allItems.Contains(id));
        
        preamble?.Invoke();
        
        if (mergedRowGroups.Count(g => g.GroupingMode == groupingMode) == 0 && allItems.Count < 2)
        {
            ImGui.TextDisabled("Need at least 2 sources to enable merging.");
            return false;
        }
        
        int? groupToUnmerge = null;
        
        // === Draw Merged Groups ===
        for (int g = 0; g < mergedRowGroups.Count; g++)
        {
            var group = mergedRowGroups[g];
            if (group.GroupingMode != groupingMode)
                continue;
            changed |= DrawMergedGroupRow(group, g, getGroupItems, getDisplayName, ref groupToUnmerge);
        }
        
        // Show count of groups from other modes
        var otherModeCount = mergedRowGroups.Count(g => g.GroupingMode != groupingMode);
        if (otherModeCount > 0)
            ImGui.TextDisabled($"({otherModeCount} group(s) hidden - created in other Group By modes)");
        
        if (mergedRowGroups.Any(g => g.GroupingMode == groupingMode) && allItems.Count > mergedItems.Count)
            ImGui.Spacing();
        
        // === Draw Individual (Unmerged) Sources ===
        foreach (var item in allItems)
        {
            if (mergedItems.Contains(item))
                continue;
            
            var displayName = getDisplayName(item);
            var isSelected = selectedItems.Contains(item);
            
            ImGui.PushID(getImGuiId(item));
            
            if (ImGui.Checkbox("##select", ref isSelected))
            {
                if (isSelected) selectedItems.Add(item);
                else selectedItems.Remove(item);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Select for merging");
            
            ImGui.SameLine();
            ImGui.TextUnformatted(displayName);
            
            ImGui.PopID();
        }
        
        // === Merge Action Bar ===
        if (selectedItems.Count >= 2)
        {
            ImGui.Spacing();
            if (ImGuiHelpers.SuccessButton($"Merge {selectedItems.Count} Selected"))
            {
                var newGroup = new MergedRowGroup
                {
                    Name = "Merged",
                    GroupingMode = groupingMode
                };
                setGroupItems(newGroup, selectedItems.OrderBy(x => x).ToList());
                mergedRowGroups.Add(newGroup);
                selectedItems.Clear();
                changed = true;
                onRefreshNeeded?.Invoke();
            }
            if (ImGui.IsItemHovered())
            {
                var names = selectedItems.Select(getDisplayName).ToList();
                ImGui.SetTooltip($"Merge:\n{string.Join("\n", names)}");
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Clear Selection"))
                selectedItems.Clear();
        }
        else if (selectedItems.Count == 1)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select at least 2 sources to merge");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
                selectedItems.Clear();
        }
        
        // === Process Deferred Actions ===
        if (groupToUnmerge.HasValue)
        {
            mergedRowGroups.RemoveAt(groupToUnmerge.Value);
            changed = true;
            onRefreshNeeded?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws a single merged group row (generic — works for both character IDs and group keys).
    /// </summary>
    private static bool DrawMergedGroupRow<TId>(
        MergedRowGroup group,
        int groupIndex,
        Func<MergedRowGroup, IReadOnlyList<TId>> getGroupItems,
        Func<TId, string> getDisplayName,
        ref int? groupToUnmerge)
    {
        var changed = false;
        var items = getGroupItems(group);
        
        ImGui.PushID($"mergedrow_{groupIndex}");

        // Merge indicator
        MergedGroupChrome.DrawMergeIndicator();
        ImGui.SameLine();
        
        // Color picker
        var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithClear(
            "##color", group.Color, ImGuiHelpers.DefaultColor, "Merged group color");
        if (colorChanged)
        {
            group.Color = newColor;
            changed = true;
        }
        
        ImGui.SameLine();
        
        // Editable name
        ImGui.SetNextItemWidth(120);
        var name = group.Name;
        if (ImGui.InputTextWithHint("##name", "Merged", ref name, 64))
        {
            group.Name = name;
            changed = true;
        }
        
        ImGui.SameLine();

        // Show merged sources count
        MergedGroupChrome.DrawMergedCountLabel(items.Count, items.Select(getDisplayName));

        ImGui.SameLine(0, 16);

        // Unmerge button
        if (MergedGroupChrome.DrawUnmergeButton("Unmerge back to individual sources"))
            groupToUnmerge = groupIndex;

        ImGui.PopID();
        
        return changed;
    }
}
